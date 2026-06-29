using HPD.Base.Events;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Tests;

internal sealed class CapturingEventPublisher : IBaseEventPublisher
{
    public BaseEventEnvelope? LastEnvelope { get; private set; }

    public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastEnvelope = envelope;
        return ValueTask.FromResult(OperationResults.Ok(new EventPublishResult
        {
            EventId = envelope.EventId,
            Guarantee = EventDeliveryGuarantee.BestEffort
        }));
    }
}
