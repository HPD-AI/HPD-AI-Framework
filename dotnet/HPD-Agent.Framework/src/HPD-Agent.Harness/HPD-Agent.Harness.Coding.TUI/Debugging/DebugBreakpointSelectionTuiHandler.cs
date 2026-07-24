using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugBreakpointSelectionTuiHandler
    : AgentTuiEventHandler<DebugBreakpointSelectionAppliedEvent>
{
    public override ValueTask HandleAsync(
        DebugBreakpointSelectionAppliedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var state = context.State.GetOrCreate(
            DebugTuiState.StateKey,
            static () => new DebugTuiState());
        if (!state.Apply(evt)) return ValueTask.CompletedTask;

        context.Shell.Transcript.RemoveLive($"tool:{evt.ToolCallId}");
        var key = DebugTuiState.EntryKey(evt);
        var cell = new DebugBreakpointCell(
            key,
            Label(evt),
            evt.BreakpointKind,
            evt.Before,
            evt.After,
            evt.Changes,
            evt.Counts,
            evt.SourcePreviews,
            evt.DetailsTruncated);
        context.Shell.Transcript.FinalizeLive(
            key,
            new TranscriptEntry(
                Id: $"debug-breakpoints-{Math.Abs(key.GetHashCode(StringComparison.Ordinal))}",
                EntryKey: key,
                Cell: cell,
                Metadata: TranscriptEntryMetadata.FromEvent(evt)).AsFinal());
        return ValueTask.CompletedTask;
    }

    private static string Label(DebugBreakpointSelectionAppliedEvent evt)
    {
        var added = evt.Changes.Count(change => change.Kind == DebugBreakpointSelectionDeltaKind.Added);
        var removed = evt.Changes.Count(change => change.Kind == DebugBreakpointSelectionDeltaKind.Removed);
        var description = added > 0 && removed > 0
            ? $"+{added} −{removed}"
            : added > 0 ? $"+{added}" : removed > 0 ? $"−{removed}" : "updated";
        return $"• Breakpoints {description} · {evt.Counts.Verified}/{evt.Counts.Requested} verified";
    }
}
