using HPD.Agent.TUI.Observability;

namespace HPD.Agent.ToolHarness.Coding.TUI.Observability;

public sealed record CodingCommandTranscriptUpdated(
    string? AgentId,
    string CommandId,
    bool Applied,
    int OutputLinesInCell,
    int OmittedLines,
    TimeSpan SnapshotDuration) : AgentTuiPerformanceEvent
{
    public override string FormatSummary()
        => $"command {CommandId} transcript applied={Applied} outputLines={OutputLinesInCell} omitted={OmittedLines} snapshot={SnapshotDuration.TotalMilliseconds:0.###}ms";
}
