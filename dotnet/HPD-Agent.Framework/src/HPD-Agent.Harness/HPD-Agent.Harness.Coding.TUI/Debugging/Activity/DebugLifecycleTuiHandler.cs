using HPD.Agent;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPDOS.ToolHarnesses.Middleware;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal sealed class DebugLifecyclePresentationStore
{
    public const string StateKey = "hpd.coding.debug.lifecycle-presentation";
    private readonly Dictionary<string, DebugLifecyclePresentationState> _trees =
        new(StringComparer.Ordinal);

    public DebugLifecyclePresentationState Tree(DebugLifecycleEvent @event)
    {
        if (!_trees.TryGetValue(@event.DebugTreeId, out var state))
        {
            state = new DebugLifecyclePresentationState(
                @event.DebugTreeId,
                @event.DebugSessionId,
                @event.AdapterId,
                @event.ToolCallId);
            _trees.Add(@event.DebugTreeId, state);
        }
        state.ToolCallId ??= @event.ToolCallId;
        state.DebugSessionId = @event.DebugSessionId;
        state.AdapterId = @event.AdapterId;
        return state;
    }

    public bool TryByToolCall(string toolCallId, out DebugLifecyclePresentationState state)
    {
        state = _trees.Values.FirstOrDefault(item =>
            string.Equals(item.ToolCallId, toolCallId, StringComparison.Ordinal))!;
        return state is not null;
    }
}

internal sealed class DebugLifecyclePresentationState(
    string debugTreeId,
    string debugSessionId,
    string adapterId,
    string? toolCallId)
{
    public string DebugTreeId { get; } = debugTreeId;
    public string DebugSessionId { get; set; } = debugSessionId;
    public string AdapterId { get; set; } = adapterId;
    public string? ToolCallId { get; set; } = toolCallId;
    public string Status { get; set; } = "preparing";
    public string? Detail { get; set; }
    public bool Terminal { get; set; }
    public bool Failed { get; set; }
}

internal sealed class DebugLifecycleTuiHandler
    : AgentTuiEventHandler<DebugLifecycleEvent>
{
    public override ValueTask HandleAsync(
        DebugLifecycleEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        if (!IsMaterial(evt)) return ValueTask.CompletedTask;
        var store = context.State.GetOrCreate(
            DebugLifecyclePresentationStore.StateKey,
            static () => new DebugLifecyclePresentationStore());
        var state = store.Tree(evt);
        Reduce(state, evt);
        if (state.ToolCallId is { Length: > 0 } callId)
            DebugToolCallTuiCoordinator.Claim(
                context,
                callId,
                DebugPresentationClaim.Lifecycle);
        Apply(context, state, evt);
        return ValueTask.CompletedTask;
    }

    private static bool IsMaterial(DebugLifecycleEvent evt)
        => evt is DebugExecutionPlannedEvent
            or DebugExecutionActivatingEvent
            or DebugHostReadyEvent
            or DebugTreeStartedEvent
            or DebugRestartTransitionEvent
            or DebugChildSessionStartedEvent
            or DebugExecutionActivationFailedEvent
            or DebugSessionFailedEvent
            or DebugTreeFaultedEvent
            or DebugTreeCompletedEvent;

    private static void Reduce(
        DebugLifecyclePresentationState state,
        DebugLifecycleEvent evt)
    {
        // Terminal lifecycle publication is authoritative. Late transport and
        // host teardown events must not rewrite the completed user outcome.
        if (state.Terminal)
            return;

        switch (evt)
        {
            case DebugExecutionPlannedEvent planned:
                state.Status = "preparing";
                state.Detail = $"{Display(planned.SemanticStartKind)} · {planned.AdapterId}";
                break;
            case DebugExecutionActivatingEvent:
                state.Status = "starting";
                state.Detail = $"Activating {state.AdapterId}…";
                break;
            case DebugHostReadyEvent ready:
                state.Status = "host ready";
                state.Detail = Display(ready.SafeProcessRole);
                break;
            case DebugTreeStartedEvent:
                state.Status = "running";
                state.Detail = state.AdapterId;
                break;
            case DebugRestartTransitionEvent restart:
                state.Status = "restarting";
                state.Detail = restart.InPlace ? "Restarting in place…" : "Starting replacement session…";
                break;
            case DebugChildSessionStartedEvent:
                state.Detail = $"{state.AdapterId} · child session started";
                break;
            case DebugExecutionActivationFailedEvent failed:
                Fail(state, failed.SafeReasonCode);
                break;
            case DebugSessionFailedEvent failed:
                Fail(state, failed.SafeReasonCode);
                break;
            case DebugTreeFaultedEvent faulted:
                Fail(state, faulted.SafeReasonCode);
                break;
            case DebugTreeCompletedEvent completed:
                state.Status = completed.FinalStatus;
                state.Terminal = true;
                state.Failed = !string.Equals(completed.FinalStatus, "Terminated", StringComparison.OrdinalIgnoreCase) ||
                    completed.ExitCode is not (null or 0);
                state.Detail = Summary(completed);
                break;
        }
    }

    private static void Fail(DebugLifecyclePresentationState state, string reason)
    {
        state.Status = "failed";
        state.Detail = Display(reason);
        state.Terminal = true;
        state.Failed = true;
    }

    private static string Summary(DebugTreeCompletedEvent summary)
    {
        var parts = new List<string>();
        if (summary.ExitCode is { } exitCode) parts.Add($"exit {exitCode}");
        parts.Add(FormatDuration(summary.DurationMilliseconds));
        if (summary.ChildSessionCount > 0)
            parts.Add($"{summary.ChildSessionCount} child session{(summary.ChildSessionCount == 1 ? "" : "s")}");
        if (summary.Breakpoints.Requested > 0)
            parts.Add($"{summary.Breakpoints.Verified}/{summary.Breakpoints.Requested} breakpoints resolved");
        if (summary.BreakpointStopCount > 0)
            parts.Add($"{summary.BreakpointStopCount} breakpoint stop{(summary.BreakpointStopCount == 1 ? "" : "s")}");
        return string.Join(" · ", parts);
    }

    private static void Apply(
        AgentTuiEventContext context,
        DebugLifecyclePresentationState state,
        AgentEvent evt)
    {
        var key = state.Terminal
            ? $"hpd.coding.debug:lifecycle:complete:{state.DebugTreeId}"
            : $"hpd.coding.debug:lifecycle:start:{state.DebugTreeId}";
        var label = state.Failed
            ? "• Debugging failed"
            : state.Terminal
                ? "• Debug session completed"
                : state.Status == "running"
                    ? "• Debugging started"
                    : $"• Debugging · {state.Status}";
        var lines = string.IsNullOrWhiteSpace(state.Detail)
            ? [state.AdapterId]
            : new[] { state.Detail };
        var entry = new TranscriptEntry(
            Id: $"debug-lifecycle-{(state.Terminal ? "complete" : "start")}-{state.DebugTreeId}",
            EntryKey: key,
            Cell: new DebugActivityCell(
                state.ToolCallId ?? state.DebugTreeId,
                "launch",
                label,
                lines,
                !state.Terminal,
                state.Failed),
            Metadata: TranscriptEntryMetadata.FromEvent(evt),
            VerticalSpacing: 1);
        if (state.Terminal || state.Status == "running")
            context.Shell.Transcript.FinalizeLive(key, entry.AsFinal());
        else context.Shell.Transcript.UpsertLive(entry.AsLive());
    }

    internal static void ObserveToolResult(
        AgentTuiEventContext context,
        string toolCallId,
        string resultText,
        AgentEvent evt)
    {
        var store = context.State.GetOrCreate(
            DebugLifecyclePresentationStore.StateKey,
            static () => new DebugLifecyclePresentationStore());
        if (!store.TryByToolCall(toolCallId, out var state)) return;
        try
        {
            var root = System.Xml.Linq.XDocument.Parse(resultText).Root;
            if (root?.Attribute("warning")?.Value != "no_initial_stop_strategy") return;
            state.Detail =
                "No stopping strategy was configured; the process may complete before inspection.";
            Apply(context, state, evt);
        }
        catch
        {
            // The coordinator owns malformed-result fallback.
        }
    }

    private static string FormatDuration(long milliseconds)
        => milliseconds < 1_000
            ? $"{milliseconds}ms"
            : $"{milliseconds / 1_000d:0.0}s";

    private static string Display(object value)
    {
        var text = value.ToString() ?? "unknown";
        return string.Concat(text.Select((character, index) =>
            index > 0 && char.IsUpper(character) ? $" {char.ToLowerInvariant(character)}" : character.ToString()))
            .Replace('_', ' ');
    }
}
