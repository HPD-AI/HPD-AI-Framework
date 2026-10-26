using HPD.Agent.TUI.Composition;

namespace HPD.Agent.ToolHarness.Coding.TUI.Debugging;

internal enum DebugPresentationClaim
{
    Pending,
    Lifecycle,
    Breakpoint,
    Execution,
    Inspection,
    Mutation,
    Folded,
    Fallback
}

internal sealed class DebugToolCallPresentationStore : IAgentTuiProjectionState
{
    public const string StateKey = "hpd.coding.debug.tool-calls";
    private readonly Dictionary<string, DebugToolCallPresentationState> _calls =
        new(StringComparer.Ordinal);

    public DebugToolCallPresentationState Start(string callId)
    {
        if (!_calls.TryGetValue(callId, out var state))
        {
            state = new DebugToolCallPresentationState(callId);
            _calls.Add(callId, state);
        }
        return state;
    }

    public bool TryGet(string callId, out DebugToolCallPresentationState state)
        => _calls.TryGetValue(callId, out state!);

    public object Fork()
    {
        var fork = new DebugToolCallPresentationStore();
        foreach (var pair in _calls)
            fork._calls.Add(pair.Key, (DebugToolCallPresentationState)pair.Value.Fork());
        return fork;
    }
}

internal sealed class DebugToolCallPresentationState(string toolCallId) : IAgentTuiProjectionState
{
    public string ToolCallId { get; } = toolCallId;
    public string Action { get; set; } = "unknown";
    public DebugActionPresentationPolicy? Policy { get; set; }
    public DebugPresentationClaim Claim { get; set; } = DebugPresentationClaim.Pending;
    public bool ResultObserved { get; set; }
    public bool EndObserved { get; set; }
    public bool Succeeded { get; set; }
    public string? ResultText { get; set; }

    public object Fork()
        => new DebugToolCallPresentationState(ToolCallId)
        {
            Action = Action,
            Policy = Policy,
            Claim = Claim,
            ResultObserved = ResultObserved,
            EndObserved = EndObserved,
            Succeeded = Succeeded,
            ResultText = ResultText
        };
}
