using HPD.Agent;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Handlers;

internal sealed class CodingExplorationToolCallResultHandler : AgentTuiEventHandler<ToolCallResultEvent>
{
    public override ValueTask HandleAsync(
        ToolCallResultEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var store = context.State.GetOrCreate(
            CodingExplorationStore.StateKey,
            static () => new CodingExplorationStore());
        var toolName = ResolveToolName(evt, store);
        if (!CodingExplorationToolNames.IsExplorationTool(toolName))
        {
            return ValueTask.CompletedTask;
        }

        var group = store.GetOrCreateGroupForResult(evt.CallId, toolName!, evt.MessageId);
        if (!store.TryGetOperation(evt.CallId, out _, out var operation))
        {
            return ValueTask.CompletedTask;
        }

        operation.Summary = CodingExplorationResultParser.Parse(toolName!, evt.Result);
        operation.Status = operation.Summary.IsError
            ? CodingExplorationOperationStatus.Failed
            : CodingExplorationOperationStatus.Completed;
        operation.CompletedAt = DateTimeOffset.UtcNow;
        group.Touch();
        CodingExplorationTranscriptEntryFactory.Apply(context, group, evt);
        return ValueTask.CompletedTask;
    }

    private static string? ResolveToolName(ToolCallResultEvent evt, CodingExplorationStore store)
    {
        if (!string.IsNullOrWhiteSpace(evt.Name))
        {
            return evt.Name;
        }

        return store.TryGetOperation(evt.CallId, out _, out var operation)
            ? operation.ToolName
            : null;
    }
}
