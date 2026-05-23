namespace HPD.Events;

/// <summary>
/// Owned inbox source for deterministic class-event consumers.
/// </summary>
public interface IEventInboxSource
{
    /// <summary>
    /// Create a caller-owned typed event inbox. The caller owns the reader loop.
    /// </summary>
    EventInbox<TEvent> CreateInbox<TEvent>(
        EventInboxOptions? options = null)
        where TEvent : Event;

    /// <summary>
    /// Create a caller-owned inbox filtered to one event channel.
    /// </summary>
    EventInbox<Event> CreateChannelInbox(
        EventChannel channel,
        EventInboxOptions? options = null);
}
