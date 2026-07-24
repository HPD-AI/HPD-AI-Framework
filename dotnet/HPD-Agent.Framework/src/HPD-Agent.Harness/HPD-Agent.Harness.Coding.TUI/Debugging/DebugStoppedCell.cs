using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

public sealed record DebugStoppedCell(
    string DebugTreeId,
    string DebugSessionId,
    int? ThreadId,
    long SuspensionEpoch,
    string Reason,
    DebugStopSummaryAvailableEvent? Summary) : TranscriptCell;
