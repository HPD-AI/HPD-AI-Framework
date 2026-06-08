using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Commands.Handlers;

internal abstract class ExecuteCommandTuiHandlerBase<TEvent> : AgentTuiEventHandler<TEvent>
    where TEvent : ExecuteCommandEvent
{
    protected static CodingCommandExecutionStore GetStore(AgentTuiEventContext context)
        => context.State.GetOrCreate(
            CodingCommandExecutionStore.StateKey,
            static () => new CodingCommandExecutionStore());

    protected static bool TryGetExistingState(
        AgentTuiEventContext context,
        ExecuteCommandEvent evt,
        out CodingCommandExecutionState state)
    {
        var store = GetStore(context);
        return store.TryGetByCommandId(evt.CommandId, out state) ||
               store.TryGetByToolCallId(evt.ToolCallId, out state);
    }

    protected static void UpdateTranscript(AgentTuiEventContext context, CodingCommandExecutionState state, AgentEvent evt)
    {
        var snapshotKey = CodingCommandTranscriptEntryFactory.SnapshotKey(state);
        if (string.Equals(state.LastTranscriptSnapshotKey, snapshotKey, StringComparison.Ordinal))
        {
            return;
        }

        state.LastTranscriptSnapshotKey = snapshotKey;
        context.Shell.Transcript.Update(CodingCommandTranscriptEntryFactory.Create(state, evt));
    }
}
