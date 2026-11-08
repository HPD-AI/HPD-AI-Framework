namespace HPD.Agent.Client;

/// <summary>Result of accepting a permission, question, continuation, or client-tool response.</summary>
public sealed record HpdAgentResponseResult(
    int Status,
    string RequestId,
    string? Message,
    bool? Accepted)
{
    public bool IsAccepted => Accepted ?? Status == (int)HpdAgentResponseStatus.Accepted;

    public HpdAgentResponseStatus ResponseStatus =>
        Enum.IsDefined(typeof(HpdAgentResponseStatus), Status)
            ? (HpdAgentResponseStatus)Status
            : HpdAgentResponseStatus.Unknown;
}

/// <summary>Stable values used by the server response-resolution status field.</summary>
public enum HpdAgentResponseStatus
{
    Accepted = 0,
    NotFound = 1,
    AlreadyResolved = 2,
    TimedOut = 3,
    Cancelled = 4,
    ResponseTypeMismatch = 5,
    TargetMismatch = 6,
    AmbiguousRequest = 7,
    ExecutionEnded = 8,
    RuntimeUnavailable = 9,
    Unknown = -1
}
