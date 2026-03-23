namespace HPDAgent.Graph.Abstractions.Checkpointing;

/// <summary>
/// Metadata about a node's saved state for checkpoint restoration.
/// Includes version information for compatibility checking.
/// </summary>
public sealed record NodeStateMetadata
{
    /// <summary>
    /// The node ID this state belongs to.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// The node's version at time of checkpoint.
    /// Used for compatibility checking during resume.
    /// If version doesn't match current node version, state is discarded with a warning.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The serialized state data (node outputs).
    /// JSON-serialized dictionary of output values.
    /// </summary>
    public required string StateJson { get; init; }

    /// <summary>
    /// When this state was captured.
    /// </summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
