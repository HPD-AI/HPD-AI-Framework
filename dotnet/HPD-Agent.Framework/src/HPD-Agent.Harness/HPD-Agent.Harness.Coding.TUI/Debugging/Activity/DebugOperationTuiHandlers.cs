using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugExecutionCommandTuiHandler
    : AgentTuiEventHandler<DebugExecutionCommandAppliedEvent>
{
    public override ValueTask HandleAsync(
        DebugExecutionCommandAppliedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (evt.ToolCallId is not { Length: > 0 } toolCallId)
            return ValueTask.CompletedTask;
        DebugToolCallTuiCoordinator.Claim(
            context,
            toolCallId,
            DebugPresentationClaim.Execution);
        var command = Display(evt.Command);
        var detail = evt.AdapterThreadId is { } threadId
            ? $"Thread {threadId}"
            : evt.DebugSessionId;
        Finalize(
            context,
            evt,
            toolCallId,
            command,
            $"• {command}",
            [detail],
            isError: false);
        return ValueTask.CompletedTask;
    }

    internal static void Finalize(
        AgentTuiEventContext context,
        HPD.Agent.AgentEvent evt,
        string toolCallId,
        string action,
        string label,
        IReadOnlyList<string> lines,
        bool isError)
    {
        var key = $"hpd.coding.debug:activity:{toolCallId}";
        context.Shell.Transcript.FinalizeLive(
            key,
            new TranscriptEntry(
                Id: $"debug-activity-{toolCallId}",
                EntryKey: key,
                Cell: new DebugActivityCell(
                    toolCallId,
                    action,
                    label,
                    lines,
                    IsActive: false,
                    IsError: isError),
                Metadata: TranscriptEntryMetadata.FromEvent(evt),
                VerticalSpacing: 1).AsFinal(),
            CommittedHistoryMutationPolicy.Reject);
    }

    private static string Display(DebugExecutionCommand command)
        => command switch
        {
            DebugExecutionCommand.Continue => "Continued",
            DebugExecutionCommand.Pause => "Paused",
            DebugExecutionCommand.StepOver => "Stepped over",
            DebugExecutionCommand.StepIn => "Stepped in",
            DebugExecutionCommand.StepOut => "Stepped out",
            DebugExecutionCommand.StepBack => "Stepped back",
            DebugExecutionCommand.ReverseContinue => "Reverse continued",
            DebugExecutionCommand.RestartFrame => "Restarted frame",
            DebugExecutionCommand.Goto => "Moved execution",
            DebugExecutionCommand.TerminateThreads => "Terminated threads",
            _ => "Debug execution"
        };
}

internal sealed class DebugStateMutationTuiHandler
    : AgentTuiEventHandler<DebugStateMutationAppliedEvent>
{
    public override ValueTask HandleAsync(
        DebugStateMutationAppliedEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (evt.ToolCallId is not { Length: > 0 } toolCallId)
            return ValueTask.CompletedTask;
        DebugToolCallTuiCoordinator.Claim(
            context,
            toolCallId,
            DebugPresentationClaim.Mutation);
        var (action, label, lines) = evt.MutationKind switch
        {
            DebugStateMutationKind.Variable => (
                "setVariable",
                "• Changed variable",
                ValueLines(evt)),
            DebugStateMutationKind.Expression => (
                "setExpression",
                "• Changed expression",
                ValueLines(evt)),
            DebugStateMutationKind.Memory => (
                "writeMemory",
                "• Wrote memory",
                new[] { evt.ByteCount is { } count ? $"{count} bytes" : "Memory updated." }),
            _ => ("mutation", "• Changed debuggee state", new[] { "State updated." })
        };
        DebugExecutionCommandTuiHandler.Finalize(
            context,
            evt,
            toolCallId,
            action,
            label,
            lines,
            isError: false);
        return ValueTask.CompletedTask;
    }

    private static string[] ValueLines(DebugStateMutationAppliedEvent evt)
        => evt.SafeTargetName is { Length: > 0 } target
            ? [$"{target} = {evt.SafeNewValue ?? "<updated>"}"]
            : [evt.SafeNewValue ?? "Value updated."];
}
