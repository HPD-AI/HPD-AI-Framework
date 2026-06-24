using HPD.Agent;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Handlers;

internal sealed class CodingExplorationToolCallArgsHandler : AgentTuiEventHandler<ToolCallArgsEvent>
{
    public override ValueTask HandleAsync(
        ToolCallArgsEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var store = context.State.GetOrCreate(
            CodingExplorationStore.StateKey,
            static () => new CodingExplorationStore());
        if (!store.TryGetOperation(evt.CallId, out var group, out var operation))
        {
            return ValueTask.CompletedTask;
        }

        operation.ArgsJson = evt.ArgsJson;
        operation.Status = CodingExplorationOperationStatus.Running;
        group.Touch();
        CodingExplorationTranscriptEntryFactory.Apply(context, group, evt);
        return ValueTask.CompletedTask;
    }
}
