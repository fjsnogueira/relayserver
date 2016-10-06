using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thinktecture.Relay.OnPremiseConnector.SignalR
{
    internal interface IRelayServerConnection : IDisposable
    {
        void RegisterOnPremiseTarget(string key, Uri baseUri);
        void RegisterOnPremiseTarget(string key, Type handlerType);
        void RemoveOnPremiseTarget(string key);
        string RelayedRequestHeader { get; set; }
        Task Connect();
        void Disconnect();
        List<string> GetOnPremiseTargetKeys();
    }
}