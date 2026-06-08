using System.Text.Json;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Models;

namespace HPD.Agent.TUI.Console;

public sealed class TextMessageStreamHandler : IAgentTuiEventHandler
{
    private const string BuffersKey = "hpd.core.text.buffers";
    private const string RolesKey = "hpd.core.text.roles";

    public bool CanHandle(AgentEvent evt)
        => evt is TextMessageStartEvent or TextDeltaEvent or TextMessageEndEvent;

    public ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var buffers = context.State.GetOrCreate(
            BuffersKey,
            static () => new Dictionary<string, string>(StringComparer.Ordinal));
        var roles = context.State.GetOrCreate(
            RolesKey,
            static () => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        switch (evt)
        {
            case TextMessageStartEvent start:
                buffers[start.MessageId] = "";
                roles[start.MessageId] = start.Role;
                UpdateTextRow(context, start.MessageId, start.Role, "", start);
                break;

            case TextDeltaEvent delta:
                buffers.TryGetValue(delta.MessageId, out var current);
                current += delta.Text;
                buffers[delta.MessageId] = current;
                roles.TryGetValue(delta.MessageId, out var deltaRole);
                UpdateTextRow(context, delta.MessageId, deltaRole, current, delta);
                break;

            case TextMessageEndEvent end when buffers.TryGetValue(end.MessageId, out var final):
                roles.TryGetValue(end.MessageId, out var endRole);
                UpdateTextRow(context, end.MessageId, endRole, final, end);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static void UpdateTextRow(
        AgentTuiEventContext context,
        string messageId,
        string? role,
        string markdown,
        AgentEvent evt)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            context.Shell.Transcript.Update(new TranscriptEntry(
                Id: $"user-{messageId}",
                EntryKey: $"user:{messageId}",
                Cell: new UserMessageCell(new Text(markdown)),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: context.Scope.AgentId,
                    AgentName: "user")));
            return;
        }

        context.Shell.Transcript.Update(new TranscriptEntry(
            Id: $"assistant-{messageId}",
            EntryKey: $"assistant:{messageId}",
            Cell: new AssistantMessageCell(
                evt.Metadata?.AgentName ?? context.Scope.AgentId,
                new Markdown(string.IsNullOrWhiteSpace(markdown) ? "_thinking..._" : markdown),
                IsStreaming: true),
            Metadata: TranscriptEntryMetadata.FromEvent(evt)));
    }
}

public sealed class ReasoningStreamHandler : IAgentTuiEventHandler
{
    private const string BuffersKey = "hpd.core.reasoning.buffers";

    public bool CanHandle(AgentEvent evt)
        => evt is ReasoningMessageStartEvent or ReasoningDeltaEvent or ReasoningMessageEndEvent;

    public ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var buffers = context.State.GetOrCreate(
            BuffersKey,
            static () => new Dictionary<string, string>(StringComparer.Ordinal));

        switch (evt)
        {
            case ReasoningMessageStartEvent start:
                buffers[start.MessageId] = "";
                UpdateReasoningRow(context, start.MessageId, "", start);
                break;

            case ReasoningDeltaEvent delta:
                buffers.TryGetValue(delta.MessageId, out var current);
                current += delta.Text;
                buffers[delta.MessageId] = current;
                UpdateReasoningRow(context, delta.MessageId, current, delta);
                break;

            case ReasoningMessageEndEvent end when buffers.TryGetValue(end.MessageId, out var final):
                UpdateReasoningRow(context, end.MessageId, final, end);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static void UpdateReasoningRow(
        AgentTuiEventContext context,
        string messageId,
        string markdown,
        AgentEvent evt)
    {
        context.Shell.Transcript.Update(new TranscriptEntry(
            Id: $"reasoning-{messageId}",
            EntryKey: $"reasoning:{messageId}",
            Cell: new ReasoningMessageCell(
                new Markdown(string.IsNullOrWhiteSpace(markdown) ? "_reasoning..._" : markdown),
                IsStreaming: true),
            Metadata: TranscriptEntryMetadata.FromEvent(evt)));
    }
}

public sealed class ToolLifecycleHandler : IAgentTuiEventHandler
{
    private const string ToolRowsKey = "hpd.core.tool.rows";

    public bool CanHandle(AgentEvent evt)
        => evt is ToolCallStartEvent or ToolCallArgsEvent or ToolCallResultEvent or ToolCallEndEvent;

    public ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var rows = context.State.GetOrCreate(
            ToolRowsKey,
            static () => new Dictionary<string, ToolRowState>(StringComparer.Ordinal));

        switch (evt)
        {
            case ToolCallStartEvent start:
                rows[start.CallId] = new ToolRowState(start.Name, null, null, "running");
                UpdateToolRow(context, start.CallId, start, rows[start.CallId]);
                break;

            case ToolCallArgsEvent args when rows.TryGetValue(args.CallId, out var argsRow):
                rows[args.CallId] = argsRow with { ArgsJson = args.ArgsJson };
                UpdateToolRow(context, args.CallId, args, rows[args.CallId]);
                break;

            case ToolCallResultEvent result when rows.TryGetValue(result.CallId, out var resultRow):
                rows[result.CallId] = resultRow with { ResultSummary = SummarizeResult(result.Result) };
                UpdateToolRow(context, result.CallId, result, rows[result.CallId]);
                break;

            case ToolCallEndEvent end when rows.TryGetValue(end.CallId, out var completedRow):
                rows[end.CallId] = completedRow with { State = "completed" };
                UpdateToolRow(context, end.CallId, end, rows[end.CallId]);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static void UpdateToolRow(
        AgentTuiEventContext context,
        string callId,
        AgentEvent evt,
        ToolRowState row)
    {
        var args = string.IsNullOrWhiteSpace(row.ArgsJson)
            ? "args: pending"
            : $"args: `{row.ArgsJson}`";
        var result = string.IsNullOrWhiteSpace(row.ResultSummary)
            ? null
            : $"\n\nresult: `{row.ResultSummary}`";

        context.Shell.Transcript.Update(new TranscriptEntry(
            Id: $"tool-{callId}",
            EntryKey: $"tool:{callId}",
            Cell: new ToolCallCell(
                row.Name,
                ToRunState(row.State),
                Summary: new Markdown($"{args}{result}")),
            Metadata: new TranscriptEntryMetadata(
                AgentId: evt.Metadata?.AgentId ?? $"{context.Scope.AgentId}/tool",
                AgentName: evt.Metadata?.AgentName ?? "tool",
                ParentAgentId: evt.Metadata?.ParentAgentId ?? context.Scope.AgentId,
                AgentChain: evt.Metadata?.AgentChain ?? ["assistant", "tool"],
                AgentDepth: evt.Metadata?.Depth ?? 1)));
    }

    private static TranscriptRunState ToRunState(string state)
        => state.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? TranscriptRunState.Failed
            : state.Contains("running", StringComparison.OrdinalIgnoreCase)
                ? TranscriptRunState.Running
                : TranscriptRunState.Completed;

    private static string? SummarizeResult(ToolResultPayload result)
    {
        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            return Trim(result.Text);
        }

        if (result.Json is { } json)
        {
            return Trim(json.ValueKind == JsonValueKind.Undefined ? "" : json.ToString());
        }

        if (result.Content is { Count: > 0 } content)
        {
            return $"{content.Count} content item(s)";
        }

        return result.ResultType;
    }

    private static string Trim(string value)
        => value.Length <= 120 ? value : $"{value[..117]}...";

    private sealed record ToolRowState(
        string Name,
        string? ArgsJson,
        string? ResultSummary,
        string State);
}

public sealed class BranchRunStatusHandler : IAgentTuiEventHandler
{
    public bool CanHandle(AgentEvent evt)
        => evt is BranchRunStartedEvent or BranchRunCompletedEvent;

    public ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case BranchRunStartedEvent started:
                ApplyBranchRunStarted(context, started);
                break;

            case BranchRunCompletedEvent completed:
                ApplyBranchRunCompleted(context, completed);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static void ApplyBranchRunStarted(
        AgentTuiEventContext context,
        BranchRunStartedEvent started)
    {
        context.Shell.Activities.Add(new ActivityModel($"run {ShortId(started.RuntimeRunId)}")
        {
            State = ActivityState.Running,
            Severity = ActivitySeverity.Info
        });

        context.Shell.FooterText = $"state: running | run: {ShortId(started.RuntimeRunId)}";
    }

    private static void ApplyBranchRunCompleted(
        AgentTuiEventContext context,
        BranchRunCompletedEvent completed)
    {
        foreach (var activity in context.Shell.Activities.Activities.Where(activity => activity.State == ActivityState.Running))
        {
            activity.State = completed.ErrorMessage is null ? ActivityState.Completed : ActivityState.Failed;
            activity.Severity = completed.ErrorMessage is null ? ActivitySeverity.Success : ActivitySeverity.Error;
        }

        context.Shell.FooterText = completed.ErrorMessage is null
            ? "state: idle | last run completed"
            : $"state: failed | {completed.ErrorMessage}";
    }

    private static string ShortId(string value)
        => value[..Math.Min(8, value.Length)];
}
