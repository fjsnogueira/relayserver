using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Thinktecture.Relay.OnPremiseConnector.OnPremiseTarget;
using Thinktecture.Relay.Server.OnPremise;

namespace Thinktecture.Relay.Server.Communication
{
	public interface IBackendCommunication : IDisposable
	{
		string OriginId { get; }
		Task<IOnPremiseTargetResponse> GetResponseAsync(string requestId);
		Task SendOnPremiseConnectorRequest(string onPremiseId, IOnPremiseConnectorRequest onPremiseConnectorRequest);
	    void RegisterOnPremise(RegistrationInformation registrationInformation);
		void UnregisterOnPremise(string connectionId);
		Task SendOnPremiseTargetResponse(string originId, IOnPremiseTargetResponse response);
	    bool IsRegistered(string connectionId);
	    List<string> GetConnections(string linkId);
	}
}