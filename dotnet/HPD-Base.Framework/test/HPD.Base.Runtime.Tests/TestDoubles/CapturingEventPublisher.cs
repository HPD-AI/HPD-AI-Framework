using HPD.Base.Events;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Tests;

internal sealed class CapturingEventPublisher : IBaseEventPublisher
{
    public BaseRecordMutationEvent? LastEvent { get; private set; }

    public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEvent @event,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (@event is not BaseRecordMutationEvent mutation)
        {
            throw new InvalidOperationException("Expected a BASE record mutation event.");
        }

        LastEvent = mutation;
        return ValueTask.FromResult(OperationResults.Ok(new EventPublishResult
        {
            EventId = mutation.EventId,
            Guarantee = EventDeliveryGuarantee.BestEffort
        }));
    }
}
