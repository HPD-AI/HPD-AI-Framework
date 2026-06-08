using HPD.Events.Struct;

namespace HPD.Agent;

/// <summary>
/// Marker contract for HPD Agent-owned process-local struct events.
/// </summary>
/// <remarks>
/// Agent struct events are local hot-path samples, not serialized AgentEvent values.
/// They flow through the underlying HPD.Events struct hub while keeping HPD Agent's
/// public surface distinct from lower-level struct events.
/// </remarks>
public interface AgentStructEvent : IStructEvent;
