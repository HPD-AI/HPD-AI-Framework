using HPD.Base.Events;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Events;

public sealed class NoOpBaseEventPublisher : IBaseEventPublisher
{
    public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);

        return ValueTask.FromResult(OperationResults.Ok(new EventPublishResult
        {
            EventId = envelope.EventId,
            Guarantee = EventDeliveryGuarantee.BestEffort
        }));
    }
}
