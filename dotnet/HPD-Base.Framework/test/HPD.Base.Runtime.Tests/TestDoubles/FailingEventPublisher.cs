using HPD.Base.Events;
using HPD.Base.Results;
using HPD.Base.Runtime.Results;

namespace HPD.Base.Runtime.Tests;

internal sealed class FailingEventPublisher : IBaseEventPublisher
{
    public ValueTask<OperationResult<EventPublishResult>> PublishAsync(
        BaseEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = envelope;
        return ValueTask.FromResult(OperationResults.StoreError<EventPublishResult>(new BaseError
        {
            Code = "publisher.failed",
            Message = "Publisher failed.",
            Category = ErrorCategory.Store
        }));
    }
}
