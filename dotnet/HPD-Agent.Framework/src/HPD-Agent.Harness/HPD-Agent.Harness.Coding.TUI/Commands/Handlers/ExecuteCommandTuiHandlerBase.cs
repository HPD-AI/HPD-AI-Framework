using System.Diagnostics;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Observability;
using HPD.Agent.ToolHarness.Coding.TUI.Observability;
using HPD.TUI.Observability;

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

    internal static void UpdateTranscript(AgentTuiEventContext context, CodingCommandExecutionState state, AgentEvent evt)
    {
        var hasSink = AgentTuiPerformanceDiagnostics.TryGetSink(context.State, out var sink);
        var startTimestamp = hasSink ? Stopwatch.GetTimestamp() : 0;
        var cell = CodingCommandTranscriptEntryFactory.CreateCell(state);
        var snapshotKey = CodingCommandTranscriptEntryFactory.SnapshotKey(cell);
        var applied = !string.Equals(state.LastTranscriptSnapshotKey, snapshotKey, StringComparison.Ordinal);
        if (!applied)
        {
            PublishCommandUpdate(context, sink, state.CommandId, applied, cell, startTimestamp);
            return;
        }

        state.LastTranscriptSnapshotKey = snapshotKey;
        var entry = CodingCommandTranscriptEntryFactory.Create(cell, evt);
        var entryKey = CodingCommandTranscriptEntryFactory.EntryKey(cell.CommandId);
        if (IsFinal(cell.State))
        {
            context.Shell.Transcript.FinalizeLive(entryKey, entry.AsFinal());
        }
        else
        {
            context.Shell.Transcript.UpsertLive(entry.AsLive());
        }

        PublishCommandUpdate(context, sink, state.CommandId, applied, cell, startTimestamp);
    }

    private static bool IsFinal(CodingCommandTranscriptState state)
        => state is
            CodingCommandTranscriptState.Completed or
            CodingCommandTranscriptState.Failed or
            CodingCommandTranscriptState.Cancelled or
            CodingCommandTranscriptState.TimedOut or
            CodingCommandTranscriptState.Exited;

    private static void PublishCommandUpdate(
        AgentTuiEventContext context,
        IHpdTuiPerformanceEventSink? sink,
        string commandId,
        bool applied,
        CodingCommandCell cell,
        long startTimestamp)
    {
        if (sink is null)
        {
            return;
        }

        sink.Publish(new CodingCommandTranscriptUpdated(
            context.Scope.AgentId,
            commandId,
            applied,
            cell.Output.Count,
            cell.OutputWindow.OmittedLineCount,
            Stopwatch.GetElapsedTime(startTimestamp))
        {
            SessionId = context.Scope.SessionId,
            ThreadId = context.Scope.ThreadId,
            Metadata = new AgentMetadata
            {
                AgentId = context.Scope.AgentId,
                AgentName = context.Scope.AgentId
            }
        });
    }
}
