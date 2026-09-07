using HPD.Agent;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.Harness;

internal sealed class CodingHarnessToolCallHandler : IAgentTuiEventHandler, IAgentTuiToolCallHandler
{
    private const string ToolName = "CodingToolHarness";
    private const string StateKey = "hpd.coding.harness.tool-calls";
    private const string EntryKey = "coding.harness";
    private const string ActiveSummary = "Expanding coding tools";
    private const string CompletedSummary = "Ready: file operations, code search, shell execution, and code analysis";

    public bool CanHandle(AgentEvent evt)
        => evt switch
        {
            ToolCallStartEvent start => IsCodingHarness(start.Name, start.ToolHarnessName),
            ToolCallResultEvent result => IsCodingHarness(result.Name, result.ToolHarnessName),
            ToolCallEndEvent end => IsCodingHarness(end.Name, null),
            _ => false
        };

    public bool CanHandleToolCall(string? toolHarnessName, string toolName, ToolCallType? callType)
        => string.Equals(toolName, ToolName, StringComparison.Ordinal);

    public ValueTask HandleAsync(
        AgentEvent evt,
        AgentTuiEventContext context,
        CancellationToken cancellationToken)
    {
        var state = context.State.GetOrCreate(
            StateKey,
            static () => new CodingHarnessToolState());

        switch (evt)
        {
            case ToolCallStartEvent start:
                if (state.HasRendered)
                {
                    context.Shell.Transcript.RemoveLive($"tool:{start.CallId}");
                    break;
                }

                state.HasRendered = true;
                state.PrimaryCallId = start.CallId;
                state.IsActive = true;
                state.Summary = ActiveSummary;
                Apply(context, start, state);
                break;

            case ToolCallResultEvent result:
            {
                if (state.PrimaryCallId is not null &&
                    !string.Equals(state.PrimaryCallId, result.CallId, StringComparison.Ordinal))
                {
                    context.Shell.Transcript.RemoveLive($"tool:{result.CallId}");
                    break;
                }

                state.HasRendered = true;
                state.PrimaryCallId ??= result.CallId;
                state.IsActive = false;
                state.Summary = SummarizeResult(result.Result);
                Apply(context, result, state);
                break;
            }

            case ToolCallEndEvent end:
            {
                if (state.PrimaryCallId is not null &&
                    !string.Equals(state.PrimaryCallId, end.CallId, StringComparison.Ordinal))
                {
                    context.Shell.Transcript.RemoveLive($"tool:{end.CallId}");
                    break;
                }

                state.HasRendered = true;
                state.PrimaryCallId ??= end.CallId;
                state.IsActive = false;
                Apply(context, end, state);
                break;
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool IsCodingHarness(string? name, string? toolHarnessName)
        => string.Equals(name, ToolName, StringComparison.Ordinal);

    private static void Apply(
        AgentTuiEventContext context,
        AgentEvent evt,
        CodingHarnessToolState state)
    {
        var entry = new TranscriptEntry(
            Id: "coding-harness",
            EntryKey: EntryKey,
            Cell: new CodingHarnessToolCell(
                state.PrimaryCallId ?? "coding-harness",
                state.IsActive,
                state.Summary),
            Metadata: TranscriptEntryMetadata.FromEvent(evt),
            VerticalSpacing: 1);

        if (state.IsActive)
        {
            context.Shell.Transcript.UpsertLive(entry, CommittedHistoryMutationPolicy.Reject);
            return;
        }

        context.Shell.Transcript.FinalizeLive(EntryKey, entry, CommittedHistoryMutationPolicy.Reject);
    }

    private static string SummarizeResult(ToolResultPayload result)
    {
        var text = result.Text;
        if (!string.IsNullOrWhiteSpace(text) &&
            text.Contains("expanded", StringComparison.OrdinalIgnoreCase))
        {
            return CompletedSummary;
        }

        if (result.Json is { ValueKind: System.Text.Json.JsonValueKind.String } json)
        {
            var jsonText = json.GetString();
            if (!string.IsNullOrWhiteSpace(jsonText) &&
                jsonText.Contains("expanded", StringComparison.OrdinalIgnoreCase))
            {
                return CompletedSummary;
            }
        }

        return CompletedSummary;
    }

    private sealed class CodingHarnessToolState
    {
        public string? PrimaryCallId { get; set; }

        public bool HasRendered { get; set; }

        public bool IsActive { get; set; }

        public string Summary { get; set; } = ActiveSummary;
    }
}
