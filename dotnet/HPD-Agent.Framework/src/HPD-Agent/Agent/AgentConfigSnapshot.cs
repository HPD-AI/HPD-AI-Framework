using System.Text.Json;

namespace HPD.Agent;

/// <summary>
/// Creates detached, serializable agent-definition snapshots.
/// </summary>
internal static class AgentConfigSnapshot
{
    /// <summary>
    /// Clones definition data while excluding runtime-only members marked with
    /// <see cref="System.Text.Json.Serialization.JsonIgnoreAttribute"/>.
    /// </summary>
    public static AgentConfig Create(AgentConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var json = JsonSerializer.Serialize(source, HPDJsonContext.Default.AgentConfig);
        return JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentConfig)
            ?? throw new InvalidOperationException("Failed to create an AgentConfig snapshot.");
    }
}
