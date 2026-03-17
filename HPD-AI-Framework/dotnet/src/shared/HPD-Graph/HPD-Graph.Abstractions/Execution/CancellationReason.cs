namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Why a node was cancelled.
/// </summary>
public enum CancellationReason
{
    /// <summary>
    /// User requested cancellation (CancellationToken).
    /// </summary>
    UserRequested,

    /// <summary>
    /// Node exceeded its timeout.
    /// </summary>
    Timeout,

    /// <summary>
    /// Parent graph or subgraph failed.
    /// Cascading cancellation.
    /// </summary>
    ParentFailed,

    /// <summary>
    /// Upstream dependency was cancelled.
    /// Propagation from dependency.
    /// </summary>
    DependencyCancelled,

    /// <summary>
    /// Resource exhaustion (memory, rate limits, etc.).
    /// </summary>
    ResourceExhausted
}
