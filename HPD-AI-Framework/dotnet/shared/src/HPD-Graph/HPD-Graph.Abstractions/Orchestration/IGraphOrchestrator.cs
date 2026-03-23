using HPD.Events;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;

namespace HPDAgent.Graph.Abstractions.Orchestration;

/// <summary>
/// Orchestrates graph execution.
/// Handles topological sorting, parallel execution, conditional routing,
/// retries, and error propagation.
/// Generic over TContext to support different execution contexts.
/// </summary>
/// <typeparam name="TContext">The graph context type</typeparam>
public interface IGraphOrchestrator<TContext> where TContext : class, IGraphContext
{
    /// <summary>
    /// Execute graph from start to finish.
    /// </summary>
    /// <param name="context">Execution context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated context after execution</returns>
    Task<TContext> ExecuteAsync(
        TContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resume graph execution from checkpoint.
    /// Skips already-completed nodes.
    /// </summary>
    /// <param name="context">Execution context with completed nodes tracked</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated context after execution</returns>
    Task<TContext> ResumeAsync(
        TContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute graph with real-time event streaming.
    /// Emits events as nodes complete, enabling progress tracking and live updates.
    /// </summary>
    /// <param name="context">Execution context</param>
    /// <param name="options">Streaming options (controls event emission)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of events (GraphExecutionStarted, NodeExecutionCompleted, GraphExecutionCompleted, etc.)</returns>
    IAsyncEnumerable<Event> ExecuteStreamingAsync(
        TContext context,
        StreamingOptions? options = null,
        CancellationToken cancellationToken = default);

    // ========== PHASE 3: DEMAND-DRIVEN EXECUTION API ==========

    /// <summary>
    /// Materialize an artifact by executing only the necessary nodes.
    /// Requires graph registry to be configured and graph to be registered.
    ///
    /// Strategy:
    /// 1. Resolve graph from registry by graphId
    /// 2. Resolve producing node via artifact registry reverse index
    /// 3. Check if artifact exists with current version (via registry + cache)
    /// 4. If exists and inputs unchanged: return cached artifact
    /// 5. Otherwise: build minimal subgraph and execute
    ///
    /// Leverages existing infrastructure:
    /// - IGraphRegistry for graph lookup (stateless)
    /// - INodeFingerprintCalculator for versioning
    /// - INodeCacheStore for artifact values
    /// - AffectedNodeDetector for minimal work computation
    /// - ExecuteAsync() for execution
    /// </summary>
    /// <typeparam name="T">Artifact value type</typeparam>
    /// <param name="graphId">ID of the graph to use (registered in graph registry)</param>
    /// <param name="artifactKey">Artifact to materialize</param>
    /// <param name="partition">Optional partition key</param>
    /// <param name="options">Materialization options (force recompute, lock timeout, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Materialized artifact with value and metadata</returns>
    Task<Artifact<T>> MaterializeAsync<T>(
        string graphId,
        ArtifactKey artifactKey,
        PartitionKey? partition = null,
        MaterializationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Materialize multiple artifacts in parallel.
    /// Automatically deduplicates shared upstream dependencies.
    /// Requires graph registry to be configured and graph to be registered.
    /// </summary>
    /// <param name="graphId">ID of the graph to use (registered in graph registry)</param>
    /// <param name="artifactKeys">Artifacts to materialize</param>
    /// <param name="partition">Optional partition key (applies to all artifacts)</param>
    /// <param name="options">Materialization options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping artifact keys to materialized values (object type, cast as needed)</returns>
    Task<IReadOnlyDictionary<ArtifactKey, object>> MaterializeManyAsync(
        string graphId,
        IEnumerable<ArtifactKey> artifactKeys,
        PartitionKey? partition = null,
        MaterializationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Backfill: materialize an artifact for a range of partitions.
    /// Processes partitions in parallel using existing Map node infrastructure.
    /// Emits events for progress tracking and completion.
    /// Requires graph registry to be configured and graph to be registered.
    /// </summary>
    /// <typeparam name="T">Artifact value type</typeparam>
    /// <param name="graphId">ID of the graph to use (registered in graph registry)</param>
    /// <param name="artifactKey">Artifact to backfill</param>
    /// <param name="partitions">Partitions to materialize</param>
    /// <param name="options">Backfill options (max parallel, skip existing, fail fast, emit events, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of events (BackfillStartedEvent, BackfillPartitionCompletedEvent, BackfillCompletedEvent).
    /// Extract artifacts from BackfillPartitionCompletedEvent.Artifact property.</returns>
    IAsyncEnumerable<Event> BackfillAsync<T>(
        string graphId,
        ArtifactKey artifactKey,
        IEnumerable<PartitionKey> partitions,
        BackfillOptions? options = null,
        CancellationToken cancellationToken = default);
}
