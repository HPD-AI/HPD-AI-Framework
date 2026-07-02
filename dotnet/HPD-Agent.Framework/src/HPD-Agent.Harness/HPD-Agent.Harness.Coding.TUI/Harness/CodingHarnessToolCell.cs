using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Harness;

public sealed record CodingHarnessToolCell(
    string CallId,
    bool IsActive,
    string Summary) : TranscriptCell;
