using HPD.Agent;
using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Handlers;

internal sealed class CodingExplorationToolCallStartHandler : AgentTuiEventHandler<ToolCallStartEvent>, IAgentTuiToolCallHandler
{
    public bool CanHandleToolCall(string? toolHarnessName, string toolName, ToolCallType? callType)
        => CodingExplorationToolNames.IsExplorationTool(toolName);

    public override ValueTask HandleAsync(
        ToolCallStartEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (!CodingExplorationToolNames.IsExplorationTool(evt.Name))
        {
            return ValueTask.CompletedTask;
        }

        var store = Store(context);
        var group = store.GetOrCreateGroupForStart(evt.CallId, evt.Name, evt.MessageId);
        UpdateTranscript(group, evt, context);
        return ValueTask.CompletedTask;
    }

    private static CodingExplorationStore Store(AgentTuiEventContext context)
        => context.State.GetOrCreate(
            CodingExplorationStore.StateKey,
            static () => new CodingExplorationStore());

    private static void UpdateTranscript(
        CodingExplorationGroup group,
        AgentEvent evt,
        AgentTuiEventContext context)
        => CodingExplorationTranscriptEntryFactory.Apply(context, group, evt);
}
