namespace HPDAgent.Graph.Abstractions.Serialization;

/// <summary>
/// Polling state for checkpoint serialization.
/// Native AOT compatible record type.
/// Moved from GraphOrchestrator internal record to enable source-generated JSON serialization.
/// </summary>
public sealed record PollingState
{
    /// <summary>
    /// When polling started.
    /// </summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Current attempt number (1-indexed).
    /// </summary>
    public required int AttemptNumber { get; init; }

    /// <summary>
    /// Suspend token for resume correlation.
    /// </summary>
    public required string SuspendToken { get; init; }

    /// <summary>
    /// Time to wait before next retry.
    /// </summary>
    public required TimeSpan RetryAfter { get; init; }

    /// <summary>
    /// Maximum total wait time before timeout.
    /// </summary>
    public required TimeSpan MaxWaitTime { get; init; }
}
