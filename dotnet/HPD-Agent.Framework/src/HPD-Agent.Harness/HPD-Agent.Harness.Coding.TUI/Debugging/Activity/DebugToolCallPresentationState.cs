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

internal sealed class DebugToolCallPresentationStore
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
}

internal sealed class DebugToolCallPresentationState(string toolCallId)
{
    public string ToolCallId { get; } = toolCallId;
    public string Action { get; set; } = "unknown";
    public DebugActionPresentationPolicy? Policy { get; set; }
    public DebugPresentationClaim Claim { get; set; } = DebugPresentationClaim.Pending;
    public bool ResultObserved { get; set; }
    public bool EndObserved { get; set; }
    public bool Succeeded { get; set; }
    public string? ResultText { get; set; }
}
