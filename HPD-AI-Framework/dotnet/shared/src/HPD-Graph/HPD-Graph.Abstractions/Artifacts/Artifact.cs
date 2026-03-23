namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Immutable snapshot of a data artifact at a specific version.
/// Version is the fingerprint from INodeFingerprintCalculator (reusing existing infrastructure).
/// Combines the artifact value with its metadata and provenance.
/// </summary>
/// <typeparam name="T">The type of the artifact value.</typeparam>
public record Artifact<T>
{
    /// <summary>
    /// Unique identifier for this artifact (path + optional partition).
    /// </summary>
    public required ArtifactKey Key { get; init; }

    /// <summary>
    /// Version fingerprint from INodeFingerprintCalculator.
    /// Reuses existing fingerprinting infrastructure.
    /// Content-addressable: same inputs → same version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// The actual artifact value.
    /// </summary>
    public required T Value { get; init; }

    /// <summary>
    /// When this artifact version was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Input artifact versions that were used to produce this artifact.
    /// Maps artifact key to version fingerprint.
    /// Enables full lineage tracking: what inputs created this output?
    /// </summary>
    public required IReadOnlyDictionary<ArtifactKey, string> InputVersions { get; init; }

    /// <summary>
    /// Node ID that produced this artifact (optional).
    /// Null if artifact was externally created.
    /// </summary>
    public string? ProducedByNodeId { get; init; }

    /// <summary>
    /// Execution ID that produced this artifact (optional).
    /// Example: "exec-123:pipeline:stage1"
    /// </summary>
    public string? ExecutionId { get; init; }

    /// <summary>
    /// Custom metadata for application-specific use.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
