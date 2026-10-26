using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;

namespace HPD.Agent.ToolHarness.Coding.TUI.SubAgents;

internal sealed class CodingSubAgentTuiHandler : IAgentTuiEventHandler, IAgentTuiToolCallHandler
{
    private const string StoreKey = "hpd.coding.subagent.store";
    private const int PromptLimit = 160;
    private const int SummaryLimit = 240;
    private const int ErrorLimit = 160;

    public bool CanHandle(AgentEvent evt) => evt switch
    {
        ToolCallStartEvent start => IsSubAgents(start.Name),
        ToolCallArgsEvent => true,
        ToolCallResultEvent result => IsSubAgents(result.Name),
        ToolCallEndEvent => true,
        SubAgentInvocationStartedEvent or SubAgentInvocationCompletedEvent or
            SubAgentInvocationFailedEvent or SubAgentInvocationCancelledEvent => true,
        _ => false
    };

    public bool CanHandleToolCall(string? toolHarnessName, string toolName, ToolCallType? callType)
        => IsSubAgents(toolName) ||
           toolName is "explore" or "worker" or "reviewer";

    public ValueTask ProjectAsync(AgentEvent evt, AgentTuiProjectionContext context, CancellationToken cancellationToken)
    {
        var store = context.State.GetOrCreate(StoreKey, static () => new Store());
        Entry? entry = null;

        switch (evt)
        {
            case ToolCallStartEvent start when IsSubAgents(start.Name):
                entry = store.GetOrCreate(start.CallId, start.Name);
                break;
            case ToolCallArgsEvent args when store.ByCall.TryGetValue(args.CallId, out entry):
                ApplyArgs(entry, context.ToolArguments);
                break;
            case SubAgentInvocationStartedEvent started:
                entry = store.GetOrCreate(started.ParentToolCallId, started.RoleName);
                entry.InvocationId = started.InvocationId;
                store.ByInvocation[started.InvocationId] = entry;
                entry.RoleName = started.RoleName;
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
            case ToolCallResultEvent result when IsSubAgents(result.Name) && store.ByCall.TryGetValue(result.CallId, out entry):
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

    private static void ApplyArgs(Entry entry, ToolCallArguments? arguments)
    {
        if (arguments is not { IsObject: true })
            return;

        var request = arguments.Root;
        if (arguments.Root.TryGetObject("request", out var nested))
            request = nested;
        if (request.TryGetString("action", out var action) && !string.IsNullOrWhiteSpace(action))
            entry.RoleName = action;
        if (request.TryGetString("child", out var child))
            entry.TaskName = child;
        if (request.TryGetString("input", out var input))
            entry.Detail = Limit(input, PromptLimit);
    }

    private static void Apply(AgentTuiProjectionContext context, Entry state, AgentEvent evt)
    {
        var key = $"coding.subagent:{state.CallId}";
        var transcriptEntry = new TranscriptEntry(
            $"coding-subagent-{state.CallId}",
            key,
            new CodingSubAgentCell(state.CallId, state.RoleName, state.TaskName, state.State,
                state.ContextPolicy, state.Mode, state.Detail),
            TranscriptEntryMetadata.FromEvent(evt));
        if (state.State is CodingSubAgentState.Preparing or CodingSubAgentState.Running)
            context.Shell.Transcript.UpsertLive(transcriptEntry.AsLive(), CommittedHistoryMutationPolicy.Reject);
        else
            context.Shell.Transcript.FinalizeLive(key, transcriptEntry.AsFinal(), CommittedHistoryMutationPolicy.Reject);
    }

    private static bool IsSubAgents(string? name)
        => string.Equals(name, SubAgentsFunctionFactory.FunctionName, StringComparison.Ordinal);

    private static string? Limit(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= limit ? normalized : $"{normalized[..(limit - 1)]}…";
    }

    private sealed class Store : IAgentTuiProjectionState
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

        public object Fork()
        {
            var fork = new Store();
            var entries = new Dictionary<Entry, Entry>();
            foreach (var pair in ByCall)
            {
                var copy = (Entry)pair.Value.Fork();
                entries[pair.Value] = copy;
                fork.ByCall.Add(pair.Key, copy);
            }
            foreach (var pair in ByInvocation)
            {
                if (!entries.TryGetValue(pair.Value, out var copy))
                {
                    copy = (Entry)pair.Value.Fork();
                    entries[pair.Value] = copy;
                }
                fork.ByInvocation.Add(pair.Key, copy);
            }
            return fork;
        }
    }

    private sealed class Entry(string callId, string roleName) : IAgentTuiProjectionState
    {
        public string CallId { get; } = callId;
        public string RoleName { get; set; } = roleName;
        public string? InvocationId { get; set; }
        public string? TaskName { get; set; }
        public CodingSubAgentState State { get; set; } = CodingSubAgentState.Preparing;
        public SubAgentContextPolicy? ContextPolicy { get; set; }
        public AgentInvocationMode? Mode { get; set; }
        public string? Detail { get; set; }

        public object Fork()
            => new Entry(CallId, RoleName)
            {
                InvocationId = InvocationId,
                TaskName = TaskName,
                State = State,
                ContextPolicy = ContextPolicy,
                Mode = Mode,
                Detail = Detail
            };
    }
}
