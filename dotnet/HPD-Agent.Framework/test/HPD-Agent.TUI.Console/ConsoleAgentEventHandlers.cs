using System.Text.Json;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Models;

namespace HPD.Agent.TUI.Console;

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

        context.Shell.Transcript.UpsertLive(new TranscriptEntry(
            Id: $"tool-{callId}",
            EntryKey: $"tool:{callId}",
            Cell: new ToolCallCell(
                row.Name,
                ToRunState(row.State),
                Summary: HPD.TUI.Content.MarkdownBlock.Create($"{args}{result}")),
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

public sealed class ThreadExecutionStatusHandler : IAgentTuiEventHandler
{
    public bool CanHandle(AgentEvent evt)
        => evt is ThreadExecutionStartedEvent or ThreadExecutionFinishedEvent;

    public ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        switch (evt)
        {
            case ThreadExecutionStartedEvent started:
                ApplyThreadExecutionStarted(context, started);
                break;

            case ThreadExecutionFinishedEvent completed:
                ApplyThreadExecutionFinished(context, completed);
                break;
        }

        return ValueTask.CompletedTask;
    }

    private static void ApplyThreadExecutionStarted(
        AgentTuiEventContext context,
        ThreadExecutionStartedEvent started)
    {
        context.Shell.Activities.Add(new ActivityModel($"run {ShortId(started.ThreadExecutionId)}")
        {
            State = ActivityState.Running,
            Severity = ActivitySeverity.Info
        });

        context.Shell.PromptStatusText = $"state: running | run: {ShortId(started.ThreadExecutionId)}";
    }

    private static void ApplyThreadExecutionFinished(
        AgentTuiEventContext context,
        ThreadExecutionFinishedEvent completed)
    {
        foreach (var activity in context.Shell.Activities.Activities.Where(activity => activity.State == ActivityState.Running))
        {
            activity.State = completed.Outcome switch
            {
                ThreadExecutionOutcome.Failed => ActivityState.Failed,
                ThreadExecutionOutcome.Cancelled => ActivityState.Cancelled,
                _ => ActivityState.Completed
            };
            activity.Severity = completed.Outcome switch
            {
                ThreadExecutionOutcome.Failed => ActivitySeverity.Error,
                ThreadExecutionOutcome.Cancelled => ActivitySeverity.Warning,
                _ => ActivitySeverity.Success
            };
        }

        context.Shell.PromptStatusText = completed.Outcome switch
        {
            ThreadExecutionOutcome.Failed => $"state: failed | {completed.Error?.Message}",
            ThreadExecutionOutcome.Cancelled => "state: idle | cancelled",
            _ => "state: idle | last execution succeeded"
        };
    }

    private static string ShortId(string value)
        => value[..Math.Min(8, value.Length)];
}
