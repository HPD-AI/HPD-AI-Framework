namespace HPD.Events;

/// <summary>
/// Base marker interface for events that support bidirectional communication.
/// Events implementing this interface can:
/// - Be emitted during execution
/// - Bubble to parent coordinators via SetParent()
/// - Wait for responses using WaitForResponseAsync()
/// </summary>
/// <remarks>
/// This is the foundation interface used across all HPD domains (Agent, Graph, etc.).
/// Domain-specific interfaces should inherit from this (e.g., IBidirectionalAgentEvent, IBidirectionalGraphEvent).
/// </remarks>
public interface IBidirectionalEvent
{
    /// <summary>
    /// Unique identifier for this request/response interaction.
    /// Used to correlate requests and responses across the event stream.
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// Name/identifier of the component that emitted this event.
    /// Examples: Middleware name, Node ID, Handler name, etc.
    /// </summary>
    string SourceName { get; }
}
