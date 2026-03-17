namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Error handling strategy for Map node execution.
/// </summary>
public enum MapErrorMode
{
    /// <summary>
    /// Stop on first error and fail the entire map operation.
    /// - Cancels remaining tasks immediately
    /// - Returns Failure result with AggregateException if multiple tasks failed
    /// - Most conservative, fastest failure detection
    /// Default behavior.
    /// </summary>
    FailFast,

    /// <summary>
    /// Continue processing all items even if some fail.
    /// - Failed items produce null results in output
    /// - Map node returns Success with partial results
    /// - Use when you can tolerate missing data
    /// </summary>
    ContinueWithNulls,

    /// <summary>
    /// Continue processing all items even if some fail.
    /// - Failed items are omitted from results entirely
    /// - Map node returns Success with successful results only
    /// - Output array may be shorter than input array
    /// - Use when failed items should be filtered out
    /// </summary>
    ContinueOmitFailures
}
