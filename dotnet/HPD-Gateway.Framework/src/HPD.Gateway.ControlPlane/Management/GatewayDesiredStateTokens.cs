using System.Security.Cryptography;
using System.Text;

namespace HPD.Gateway.ControlPlane;

internal static class GatewayDesiredStateTokens
{
    internal static string Create(GatewayDesiredState desired, GatewayManagementRuntimeOptions options)
    {
        string payload = $"v1\n{desired.ManagementAuthorityId}\n{desired.NamespaceId}\n{desired.TargetNodeId}\n{desired.ActivationIntentId}\n{desired.RevisionId}\n{desired.CandidateId}";
        byte[] signature = HMACSHA256.HashData(options.GetTokenKey(), Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)) + "." + Convert.ToHexStringLower(signature);
    }
}
