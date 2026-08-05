
namespace HPD.Base;

/// <summary>
/// Publishes BASE domain events and reports BASE-specific publish results.
/// </summary>
public interface IBaseEventPublisher
{
    /// <summary>Publishes a BASE event.</summary>
    ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEvent @event,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles BASE domain events.
/// </summary>
public interface IBaseEventSink
{
    /// <summary>Handles a BASE event.</summary>
    ValueTask<OperationResult> HandleAsync(
        BaseEvent @event,
        CancellationToken cancellationToken = default);
}
