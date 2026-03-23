namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// How errors propagate to downstream nodes.
/// Universal modes for ANY workflow.
/// </summary>
public enum PropagationMode
{
    /// <summary>
    /// Stop entire graph execution immediately.
    /// Use case: Critical failures in essential nodes.
    /// Example: Authentication fails → stop everything.
    /// Behavior: Throws exception from orchestrator.
    /// </summary>
    StopGraph,

    /// <summary>
    /// Skip all downstream dependent nodes.
    /// Mark dependents as Skipped with reason DependencyFailed.
    /// Continue executing independent branches.
    /// Use case: Branching workflows where one branch can fail independently.
    /// Example: Image processing fails → skip image nodes, continue text processing.
    /// </summary>
    SkipDependents,

    /// <summary>
    /// Execute fallback node, then continue normal execution.
    /// Use case: Graceful degradation with alternative strategies.
    /// Example: Vector search fails → fallback to BM25 search.
    /// Fallback node receives original inputs + error context.
    /// </summary>
    ExecuteFallback,

    /// <summary>
    /// Isolate error - don't affect any downstream nodes.
    /// Downstream nodes execute normally (may receive partial/null inputs).
    /// Use case: Optional enrichment, best-effort operations.
    /// Example: Sentiment analysis fails → continue with raw text.
    /// </summary>
    Isolate
}
