namespace HPD.Events;

/// <summary>
/// Composed class-event bus surface for code that truly needs publishing, observers, and owned inboxes.
/// </summary>
public interface IEventBus : IEventPublisher, IEventObserverBus, IEventInboxSource
{
    /// <summary>
    /// Registry for interruptible event flows.
    /// </summary>
    IEventFlowRegistry EventFlows { get; }

    /// <summary>
    /// Return current class-event bus health.
    /// </summary>
    EventBusStats GetStats();
}
