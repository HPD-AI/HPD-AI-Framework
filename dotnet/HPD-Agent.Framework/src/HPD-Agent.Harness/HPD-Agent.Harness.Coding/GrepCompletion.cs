namespace HPD.Agent.ToolHarness.Coding;

/// <summary>Terminal state of one model-facing Grep operation.</summary>
internal enum GrepCompletionStatus
{
    Success,
    NoMatches,
    Truncated,
    TimedOut,
    Cancelled,
    Failed,
}

/// <summary>
/// Normalized completion metadata retained after a backend workspace search.
/// </summary>
internal sealed record GrepCompletionEvent
{
    public required GrepCompletionStatus Status { get; init; }
    public int? ExitCode { get; init; }
    public bool Partial { get; init; }
    public bool TimedOut { get; init; }
    public bool Cancelled { get; init; }
    public bool Truncated { get; init; }
    public int MatchesEmitted { get; init; }
    public string? Stderr { get; init; }
    public string? Reason { get; init; }
}
