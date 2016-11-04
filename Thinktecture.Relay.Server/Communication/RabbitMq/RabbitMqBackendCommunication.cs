﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyNetQ;
using EasyNetQ.Topology;
using Newtonsoft.Json;
using NLog;
using Thinktecture.Relay.OnPremiseConnector.OnPremiseTarget;
using Thinktecture.Relay.Server.Configuration;
using Thinktecture.Relay.Server.OnPremise;

namespace Thinktecture.Relay.Server.Communication.RabbitMq
{
    internal class RabbitMqBackendCommunication : BackendCommunication
    {
        private static readonly int _expiration = (int) TimeSpan.FromSeconds(10).TotalMilliseconds;

        private readonly IConfiguration _configuration;
        private readonly IOnPremiseConnectorCallbackFactory _onPremiseConnectorCallbackFactory;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, IOnPremiseConnectorCallback> _callbacks;
        private readonly ConcurrentDictionary<string, IDisposable> _onPremiseConsumers;
        private readonly ConcurrentDictionary<string, ConnectionInformation> _onPremises;
        private readonly ConcurrentDictionary<string, IQueue> _declaredQueues;

        private IBus _bus;
        private IDisposable _consumer;

        internal class ConnectionInformation
        {
            public readonly string LinkId;

            public ConnectionInformation(string linkId)
            {
                LinkId = linkId;
            }
        }

        public RabbitMqBackendCommunication(IConfiguration configuration, IRabbitMqBusFactory busFactory, IOnPremiseConnectorCallbackFactory onPremiseConnectorCallbackFactory, ILogger logger) 
            : base(logger)
        {
            _configuration = configuration;
            _onPremiseConnectorCallbackFactory = onPremiseConnectorCallbackFactory;
            _logger = logger;
            _bus = busFactory.CreateBus();

            _callbacks = new ConcurrentDictionary<string, IOnPremiseConnectorCallback>();
            _onPremiseConsumers = new ConcurrentDictionary<string, IDisposable>();
            _onPremises = new ConcurrentDictionary<string, ConnectionInformation>();
            _declaredQueues = new ConcurrentDictionary<string, IQueue>();

            StartReceivingOnPremiseTargetResponses(OriginId);
        }

        public override Task<IOnPremiseTargetResponse> GetResponseAsync(string requestId)
        {
            CheckDisposed();

            _logger.Debug("Waiting for response for request id {0}", requestId);

            var onPremiseConnectorCallback = _onPremiseConnectorCallbackFactory.Create(requestId);
            _callbacks[requestId] = onPremiseConnectorCallback;

            var task = Task<IOnPremiseTargetResponse>.Factory.StartNew(WaitForOnPremiseTargetResponse, onPremiseConnectorCallback);
            return task;
        }

        private IOnPremiseTargetResponse WaitForOnPremiseTargetResponse(object state)
        {
            var onPremiseConnectorCallback = (IOnPremiseConnectorCallback) state;
            if (onPremiseConnectorCallback.Handle.WaitOne(_configuration.OnPremiseConnectorCallbackTimeout))
            {
                _logger.Debug("On-Premise target response for request id {0}", onPremiseConnectorCallback.RequestId);
                return onPremiseConnectorCallback.Response;
            }

            _callbacks.TryRemove(onPremiseConnectorCallback.RequestId, out onPremiseConnectorCallback);
            return null;
        }

        public override async Task SendOnPremiseConnectorRequest(string onPremiseId, IOnPremiseConnectorRequest onPremiseConnectorRequest)
        {
            CheckDisposed();

            _logger.Debug("Sending client request for on-premise connector {0}", onPremiseId);

            var queue = DeclareOnPremiseQueue(onPremiseId);
            await _bus.Advanced.PublishAsync(Exchange.GetDefault(), queue.Name, false, false, new Message<string>(JsonConvert.SerializeObject(onPremiseConnectorRequest)));
        }

        public override void RegisterOnPremise(RegistrationInformation registrationInformation)
        {
            CheckDisposed();

            UnregisterOnPremise(registrationInformation.ConnectionId);

            _logger.Debug("Registering on-premise {0} via connection {1}", registrationInformation.OnPremiseId, registrationInformation.ConnectionId);

            var queue = DeclareOnPremiseQueue(registrationInformation.OnPremiseId);

            var consumer = _bus.Advanced.Consume(queue, (Action<IMessage<string>, MessageReceivedInfo>) ((message, info) => registrationInformation.RequestAction(JsonConvert.DeserializeObject<OnPremiseConnectorRequest>(message.Body))));
            _onPremiseConsumers[registrationInformation.ConnectionId] = consumer;
            _onPremises[registrationInformation.ConnectionId] = new ConnectionInformation(registrationInformation.OnPremiseId);
        }

        private IQueue DeclareOnPremiseQueue(string onPremiseId)
        {
            var queueName = "OnPremises " + onPremiseId;
            return _declaredQueues.GetOrAdd(queueName, DeclareQueue);
        }

        public override void UnregisterOnPremise(string connectionId)
        {
            CheckDisposed();

            ConnectionInformation onPremiseInformation;
            if (_onPremises.TryRemove(connectionId, out onPremiseInformation))
            {
                IQueue queue;
                _declaredQueues.TryRemove("OnPremises " + onPremiseInformation.LinkId, out queue);
                _logger.Debug("Unregistered on-premise connection id {0}", connectionId);
            }

            var onPremiseId = onPremiseInformation == null ? "unknown" : onPremiseInformation.LinkId;
            
            IDisposable consumer;
            if (_onPremiseConsumers.TryRemove(connectionId, out consumer))
            {
                _logger.Debug("Unregistered on-premise consumer {0} for connection id {1}", onPremiseId, connectionId);
                consumer.Dispose();
            }
        }

        public override async Task SendOnPremiseTargetResponse(string originId, IOnPremiseTargetResponse response)
        {
            CheckDisposed();

            _logger.Debug("Sending on-premise target response to origin id {0}", originId);

            var queue = DeclareRelayServerQueue(originId);
            await _bus.Advanced.PublishAsync(Exchange.GetDefault(), queue.Name, false, false, new Message<string>(JsonConvert.SerializeObject(response)));
        }

        public override bool IsRegistered(string connectionId)
        {
            return _onPremises.Any(o => o.Value.LinkId.Equals(connectionId, StringComparison.OrdinalIgnoreCase));
        }

        public override List<string> GetConnections(string linkId)
        {
            return _onPremises.Where(p=>p.Value.LinkId.Equals(linkId, StringComparison.OrdinalIgnoreCase)).Select(p=>p.Key).ToList();
        }

        private void StartReceivingOnPremiseTargetResponses(string originId)
        {
            _logger.Debug("Start receiving on-premise target responses for origin id {0}", originId);

            var queue = DeclareRelayServerQueue(originId);
            _bus.Advanced.Consume(queue, (Action<IMessage<string>, MessageReceivedInfo>) ((message, info) => ForwardOnPremiseTargetResponse(JsonConvert.DeserializeObject<OnPremiseTargetResponse>(message.Body))));
        }

        private IQueue DeclareRelayServerQueue(string originId)
        {
            var queueName = "RelayServer " + originId;
            return _declaredQueues.GetOrAdd(queueName, DeclareQueue);
        }

        private void ForwardOnPremiseTargetResponse(IOnPremiseTargetResponse response)
        {
            _logger.Debug("Forwarding on-premise target response for request id {0}", response.RequestId);

            IOnPremiseConnectorCallback onPremiseConnectorCallback;
            if (_callbacks.TryRemove(response.RequestId, out onPremiseConnectorCallback))
            {
                onPremiseConnectorCallback.Response = response;
                onPremiseConnectorCallback.Handle.Set();
            }
            else
            {
                _logger.Debug("No callback found for request id {0}", response.RequestId);
            }
        }

        private IQueue DeclareQueue(string queueName)
        {
            _logger.Debug("Creating queue {0}", queueName);

            var queue = _bus.Advanced.QueueDeclare(queueName, expires: _expiration);
            return queue;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                if (_consumer != null)
                {
                    _consumer.Dispose();
                    _consumer = null;
                }

                foreach (var consumer in _onPremiseConsumers.Values)
                {
                    consumer.Dispose();
                }

                if (_bus != null)
                {
                    _bus.Dispose();
                    _bus = null;
                }
            }
        }
    }
}
