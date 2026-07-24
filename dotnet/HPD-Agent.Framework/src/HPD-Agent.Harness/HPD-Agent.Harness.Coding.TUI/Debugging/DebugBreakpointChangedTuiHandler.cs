using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugBreakpointChangedTuiHandler
    : AgentTuiEventHandler<DebugBreakpointChangedEvent>
{
    public override ValueTask HandleAsync(
        DebugBreakpointChangedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var state = context.State.GetOrCreate(
            DebugTuiState.StateKey,
            static () => new DebugTuiState());
        var selection = state.Reconcile(evt);
        if (selection is null) return ValueTask.CompletedTask;
        var key = DebugTuiState.EntryKey(selection);
        var cell = new DebugBreakpointCell(
            key,
            $"• Breakpoints · {selection.Counts.Verified}/{selection.Counts.Requested} verified",
            selection.BreakpointKind,
            selection.Before,
            selection.After,
            selection.Changes,
            selection.Counts,
            selection.SourcePreviews,
            selection.DetailsTruncated);
        context.Shell.Transcript.FinalizeLive(
            key,
            new TranscriptEntry(
                Id: $"debug-breakpoints-{Math.Abs(key.GetHashCode(StringComparison.Ordinal))}",
                EntryKey: key,
                Cell: cell,
                Metadata: TranscriptEntryMetadata.FromEvent(evt)).AsFinal());
        return ValueTask.CompletedTask;
    }
}
