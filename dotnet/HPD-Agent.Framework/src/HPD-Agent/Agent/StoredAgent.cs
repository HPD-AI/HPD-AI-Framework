namespace HPD.Agent;

/// <summary>
/// Identity envelope for an <see cref="AgentConfig"/>.
/// Gives the config a stable ID, name, and lifecycle timestamps so it can be
/// stored, retrieved, and referenced at runtime without embedding identity
/// inside the config itself.
/// </summary>
public class StoredAgent
{
    private AgentConfig _config = new();

    public string Id { get; init; } = string.Empty;
    public AgentConfig Config
    {
        get => _config;
        set => _config = value ?? throw new ArgumentNullException(nameof(value));
    }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}
