using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

public sealed record CodingExplorationCell(
    string GroupId,
    string? MessageId,
    bool IsActive,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUpdatedAt,
    IReadOnlyList<CodingExplorationOperationCell> Operations,
    IReadOnlyList<string> Rows) : TranscriptCell;

public sealed record CodingExplorationOperationCell(
    string CallId,
    string ToolName,
    CodingExplorationOperationState State,
    string? ArgsJson,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    CodingExplorationSummaryCell? Summary);

public enum CodingExplorationOperationState
{
    Pending,
    Running,
    Completed,
    Failed
}

public abstract record CodingExplorationSummaryCell
{
    public string? Path { get; init; }
    public bool Truncated { get; init; }
    public string? TruncationReason { get; init; }
    public bool HasMore { get; init; }
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record UnknownExplorationSummaryCell : CodingExplorationSummaryCell;

public sealed record ReadFileExplorationSummaryCell : CodingExplorationSummaryCell
{
    public int StartLine { get; init; }
    public int LinesRead { get; init; }
    public int TotalLines { get; init; }
    public string? Coverage { get; init; }
    public bool Unchanged { get; init; }
}

public sealed record GrepExplorationSummaryCell : CodingExplorationSummaryCell
{
    public string? Pattern { get; init; }
    public string? OutputMode { get; init; }
    public string? TotalResults { get; init; }
    public string? TotalMatches { get; init; }
    public string? Status { get; init; }
}

public sealed record GlobExplorationSummaryCell : CodingExplorationSummaryCell
{
    public string? Pattern { get; init; }
    public string? OriginalPattern { get; init; }
    public string? TotalMatches { get; init; }
    public int MatchesRead { get; init; }
    public int IgnoredCount { get; init; }
}

public sealed record ListDirectoryExplorationSummaryCell : CodingExplorationSummaryCell
{
    public bool Recursive { get; init; }
    public int EntriesRead { get; init; }
    public string? TotalEntries { get; init; }
    public int IgnoredCount { get; init; }
}
