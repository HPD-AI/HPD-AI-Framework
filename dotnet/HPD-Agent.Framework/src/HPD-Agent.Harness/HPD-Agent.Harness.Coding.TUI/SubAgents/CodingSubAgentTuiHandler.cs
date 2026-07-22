using System.Text.Json;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.SubAgents;

internal sealed class CodingSubAgentTuiHandler : IAgentTuiEventHandler
{
    private const string StoreKey = "hpd.coding.subagent.store";
    private const int PromptLimit = 160;
    private const int SummaryLimit = 240;
    private const int ErrorLimit = 160;

    public bool CanHandle(AgentEvent evt) => evt switch
    {
        ToolCallStartEvent start => IsSubAgent(start.Name),
        ToolCallArgsEvent => true,
        ToolCallResultEvent result => IsSubAgent(result.Name),
        ToolCallEndEvent => true,
        SubAgentInvocationStartedEvent or SubAgentInvocationCompletedEvent or
            SubAgentInvocationFailedEvent or SubAgentInvocationCancelledEvent => true,
        _ => false
    };

    public ValueTask HandleAsync(AgentEvent evt, AgentTuiEventContext context, CancellationToken cancellationToken)
    {
        var store = context.State.GetOrCreate(StoreKey, static () => new Store());
        Entry? entry = null;

        switch (evt)
        {
            case ToolCallStartEvent start when IsSubAgent(start.Name):
                entry = store.GetOrCreate(start.CallId, start.Name);
                break;
            case ToolCallArgsEvent args when store.ByCall.TryGetValue(args.CallId, out entry):
                ApplyArgs(entry, args.ArgsJson);
                break;
            case SubAgentInvocationStartedEvent started:
                entry = store.GetOrCreate(started.ParentToolCallId, started.RoleName);
                entry.InvocationId = started.InvocationId;
                store.ByInvocation[started.InvocationId] = entry;
                entry.RoleName = started.RoleName;
                entry.TaskName = started.TaskName;
                entry.ContextPolicy = started.ContextPolicy;
                entry.Mode = started.Mode;
                entry.State = CodingSubAgentState.Running;
                break;
            case SubAgentInvocationCompletedEvent completed:
                entry = store.GetOrCreateInvocation(completed.InvocationId);
                entry.State = CodingSubAgentState.Completed;
                entry.Detail = Limit(completed.Summary, SummaryLimit);
                break;
            case SubAgentInvocationFailedEvent failed:
                entry = store.GetOrCreateInvocation(failed.InvocationId);
                entry.State = CodingSubAgentState.Failed;
                entry.Detail = Limit(failed.Message, ErrorLimit);
                break;
            case SubAgentInvocationCancelledEvent cancelled:
                entry = store.GetOrCreateInvocation(cancelled.InvocationId);
                entry.State = CodingSubAgentState.Cancelled;
                entry.Detail = Limit(cancelled.Reason, ErrorLimit);
                break;
            case ToolCallResultEvent result when IsSubAgent(result.Name) && store.ByCall.TryGetValue(result.CallId, out entry):
                if (entry.State == CodingSubAgentState.Preparing)
                {
                    entry.State = CodingSubAgentState.Completed;
                    entry.Detail = Limit(result.Result.Text ?? result.Result.Json?.GetRawText(), SummaryLimit);
                }
                break;
            case ToolCallEndEvent end when store.ByCall.TryGetValue(end.CallId, out entry):
                if (entry.State == CodingSubAgentState.Preparing)
                    entry.State = CodingSubAgentState.Completed;
                break;
        }

        if (entry is not null) Apply(context, entry, evt);
        return ValueTask.CompletedTask;
    }

    private static void ApplyArgs(Entry entry, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("taskName", out var task) && task.ValueKind == JsonValueKind.String)
                entry.TaskName = task.GetString();
            if (root.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.String)
                entry.Detail = Limit(input.GetString(), PromptLimit);
        }
        catch (JsonException)
        {
            // Streaming tool arguments may be incomplete until a later event.
        }
    }

    private static void Apply(AgentTuiEventContext context, Entry state, AgentEvent evt)
    {
        var key = $"coding.subagent:{state.CallId}";
        var transcriptEntry = new TranscriptEntry(
            $"coding-subagent-{state.CallId}",
            key,
            new CodingSubAgentCell(state.CallId, state.RoleName, state.TaskName, state.State,
                state.ContextPolicy, state.Mode, state.Detail),
            TranscriptEntryMetadata.FromEvent(evt));
        if (state.State is CodingSubAgentState.Preparing or CodingSubAgentState.Running)
            context.Shell.Transcript.UpsertLive(transcriptEntry.AsLive());
        else
            context.Shell.Transcript.FinalizeLive(key, transcriptEntry.AsFinal());
    }

    private static bool IsSubAgent(string? name)
        => name is not null && (name.Equals("explore", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("worker", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("reviewer", StringComparison.OrdinalIgnoreCase));

    private static string? Limit(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= limit ? normalized : $"{normalized[..(limit - 1)]}…";
    }

    private sealed class Store
    {
        public Dictionary<string, Entry> ByCall { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Entry> ByInvocation { get; } = new(StringComparer.Ordinal);

        public Entry GetOrCreate(string callId, string roleName)
        {
            if (!ByCall.TryGetValue(callId, out var entry))
                ByCall[callId] = entry = new Entry(callId, roleName);
            return entry;
        }

        public Entry GetOrCreateInvocation(string invocationId)
        {
            if (ByInvocation.TryGetValue(invocationId, out var entry)) return entry;
            entry = GetOrCreate($"invocation:{invocationId}", "subagent");
            entry.InvocationId = invocationId;
            ByInvocation[invocationId] = entry;
            return entry;
        }
    }

    private sealed class Entry(string callId, string roleName)
    {
        public string CallId { get; } = callId;
        public string RoleName { get; set; } = roleName;
        public string? InvocationId { get; set; }
        public string? TaskName { get; set; }
        public CodingSubAgentState State { get; set; } = CodingSubAgentState.Preparing;
        public SubAgentContextPolicy? ContextPolicy { get; set; }
        public AgentInvocationMode? Mode { get; set; }
        public string? Detail { get; set; }
    }
}
