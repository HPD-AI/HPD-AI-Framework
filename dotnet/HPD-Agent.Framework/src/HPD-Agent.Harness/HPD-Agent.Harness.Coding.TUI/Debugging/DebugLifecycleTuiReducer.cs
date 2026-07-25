using HPD.Agent.TUI.Composition;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugLifecycleTuiReducer : IAgentTuiEventHandler
{
    public bool CanHandle(HPD.Agent.AgentEvent evt) => evt is DebugLifecycleEvent;

    public ValueTask HandleAsync(
        HPD.Agent.AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (evt is not DebugLifecycleEvent debug) return ValueTask.CompletedTask;
        var state = context.State.GetOrCreate(
            DebugTuiState.StateKey,
            static () => new DebugTuiState());
        if (!state.BeginReduce(debug)) return ValueTask.CompletedTask;
        var tree = state.Tree(debug.DebugTreeId);
        tree.AdapterId = debug.AdapterId;
        switch (debug)
        {
            case DebugTreeStartedEvent:
                tree.Status = "Starting";
                break;
            case DebugSessionStateChangedEvent changed:
                tree.Status = changed.Status;
                break;
            case DebugSessionStoppedEvent:
                tree.Status = "Stopped";
                break;
            case DebugSessionContinuedEvent:
                tree.Status = "Running";
                break;
            case DebugBreakpointSelectionAppliedEvent selection:
                tree.Breakpoints = selection.Counts;
                break;
            case DebugThreadChangedEvent thread:
                tree.ThreadCount = AdjustCount(tree.ThreadCount, thread.Reason);
                break;
            case DebugModuleChangedEvent module:
                tree.ModuleCount = AdjustCount(tree.ModuleCount, module.Reason);
                break;
            case DebugLoadedSourceChangedEvent source:
                tree.SourceCount = AdjustCount(tree.SourceCount, source.Reason);
                break;
            case DebugOutputAvailableEvent output:
                if (!string.IsNullOrWhiteSpace(output.InlineText))
                {
                    tree.Output.Enqueue(output.InlineText);
                    while (tree.Output.Count > 100) tree.Output.Dequeue();
                }
                tree.DroppedOutputRecords += output.DroppedRecords;
                break;
            case DebugTreeCompletedEvent completed:
                tree.Status = completed.FinalStatus;
                tree.Breakpoints = completed.Breakpoints;
                tree.DroppedOutputRecords = Math.Max(
                    tree.DroppedOutputRecords,
                    completed.DroppedOutputRecords);
                break;
            case DebugTreeTerminatedEvent:
                tree.Status = "Terminated";
                break;
            case DebugTreeFaultedEvent:
                tree.Status = "Faulted";
                break;
            case DebugTerminalRecordEvictedEvent:
                state.Evict(debug.DebugTreeId);
                return ValueTask.CompletedTask;
        }
        state.Touch(debug.DebugTreeId);
        return ValueTask.CompletedTask;
    }

    private static int AdjustCount(int count, string reason)
        => string.Equals(reason, "removed", StringComparison.Ordinal)
            ? Math.Max(0, count - 1)
            : string.Equals(reason, "new", StringComparison.Ordinal) ||
                string.Equals(reason, "started", StringComparison.Ordinal)
                ? count + 1
                : count;
}
