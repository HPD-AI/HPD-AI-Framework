using HPD.Base.Events;
using HPD.Base.Results;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.Observability.Logging;
using HPD.Base.Runtime.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HPD.Base.Runtime.Events;

internal sealed class DefaultBaseEventDispatcher : IBaseEventDispatcher
{
    private readonly IBaseEventPublisher _publisher;
    private readonly HPDBaseRuntimeEventOptions _options;
    private readonly ILogger<DefaultBaseEventDispatcher> _logger;

    public DefaultBaseEventDispatcher(
        IBaseEventPublisher publisher,
        IOptions<HPDBaseRuntimeOptions> options,
        ILogger<DefaultBaseEventDispatcher> logger)
    {
        _publisher = publisher;
        _options = options.Value.Events;
        _logger = logger;
    }

    public async ValueTask<OperationResult<EventReference[]>> DispatchMutationAsync(
        BaseEvent @event,
        EventDeliveryGuarantee committedGuarantee,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled || _options.PublishFailureMode == BaseEventPublishFailureMode.Disabled)
        {
            return OkWithoutReferences(Warning(
                "base.runtime.events.disabled",
                "Event publishing is disabled."));
        }

        var result = await _publisher.PublishAsync(@event, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null)
        {
            LogDispatchFailure(@event, "unexpected", "base.runtime.events.publishFailed");
            return OkWithoutReferences(Warning(
                "base.runtime.events.publishFailed",
                "Event publishing failed after the mutation committed."));
        }

        if (_options.PublishFailureMode == BaseEventPublishFailureMode.RequireEnqueue
            && committedGuarantee < EventDeliveryGuarantee.DurableEnqueued
            && result.Value.Guarantee == EventDeliveryGuarantee.BestEffort)
        {
            LogDispatchFailure(@event, "capability", "base.runtime.events.enqueueRequired");
            return OkWithoutReferences(Warning(
                "base.runtime.events.enqueueRequired",
                "Durable event enqueue is required but not available."));
        }

        return OperationResults.Ok(new[]
        {
            new EventReference
            {
                EventId = result.Value.EventId,
                Type = @event.Type,
                Stream = result.Value.Stream,
                Resource = @event is BaseRecordMutationEvent mutation
                    ? mutation.Resource.ResourcePath
                    : null,
                PublishedAt = result.Value.PublishedAt,
                Guarantee = result.Value.Guarantee
            }
        });
    }

    private static OperationResult<EventReference[]> OkWithoutReferences(OperationWarning warning) => new()
    {
        Status = OperationStatus.Ok,
        Value = [],
        Warnings = [warning]
    };

    private static OperationWarning Warning(string code, string message) => new()
    {
        Code = code,
        Message = message
    };

    private void LogDispatchFailure(BaseEvent @event, string errorCategory, string errorCode)
    {
        var operation = @event is BaseRecordMutationEvent mutation
            ? HPDBaseRuntimeLog.OperationKind(mutation.Operation)
            : "unknown";
        HPDBaseRuntimeLog.MutationEventDispatchFailed(
            _logger,
            operation,
            errorCategory,
            errorCode);
    }
}
