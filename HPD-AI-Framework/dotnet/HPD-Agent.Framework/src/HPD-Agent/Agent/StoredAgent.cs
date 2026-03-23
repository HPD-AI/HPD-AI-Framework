namespace HPD.Agent;

/// <summary>
/// Identity envelope for an <see cref="AgentConfig"/>.
/// Gives the config a stable ID, name, and lifecycle timestamps so it can be
/// stored, retrieved, and referenced at runtime without embedding identity
/// inside the config itself.
/// </summary>
public class StoredAgent
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AgentConfig Config { get; set; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}
