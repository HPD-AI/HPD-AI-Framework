using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

public sealed record DebugActivityCell(
    string ToolCallId,
    string Action,
    string Label,
    IReadOnlyList<string> Lines,
    bool IsActive,
    bool IsError) : TranscriptCell;
