using HPD.Base.Events;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;
using HPD.Events;

namespace HPD.Base.Runtime.Events;

/// <summary>
/// BASE event publisher backed by the HPD.Events event spine.
/// </summary>
public sealed class HPDEventsBaseEventPublisher : IBaseEventPublisher
{
    private readonly IEventPublisher _events;

    /// <summary>
    /// Creates a publisher that emits BASE events through HPD.Events.
    /// </summary>
    public HPDEventsBaseEventPublisher(IEventPublisher events)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEvent @event,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(@event);

        await _events.EmitAsync(@event, cancellationToken).ConfigureAwait(false);

        return OperationResults.Ok(new EventPublishResult
        {
            EventId = @event.EventId,
            PublishedAt = @event.Timestamp,
            Guarantee = EventDeliveryGuarantee.BestEffort
        });
    }
}
