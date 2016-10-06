using System;
using System.Threading.Tasks;

namespace Thinktecture.Relay.OnPremiseConnector.OnPremiseTarget
{
	internal interface IOnPremiseTargetConnector
	{
		Uri BaseUri { get; }
        Task<IOnPremiseTargetResponse> GetResponseAsync(string url, IOnPremiseTargetRequest request);
    }
}