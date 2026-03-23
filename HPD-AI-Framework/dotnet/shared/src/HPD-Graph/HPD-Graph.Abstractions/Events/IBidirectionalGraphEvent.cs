using HPD.Events;

namespace HPDAgent.Graph.Abstractions.Events;

/// <summary>
/// Marker interface for graph-specific bidirectional events.
/// Inherits from HPD.Events.IBidirectionalEvent for cross-domain consistency.
/// Events implementing this interface can:
/// - Be emitted during node/graph execution
/// - Bubble to parent coordinators via SetParent()
/// - Wait for responses using WaitForResponseAsync()
/// </summary>
/// <remarks>
/// Used for Human-in-the-Loop (HITL) scenarios in graph execution:
/// - Node approval requests (before executing sensitive operations)
/// - User clarifications (when nodes need additional input)
/// - Progress confirmations (for long-running operations)
/// </remarks>
public interface IBidirectionalGraphEvent : IBidirectionalEvent
{
    // Inherits RequestId and SourceName from HPD.Events.IBidirectionalEvent
}
