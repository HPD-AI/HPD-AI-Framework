using HPD.Base;

namespace HPD.Base.Tests;

internal sealed class FailingEventPublisher : IBaseEventPublisher
{
    public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEvent @event,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = @event;
        return ValueTask.FromResult(OperationResults.StoreError<EventPublishResult>(new BaseError
        {
            Code = "publisher.failed",
            Message = "Publisher failed.",
            Category = ErrorCategory.Store
        }));
    }
}
