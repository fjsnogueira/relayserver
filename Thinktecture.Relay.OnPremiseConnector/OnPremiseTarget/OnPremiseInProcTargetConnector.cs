using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog.Interface;

namespace Thinktecture.Relay.OnPremiseConnector.OnPremiseTarget
{
    internal class OnPremiseInProcTargetConnector : IOnPremiseTargetConnector
    {
        private readonly Type _handlerType;
        private readonly int _requestTimeout;
        private readonly ILogger _logger;

        public OnPremiseInProcTargetConnector(Type handlerType, int requestTimeout, ILogger logger)
        {
            _handlerType = handlerType;
            _requestTimeout = requestTimeout;
            _logger = logger;
        }

        public async Task<IOnPremiseTargetResponse> GetResponseAsync(string url, IOnPremiseTargetRequest request)
        {
            _logger.Debug("Requesting response from on-premise in-proc target");
            _logger.Trace("Requesting response from on-premise in-proc target. request-id={0}, url={1}, origin-id={2}", request.RequestId, url, request.OriginId);

            var response = new OnPremiseTargetResponse()
            {
                RequestId = request.RequestId,
                OriginId = request.OriginId,
                RequestStarted = DateTime.UtcNow
            };

            try
            {
                var handler = (IOnPremiseInProcHandler) Activator.CreateInstance(_handlerType);

                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_requestTimeout)))
                    {
                        await handler.ProcessRequest(request, response, cts.Token);

                        if (cts.IsCancellationRequested)
                        {
                            _logger.Warn("Gateway timeout");
                            _logger.Trace("Gateway timeout. request-id={0}", request.RequestId);

                            response.StatusCode = HttpStatusCode.GatewayTimeout;
                            response.HttpHeaders = new Dictionary<string, string>()
                            {
                                { "X-TTRELAY-TIMEOUT", "On-Premise Target" }
                            };
                            response.Body = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Trace("Error requesting response. request-id={0}", ex, request.RequestId);

                    response.StatusCode = HttpStatusCode.InternalServerError;
                    response.HttpHeaders = new Dictionary<string, string>()
                    {
                        { "Content-Type", "text/plain" }
                    };
                    response.Body = Encoding.UTF8.GetBytes(ex.ToString());
                }
                finally
                {
                    var disposable = handler as IDisposable;
                    if (disposable != null)
                    {
                        disposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Trace("Error creating handler. request-id={0}", ex, request.RequestId);

                response.StatusCode = HttpStatusCode.InternalServerError;
                response.HttpHeaders = new Dictionary<string, string>()
                {
                    { "Content-Type", "text/plain" }
                };
                response.Body = Encoding.UTF8.GetBytes(ex.ToString());
            }

            response.RequestFinished = DateTime.UtcNow;

            _logger.Trace("Got response. request-id={0}, status-code={1}", response.RequestId, response.StatusCode);

            return response;
        }
    }
}