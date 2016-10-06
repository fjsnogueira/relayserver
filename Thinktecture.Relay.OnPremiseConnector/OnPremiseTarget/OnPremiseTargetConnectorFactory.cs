using System;
using NLog.Interface;

namespace Thinktecture.Relay.OnPremiseConnector.OnPremiseTarget
{
    internal class OnPremiseTargetConnectorFactory : IOnPremiseTargetConnectorFactory
    {
        private readonly ILogger _logger;

        public OnPremiseTargetConnectorFactory(ILogger logger)
        {
            _logger = logger;
        }

        public IOnPremiseTargetConnector Create(Uri baseUri, int requestTimeout)
        {
            return new OnPremiseWebTargetConnector(baseUri, requestTimeout, _logger);
        }

        public IOnPremiseTargetConnector Create(Type handlerType, int requestTimeout)
        {
            return new OnPremiseInProcTargetConnector(handlerType, requestTimeout, _logger);
        }
    }
}
