using System.Security.Cryptography;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>Raised when a stored Agent definition changed after it was reviewed or validated.</summary>
public sealed class AgentStoreConcurrencyException(string agentId)
    : InvalidOperationException($"Agent definition '{agentId}' changed after it was read.")
{
    public string AgentId { get; } = agentId;
}

/// <summary>Computes the opaque content revision used for Agent-definition compare-and-swap.</summary>
public static class AgentDefinitionRevision
{
    public static string Compute(StoredAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(agent, HPDJsonContext.Default.StoredAgent);
        return Convert.ToHexStringLower(SHA256.HashData(json));
    }
}
