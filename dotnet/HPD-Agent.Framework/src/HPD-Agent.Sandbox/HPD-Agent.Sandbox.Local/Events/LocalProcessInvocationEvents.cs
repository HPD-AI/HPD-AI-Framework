using HPD.Agent;
using HPD.Environment.Contracts;
using HPD.Agent.Serialization;

namespace HPD.Agent.Sandbox.Local.Events;

public abstract record LocalProcessInvocationEvent : AgentEvent, IObservabilityEvent
{
    public string SourceName => "LocalSandbox";
    public string ProcessId { get; init; } = string.Empty;
    public string CommandKind { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string? WorkingDirectory { get; init; }
    public NetworkEgressMode NetworkMode { get; init; }
    public string Platform { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
}

[EventType("LOCAL_PROCESS_INVOCATION_STARTING")]
public sealed record LocalProcessInvocationStartingEvent : LocalProcessInvocationEvent;

[EventType("LOCAL_PROCESS_INVOCATION_STARTED")]
public sealed record LocalProcessInvocationStartedEvent : LocalProcessInvocationEvent
{
    public int SystemProcessId { get; init; }
}

[EventType("LOCAL_PROCESS_INVOCATION_COMPLETED")]
public sealed record LocalProcessInvocationCompletedEvent : LocalProcessInvocationEvent
{
    public int ExitCode { get; init; }
}

[EventType("LOCAL_PROCESS_INVOCATION_FAILED")]
public sealed record LocalProcessInvocationFailedEvent : LocalProcessInvocationEvent
{
    public string Message { get; init; } = string.Empty;
}

[EventType("LOCAL_PROCESS_INVOCATION_TIMED_OUT")]
public sealed record LocalProcessInvocationTimedOutEvent : LocalProcessInvocationEvent
{
    public TimeSpan Timeout { get; init; }
}

[EventType("LOCAL_PROCESS_INVOCATION_CANCELLED")]
public sealed record LocalProcessInvocationCancelledEvent : LocalProcessInvocationEvent;

[EventType("LOCAL_PROCESS_INVOCATION_KILLED")]
public sealed record LocalProcessInvocationKilledEvent : LocalProcessInvocationEvent
{
    public string Reason { get; init; } = string.Empty;
}
