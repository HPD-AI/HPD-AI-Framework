using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugStoppedTuiHandler : AgentTuiEventHandler<DebugSessionStoppedEvent>
{
    public override ValueTask HandleAsync(DebugSessionStoppedEvent evt, AgentTuiEventContext context, CancellationToken cancellationToken)
    {
        var state = State(context).Session(evt.DebugTreeId, evt.DebugSessionId);
        state.Status = "Stopped";
        state.StoppedThreadId = evt.AdapterThreadId;
        state.StopReason = evt.Reason;
        state.SuspensionEpoch = evt.SuspensionEpoch;
        state.CurrentStop = null;
        Apply(context, state, evt, final: false);
        return ValueTask.CompletedTask;
    }

    internal static DebugTuiState State(AgentTuiEventContext context)
        => context.State.GetOrCreate(DebugTuiState.StateKey, static () => new DebugTuiState());

    internal static string EntryKey(DebugSessionTuiState state)
        => $"hpd.coding.debug:stop:{state.DebugTreeId}:{state.DebugSessionId}:{state.SuspensionEpoch}";

    internal static void Apply(
        AgentTuiEventContext context,
        DebugSessionTuiState state,
        HPD.Agent.AgentEvent evt,
        bool final)
    {
        var key = EntryKey(state);
        var entry = new TranscriptEntry(
            Id: $"debug-stop-{Math.Abs(key.GetHashCode(StringComparison.Ordinal))}",
            EntryKey: key,
            Cell: new DebugStoppedCell(
                state.DebugTreeId,
                state.DebugSessionId,
                state.StoppedThreadId,
                state.SuspensionEpoch ?? 0,
                state.StopReason ?? "stopped",
                state.CurrentStop),
            Metadata: TranscriptEntryMetadata.FromEvent(evt));
        if (final) context.Shell.Transcript.FinalizeLive(key, entry.AsFinal(), CommittedHistoryMutationPolicy.Reject);
        else context.Shell.Transcript.UpsertLive(entry.AsLive(), CommittedHistoryMutationPolicy.Reject);
    }
}

internal sealed class DebugPrimaryStopTuiHandler : AgentTuiEventHandler<DebugPrimaryStopAvailableEvent>
{
    public override ValueTask HandleAsync(DebugPrimaryStopAvailableEvent evt, AgentTuiEventContext context, CancellationToken cancellationToken)
    {
        var state = DebugStoppedTuiHandler.State(context).Session(evt.DebugTreeId, evt.DebugSessionId);
        if (state.Status != "Stopped" ||
            state.SuspensionEpoch != evt.SuspensionEpoch ||
            state.StoppedThreadId != evt.AdapterThreadId)
            return ValueTask.CompletedTask;
        state.CurrentStop = evt;
        DebugStoppedTuiHandler.Apply(context, state, evt, final: false);
        var debugState = DebugStoppedTuiHandler.State(context);
        if (!debugState.BeginBreakpointProjection(evt))
            return ValueTask.CompletedTask;
        foreach (var selection in debugState.ObserveHits(evt))
            DebugBreakpointSelectionTuiHandler.Render(context, selection, evt);
        return ValueTask.CompletedTask;
    }
}

internal sealed class DebugContinuedTuiHandler : AgentTuiEventHandler<DebugSessionContinuedEvent>
{
    public override ValueTask HandleAsync(DebugSessionContinuedEvent evt, AgentTuiEventContext context, CancellationToken cancellationToken)
    {
        var state = DebugStoppedTuiHandler.State(context).Session(evt.DebugTreeId, evt.DebugSessionId);
        if (state.Status == "Stopped")
            DebugStoppedTuiHandler.Apply(context, state, evt, final: true);
        state.Status = "Running";
        state.StoppedThreadId = null;
        state.StopReason = null;
        state.SuspensionEpoch = null;
        state.CurrentStop = null;
        return ValueTask.CompletedTask;
    }
}
