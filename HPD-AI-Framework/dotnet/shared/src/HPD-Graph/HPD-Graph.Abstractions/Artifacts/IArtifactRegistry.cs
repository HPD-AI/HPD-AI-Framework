namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Registry for artifact metadata (NOT values—those live in INodeCacheStore).
/// This is a METADATA LAYER on top of existing cache infrastructure.
///
/// Responsibilities:
/// - Track artifact versions (fingerprints from INodeFingerprintCalculator)
/// - Maintain reverse index: artifact → producing node IDs
/// - Store artifact metadata (provenance, lineage, timestamps)
/// - Distributed locking for multi-process coordination
/// - Retention policy enforcement
/// </summary>
public interface IArtifactRegistry
{
    /// <summary>
    /// Get latest version fingerprint for an artifact.
    /// Returns the fingerprint (from INodeFingerprintCalculator) of the most recent execution.
    /// Returns null if artifact has never been materialized.
    /// </summary>
    /// <param name="key">Artifact identifier (path + optional partition).</param>
    /// <param name="partition">Optional partition override (overrides key.Partition if specified).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Version fingerprint or null if not found.</returns>
    Task<string?> GetLatestVersionAsync(
        ArtifactKey key,
        PartitionKey? partition = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get artifact metadata for a specific version.
    /// Metadata is separate from cached values (which live in INodeCacheStore).
    /// Returns null if version not found.
    /// </summary>
    /// <param name="key">Artifact identifier.</param>
    /// <param name="version">Version fingerprint from INodeFingerprintCalculator.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Artifact metadata or null if not found.</returns>
    Task<ArtifactMetadata?> GetMetadataAsync(
        ArtifactKey key,
        string version,
        CancellationToken ct = default);

    /// <summary>
    /// Register that an artifact version was created.
    /// Called by orchestrator after successful node execution.
    ///
    /// This method:
    /// - Updates latest version pointer
    /// - Stores artifact metadata
    /// - Updates reverse index (artifact → node)
    /// </summary>
    /// <param name="key">Artifact identifier.</param>
    /// <param name="version">Version fingerprint from INodeFingerprintCalculator.</param>
    /// <param name="metadata">Artifact metadata (provenance, lineage, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    Task RegisterAsync(
        ArtifactKey key,
        string version,
        ArtifactMetadata metadata,
        CancellationToken ct = default);

    /// <summary>
    /// REVERSE INDEX: Get all node IDs that produce this artifact.
    /// This is the critical missing piece for demand-driven execution.
    /// Built at graph initialization time via artifact index scan.
    ///
    /// Multi-Producer Resolution:
    /// When multiple nodes produce the same artifact, this method returns ALL candidates
    /// sorted by priority:
    ///   1. ExecutionId depth (deeper = more specific in subgraph hierarchy)
    ///   2. Deterministic tie-break (alphabetical by node ID)
    ///
    /// ExecutionId Depth Calculation:
    /// ExecutionId is hierarchical: "exec-123:pipeline:stage1:transform"
    /// Depth = number of colons = 3 (deeper = more specific in subgraph hierarchy)
    ///
    /// Caller should use First() to get the highest priority producer.
    /// Partition capability filtering can be added as a future enhancement.
    /// </summary>
    /// <param name="key">Artifact identifier.</param>
    /// <param name="partition">Optional partition (for future partition-aware filtering).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Prioritized list of node IDs that produce this artifact (highest priority first).</returns>
    Task<IReadOnlyList<string>> GetProducingNodeIdsAsync(
        ArtifactKey key,
        PartitionKey? partition = null,
        CancellationToken ct = default);

    /// <summary>
    /// List all known artifacts (for catalog/discovery).
    /// Returns unique artifact keys (without partition details).
    /// Useful for data cataloging and discovery.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async enumerable of artifact keys.</returns>
    IAsyncEnumerable<ArtifactKey> ListArtifactsAsync(CancellationToken ct = default);

    /// <summary>
    /// Get lineage: which artifact versions were inputs to create this version?
    /// Enables data provenance tracking.
    /// Returns empty dictionary if version has no dependencies.
    /// </summary>
    /// <param name="key">Artifact identifier.</param>
    /// <param name="version">Version fingerprint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Map of input artifact keys to their version fingerprints.</returns>
    Task<IReadOnlyDictionary<ArtifactKey, string>> GetLineageAsync(
        ArtifactKey key,
        string version,
        CancellationToken ct = default);

    /// <summary>
    /// Acquire distributed lock for materializing an artifact.
    /// Prevents race conditions in multi-node deployments.
    /// Returns null if lock cannot be acquired within timeout.
    /// Essential for production distributed systems.
    ///
    /// Phase 1 Implementation:
    /// - Single-process only (SemaphoreSlim-based)
    /// - Logs warning if used in distributed scenario
    ///
    /// Phase 5 Implementation:
    /// - SQL semaphore pattern (PostgreSQL FOR UPDATE SKIP LOCKED)
    /// - Redis distributed locks (optional)
    /// </summary>
    /// <param name="key">Artifact identifier.</param>
    /// <param name="partition">Optional partition to lock.</param>
    /// <param name="timeout">Maximum wait time for lock acquisition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// IAsyncDisposable lock handle (dispose to release lock).
    /// Null if lock cannot be acquired within timeout.
    /// </returns>
    Task<IAsyncDisposable?> TryAcquireMaterializationLockAsync(
        ArtifactKey key,
        PartitionKey? partition,
        TimeSpan timeout,
        CancellationToken ct = default);

    /// <summary>
    /// Prune old artifact versions based on retention policy.
    /// Prevents unbounded registry growth in production.
    ///
    /// This method:
    /// - Identifies versions to delete based on policy
    /// - Removes metadata entries
    /// - Does NOT delete cached values (caller must clean INodeCacheStore separately)
    /// </summary>
    /// <param name="key">Artifact identifier.</param>
    /// <param name="policy">Retention rules.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of versions pruned.</returns>
    Task<int> PruneOldVersionsAsync(
        ArtifactKey key,
        RetentionPolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Validate consistency: ensure all registered artifacts have corresponding cache entries.
    /// Returns list of orphaned artifacts (metadata exists but no cached value).
    /// Used for operational health checks.
    ///
    /// Note: Requires access to INodeCacheStore to validate existence.
    /// Caller should provide cache store for validation.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of artifact keys with orphaned metadata.</returns>
    Task<IReadOnlyList<ArtifactKey>> ValidateConsistencyAsync(CancellationToken ct = default);
}
