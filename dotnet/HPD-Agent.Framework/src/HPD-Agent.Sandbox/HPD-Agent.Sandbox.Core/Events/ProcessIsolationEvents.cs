using HPD.Agent;
using HPD.Environment.Contracts;
using HPD.Agent.Serialization;

namespace HPD.Agent.Sandbox.Events;

[EventType("PROCESS_ISOLATION_VIOLATION", Durability = AgentEventDurability.LiveOnly)]
public sealed record ProcessIsolationViolationEvent : AgentEvent
{
    public string SourceName => "HostSandboxApplicator";
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

[EventType("PROCESS_ISOLATION_INITIALIZED", Durability = AgentEventDurability.LiveOnly)]
public sealed record ProcessIsolationInitializedEvent : AgentEvent, IObservabilityEvent
{
    public string Platform { get; init; } = string.Empty;
    public int? HttpProxyPort { get; init; }
    public int? Socks5ProxyPort { get; init; }
}
