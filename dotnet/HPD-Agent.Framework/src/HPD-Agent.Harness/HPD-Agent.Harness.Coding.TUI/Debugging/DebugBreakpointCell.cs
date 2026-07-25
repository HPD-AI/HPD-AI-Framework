using HPD.Agent.TUI.Models;
using HPD.Agent.ToolHarness.Coding.Debugging;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

public sealed record DebugBreakpointCell(
    string EntryKey,
    string Label,
    DebugBreakpointKind BreakpointKind,
    IReadOnlyList<DebugBreakpointSelectionEventItem> Before,
    IReadOnlyList<DebugBreakpointSelectionEventItem> After,
    IReadOnlyList<DebugBreakpointSelectionDelta> Changes,
    DebugBreakpointCounts Counts,
    IReadOnlySet<string> HitBreakpointClientIds,
    IReadOnlyList<DebugSourcePreview> SourcePreviews,
    bool Truncated) : TranscriptCell;
