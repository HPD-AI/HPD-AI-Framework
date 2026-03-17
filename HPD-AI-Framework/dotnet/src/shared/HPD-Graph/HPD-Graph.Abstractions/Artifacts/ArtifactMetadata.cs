namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Metadata about an artifact version, separate from the artifact value itself.
/// Enables provenance tracking, lineage, and operational metadata.
/// </summary>
public record ArtifactMetadata
{
    /// <summary>
    /// When this artifact version was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Input artifact versions that were used to produce this artifact.
    /// Maps artifact key to version fingerprint.
    /// Enables full lineage tracking.
    /// </summary>
    public required IReadOnlyDictionary<ArtifactKey, string> InputVersions { get; init; }

    /// <summary>
    /// Node ID that produced this artifact (optional).
    /// Null if artifact was externally created.
    /// </summary>
    public string? ProducedByNodeId { get; init; }

    /// <summary>
    /// Execution ID that produced this artifact (optional).
    /// Used for ExecutionId depth calculation in multi-producer resolution.
    /// Example: "exec-123:pipeline:stage1" → depth = 2
    /// </summary>
    public string? ExecutionId { get; init; }

    /// <summary>
    /// Custom metadata for application-specific use.
    /// Examples: data quality metrics, source system info, business metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomMetadata { get; init; }
}
