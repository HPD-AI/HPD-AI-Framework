using HPD.Agent;
using HPD.Execution.Contracts;

namespace HPD.Execution.Local.Events;

public sealed record ProcessIsolationViolationEvent : AgentEvent
{
    public string SourceName => "LocalProcessProvider";
    public string ProcessId { get; init; } = string.Empty;
    public ProcessIsolationViolationType ViolationType { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Path { get; init; }

    public ProcessIsolationViolationEvent() { }

    public ProcessIsolationViolationEvent(
        string processId,
        ProcessIsolationViolationType violationType,
        string message,
        string? path = null)
    {
        ProcessId = processId;
        ViolationType = violationType;
        Message = message;
        Path = path;
    }
}

public sealed record ProcessIsolationInitializedEvent : AgentEvent, IObservabilityEvent
{
    public string Platform { get; init; } = string.Empty;
    public int? HttpProxyPort { get; init; }
    public int? Socks5ProxyPort { get; init; }
}

public abstract record LocalProcessInvocationEvent : AgentEvent, IObservabilityEvent
{
    public string SourceName => "LocalProcessProvider";
    public string ProcessId { get; init; } = string.Empty;
    public string CommandKind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public NetworkEgressMode NetworkMode { get; init; }
    public string Platform { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
}

public sealed record LocalProcessInvocationStartingEvent : LocalProcessInvocationEvent;

public sealed record LocalProcessInvocationStartedEvent : LocalProcessInvocationEvent
{
    public int SystemProcessId { get; init; }
}

public sealed record LocalProcessInvocationCompletedEvent : LocalProcessInvocationEvent
{
    public int ExitCode { get; init; }
}

public sealed record LocalProcessInvocationFailedEvent : LocalProcessInvocationEvent
{
    public string Message { get; init; } = string.Empty;
}

public sealed record LocalProcessInvocationTimedOutEvent : LocalProcessInvocationEvent
{
    public TimeSpan Timeout { get; init; }
}

public sealed record LocalProcessInvocationCancelledEvent : LocalProcessInvocationEvent;

public sealed record LocalProcessInvocationKilledEvent : LocalProcessInvocationEvent
{
    public string Reason { get; init; } = string.Empty;
}
