using HPD.Agent;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Handlers;

internal sealed class CodingExplorationToolCallEndHandler : AgentTuiEventHandler<ToolCallEndEvent>
{
    public override ValueTask HandleAsync(
        ToolCallEndEvent evt,
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

        if (!operation.IsComplete)
        {
            operation.Status = CodingExplorationOperationStatus.Completed;
            operation.CompletedAt = DateTimeOffset.UtcNow;
        }

        group.Touch();
        CodingExplorationTranscriptEntryFactory.Apply(context, group, evt);
        return ValueTask.CompletedTask;
    }
}
