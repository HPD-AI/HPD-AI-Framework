using HPD.Events;

namespace HPD.Agent.Security;

/// <summary>Identifies a capability controlled by the host security boundary.</summary>
public enum AgentCapabilityKind
{
    FilesystemRead,
    FilesystemWrite,
    NetworkEgress,
    LocalBinding,
    InteractiveTerminal,
    UnsandboxedExecution
}

/// <summary>Describes the narrow resource requested for a capability.</summary>
public sealed record AgentCapabilityResource
{
    /// <summary>Gets the canonical resource value, such as a path or domain.</summary>
    public required string Value { get; init; }

    /// <summary>Gets a bounded user-facing description of the resource.</summary>
    public required string DisplayName { get; init; }
}

/// <summary>Requests authority to cross an enforced agent sandbox boundary.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_CAPABILITY_REQUEST")]
public sealed record AgentCapabilityRequestEvent(
    string RequestId,
    string SourceName,
    string CallId,
    string OperationId,
    AgentCapabilityKind Capability,
    AgentCapabilityResource? Resource,
    string Reason) : AgentEvent, IAgentRequestEvent<AgentCapabilityResponseEvent>
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
}

/// <summary>Returns the host decision for an agent capability request.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_CAPABILITY_RESPONSE")]
public sealed record AgentCapabilityResponseEvent(
    string RequestId,
    string SourceName,
    bool Approved) : AgentEvent, IAgentResponseEvent
{
    public override EventChannel Channel { get; init; } = EventChannel.Interactive;
    public override EventKind Kind { get; init; } = EventKind.Control;
    public override EventDirection Direction { get; init; } = EventDirection.Upstream;
}
