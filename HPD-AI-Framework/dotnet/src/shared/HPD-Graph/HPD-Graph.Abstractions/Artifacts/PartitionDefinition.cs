using System.Text.Json.Serialization;

namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Base class for partition definitions.
/// Defines how a dataset is sliced into partitions.
///
/// Partition definitions are **data** (immutable records) that describe partitioning logic.
/// The actual resolution of partition keys happens at runtime via ResolveAsync().
///
/// Framework provides built-in types:
/// - StaticPartitionDefinition: Fixed set of keys (regions, categories, etc.)
/// - TimePartitionDefinition: Time-based partitions (hourly, daily, weekly, etc.)
/// - MultiPartitionDefinition: Cartesian product of multiple dimensions
///
/// Users can extend this class to implement custom partition logic (database queries,
/// S3 listings, API calls, etc.) with access to service provider for dependency injection.
/// </summary>
[JsonDerivedType(typeof(StaticPartitionDefinition), typeDiscriminator: "static")]
[JsonDerivedType(typeof(TimePartitionDefinition), typeDiscriminator: "time")]
[JsonDerivedType(typeof(MultiPartitionDefinition), typeDiscriminator: "multi")]
public abstract record PartitionDefinition
{
    /// <summary>
    /// Resolves partition keys at runtime and returns a snapshot.
    /// Framework uses the snapshot for fingerprinting and caching.
    ///
    /// This method is called by the orchestrator during graph execution to determine
    /// which partitions need to be processed. The returned PartitionSnapshot contains
    /// both the partition keys and a stable hash used for incremental execution.
    ///
    /// Implementations should:
    /// 1. Resolve partition keys (may involve I/O: database queries, API calls, file listings)
    /// 2. Compute a stable snapshot hash based on the chosen SnapshotStrategy
    /// 3. Return an immutable PartitionSnapshot
    ///
    /// For deterministic (static/time-based) partitions, this method typically returns
    /// immediately with a deterministically computed snapshot. For dynamic partitions
    /// (database-driven, file-based), this method performs I/O to discover partitions.
    /// </summary>
    /// <param name="services">Service provider for accessing dependencies (databases, S3 clients, etc.)</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Immutable snapshot containing partition keys and hash for fingerprinting</returns>
    public abstract Task<PartitionSnapshot> ResolveAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default);
}
