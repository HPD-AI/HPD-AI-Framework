namespace HPD.Events;

/// <summary>
/// Registry for managing interruptible event flows.
/// Allows event flows to be interrupted as a group (e.g., canceling an agent turn).
/// </summary>
public interface IEventFlowRegistry
{
    /// <summary>
    /// Create a new interruptible event flow with optional auto-generated ID.
    /// Convenience method for when you don't need to specify an event-flow ID.
    /// </summary>
    /// <param name="eventFlowId">Optional unique identifier. If null, generates a new GUID.</param>
    /// <returns>Handle for controlling the event flow.</returns>
    IEventFlowHandle Create(string? eventFlowId = null);

    /// <summary>
    /// Begin a new interruptible event flow.
    /// Events emitted with this event flow ID can be interrupted using the returned handle.
    /// </summary>
    /// <param name="eventFlowId">Unique identifier for the event flow.</param>
    /// <returns>Handle for controlling the event flow.</returns>
    IEventFlowHandle BeginFlow(string eventFlowId);

    /// <summary>
    /// Gets an existing event-flow handle by ID.
    /// </summary>
    /// <param name="eventFlowId">Event-flow ID to retrieve.</param>
    /// <returns>Event-flow handle if found, null otherwise.</returns>
    IEventFlowHandle? Get(string eventFlowId);

    /// <summary>
    /// Interrupt all events in the specified event flow.
    /// Events with CanInterrupt=true and matching EventFlowId will be dropped.
    /// </summary>
    /// <param name="eventFlowId">Event-flow ID to interrupt.</param>
    void InterruptFlow(string eventFlowId);

    /// <summary>
    /// Complete an event flow normally.
    /// Removes the event flow from the registry to free resources.
    /// </summary>
    /// <param name="eventFlowId">Event-flow ID to complete.</param>
    void CompleteFlow(string eventFlowId);

    /// <summary>
    /// Check if an event flow is currently active.
    /// </summary>
    /// <param name="eventFlowId">Event-flow ID to check.</param>
    /// <returns>True if the event flow exists and is active.</returns>
    bool IsActive(string eventFlowId);

    /// <summary>
    /// Interrupt all active event flows.
    /// </summary>
    void InterruptAll();

    /// <summary>
    /// Interrupt event flows matching a predicate.
    /// </summary>
    /// <param name="predicate">Predicate to filter event flows.</param>
    void InterruptWhere(Func<IEventFlowHandle, bool> predicate);

    /// <summary>
    /// Gets all active event flows.
    /// </summary>
    IReadOnlyList<IEventFlowHandle> ActiveFlows { get; }

    /// <summary>
    /// Gets the count of active event flows.
    /// </summary>
    int ActiveCount { get; }
}
