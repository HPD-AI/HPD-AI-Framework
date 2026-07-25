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

        DebugToolCallTuiCoordinator.Claim(
            context,
            evt.ToolCallId,
            DebugPresentationClaim.Breakpoint);
        context.Shell.Transcript.RemoveLive($"tool:{evt.ToolCallId}");
        Render(context, state.BreakpointSelections[DebugTuiState.EntryKey(evt)], evt);
        return ValueTask.CompletedTask;
    }

    internal static void Render(
        AgentTuiEventContext context,
        DebugBreakpointPresentationState selection,
        HPD.Agent.AgentEvent evt)
    {
        var key = selection.EntryKey;
        var counts = selection.Counts;
        var cell = new DebugBreakpointCell(
            key,
            Label(selection),
            selection.Kind,
            selection.Before,
            selection.After,
            selection.Changes,
            counts,
            selection.Items.Values.Where(item => item.HitCount > 0)
                .Select(item => item.ClientBreakpointId)
                .ToHashSet(StringComparer.Ordinal),
            selection.SourcePreviews,
            selection.DetailsTruncated);
        context.Shell.Transcript.FinalizeLive(
            key,
            new TranscriptEntry(
                Id: $"debug-breakpoints-{Math.Abs(key.GetHashCode(StringComparison.Ordinal))}",
                EntryKey: key,
                Cell: cell,
                Metadata: TranscriptEntryMetadata.FromEvent(evt)).AsFinal());
    }

    private static string Label(DebugBreakpointPresentationState state)
    {
        var added = state.Changes.Count(change => change.Kind == DebugBreakpointSelectionDeltaKind.Added);
        var removed = state.Changes.Count(change => change.Kind == DebugBreakpointSelectionDeltaKind.Removed);
        var description = state.HasEvolved ? string.Empty : added > 0 && removed > 0
            ? $"+{added} −{removed}"
            : added > 0 ? $"+{added}" : removed > 0 ? $"−{removed}" : "updated";
        var counts = state.Counts;
        var suffix = counts.Hit > 0 ? $" · {counts.Hit} hit" : string.Empty;
        if (counts.UnknownHit > 0)
            suffix += $" · {counts.UnknownHit} unidentified stop{(counts.UnknownHit == 1 ? "" : "s")}";
        return $"• Breakpoints{(description.Length > 0 ? $" {description}" : "")} · {counts.Verified}/{counts.Requested} resolved{suffix}";
    }
}
