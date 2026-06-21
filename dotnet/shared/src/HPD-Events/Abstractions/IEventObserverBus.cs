namespace HPD.Events;

/// <summary>
/// Observer subscription surface for callback handlers owned by the event system.
/// </summary>
public interface IEventObserverBus
{
    /// <summary>
    /// Register a typed callback observer processed by a background subscriber pump.
    /// </summary>
    IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : Event;

    /// <summary>
    /// Register a callback observer for all class events processed by a background subscriber pump.
    /// </summary>
    IDisposable SubscribeAny(
        Func<Event, ValueTask> handler,
        EventSubscriptionOptions? options = null);
}
