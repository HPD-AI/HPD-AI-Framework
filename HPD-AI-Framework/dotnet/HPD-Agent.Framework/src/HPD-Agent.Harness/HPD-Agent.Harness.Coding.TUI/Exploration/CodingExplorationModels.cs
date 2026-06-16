namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration;

internal enum CodingExplorationOperationStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

internal sealed class CodingExplorationGroup
{
    private readonly object _gate = new();
    private readonly List<CodingExplorationOperation> _operations = [];

    public CodingExplorationGroup(string groupId, string? messageId)
    {
        GroupId = groupId;
        MessageId = messageId;
        StartedAt = DateTimeOffset.UtcNow;
        LastUpdatedAt = StartedAt;
    }

    public string GroupId { get; }

    public string? MessageId { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset LastUpdatedAt { get; private set; }

    public IReadOnlyList<CodingExplorationOperation> CaptureOperations()
    {
        lock (_gate)
        {
            return _operations.ToArray();
        }
    }

    public bool CaptureIsActive()
    {
        lock (_gate)
        {
            return _operations.Any(static operation => !operation.IsComplete);
        }
    }

    public CodingExplorationOperation GetOrAdd(string callId, string toolName)
    {
        lock (_gate)
        {
            var existing = _operations.FirstOrDefault(operation => string.Equals(operation.CallId, callId, StringComparison.Ordinal));
            if (existing is not null)
            {
                existing.ToolName = toolName;
                Touch();
                return existing;
            }

            var operation = new CodingExplorationOperation(callId, toolName);
            _operations.Add(operation);
            Touch();
            return operation;
        }
    }

    public void Touch() => LastUpdatedAt = DateTimeOffset.UtcNow;
}

internal sealed class CodingExplorationOperation
{
    public CodingExplorationOperation(string callId, string toolName)
    {
        CallId = callId;
        ToolName = toolName;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public string CallId { get; }

    public string ToolName { get; set; }

    public string? ArgsJson { get; set; }

    public CodingExplorationOperationStatus Status { get; set; } = CodingExplorationOperationStatus.Pending;

    public CodingExplorationSummary? Summary { get; set; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; set; }

    public bool IsComplete => Status is CodingExplorationOperationStatus.Completed or CodingExplorationOperationStatus.Failed;
}

internal abstract record CodingExplorationSummary
{
    public string? Path { get; init; }
    public bool Truncated { get; init; }
    public string? TruncationReason { get; init; }
    public bool HasMore { get; init; }
    public bool IsError { get; init; }
    public string? ErrorMessage { get; init; }
}

internal sealed record UnknownExplorationSummary : CodingExplorationSummary;

internal sealed record ReadFileExplorationSummary : CodingExplorationSummary
{
    public int StartLine { get; init; }
    public int LinesRead { get; init; }
    public int TotalLines { get; init; }
    public string? Coverage { get; init; }
    public bool Unchanged { get; init; }
}

internal sealed record GrepExplorationSummary : CodingExplorationSummary
{
    public string? Pattern { get; init; }
    public string? OutputMode { get; init; }
    public string? TotalResults { get; init; }
    public string? TotalMatches { get; init; }
    public string? Status { get; init; }
}

internal sealed record GlobExplorationSummary : CodingExplorationSummary
{
    public string? Pattern { get; init; }
    public string? OriginalPattern { get; init; }
    public string? TotalMatches { get; init; }
    public int MatchesRead { get; init; }
    public int IgnoredCount { get; init; }
}

internal sealed record ListDirectoryExplorationSummary : CodingExplorationSummary
{
    public bool Recursive { get; init; }
    public int EntriesRead { get; init; }
    public string? TotalEntries { get; init; }
    public int IgnoredCount { get; init; }
}
