using HPD.Base.Results;

namespace HPD.Base.Events;

public interface IBaseEventPublisher
{
    ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}

public interface IBaseEventSink
{
    ValueTask<OperationResult> HandleAsync(
        BaseEventEnvelope envelope,
        CancellationToken cancellationToken = default);
}
