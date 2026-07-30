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
    private readonly IBaseCommittedMutationObserver[] _observers;

    public DefaultBaseEventDispatcher(
        IBaseEventPublisher publisher,
        IOptions<HPDBaseRuntimeOptions> options,
        ILogger<DefaultBaseEventDispatcher> logger,
        IEnumerable<IBaseCommittedMutationObserver> observers)
    {
        _publisher = publisher;
        _options = options.Value.Events;
        _logger = logger;
        _observers = observers.ToArray();
    }

    public async ValueTask<OperationResult<EventReference[]>> DispatchMutationAsync(
        BaseEvent @event,
        EventDeliveryGuarantee committedGuarantee,
        CancellationToken cancellationToken = default)
    {
        var observerWarning = @event is BaseRecordMutationEvent mutation
            ? await NotifyObserversAsync(mutation).ConfigureAwait(false)
            : null;
        if (!_options.Enabled || _options.PublishFailureMode == BaseEventPublishFailureMode.Disabled)
        {
            return OkWithoutReferences(
                observerWarning,
                Warning("base.runtime.events.disabled", "Event publishing is disabled."));
        }

        using var publishLifetime = new CancellationTokenSource(_options.PostCommitWorkTimeout);
        OperationResult<EventPublishResult> result;
        try
        {
            result = await _publisher.PublishAsync(@event, publishLifetime.Token)
                .AsTask()
                .WaitAsync(publishLifetime.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogDispatchFailure(@event, "timeout", "base.runtime.events.publishFailed");
            return OkWithoutReferences(
                observerWarning,
                Warning(
                    "base.runtime.events.publishFailed",
                    "Event publishing failed after the mutation committed."));
        }
        if (!result.IsSuccess() || result.Value is null)
        {
            LogDispatchFailure(@event, "unexpected", "base.runtime.events.publishFailed");
            return OkWithoutReferences(
                observerWarning,
                Warning(
                    "base.runtime.events.publishFailed",
                    "Event publishing failed after the mutation committed."));
        }

        if (_options.PublishFailureMode == BaseEventPublishFailureMode.RequireEnqueue
            && committedGuarantee < EventDeliveryGuarantee.DurableEnqueued
            && result.Value.Guarantee == EventDeliveryGuarantee.BestEffort)
        {
            LogDispatchFailure(@event, "capability", "base.runtime.events.enqueueRequired");
            return OkWithoutReferences(
                observerWarning,
                Warning(
                    "base.runtime.events.enqueueRequired",
                    "Durable event enqueue is required but not available."));
        }

        return new OperationResult<EventReference[]>
        {
            Status = OperationStatus.Ok,
            Value =
            [
                new EventReference
                {
                    EventId = result.Value.EventId,
                    Type = @event.Type,
                    Stream = result.Value.Stream,
                    Resource = @event is BaseRecordMutationEvent mutationEvent
                        ? mutationEvent.Resource.ResourcePath
                        : null,
                    PublishedAt = result.Value.PublishedAt,
                    Guarantee = result.Value.Guarantee
                }
            ],
            Warnings = observerWarning is null ? null : [observerWarning]
        };
    }

    private async ValueTask<OperationWarning?> NotifyObserversAsync(
        BaseRecordMutationEvent mutation)
    {
        var failed = false;
        foreach (var observer in _observers)
        {
            using var observerLifetime = new CancellationTokenSource(_options.PostCommitWorkTimeout);
            try
            {
                await observer.ObserveAsync(mutation, observerLifetime.Token)
                    .AsTask()
                    .WaitAsync(observerLifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                LogDispatchFailure(mutation, "unexpected", "base.runtime.events.observerFailed");
                failed = true;
            }
        }

        return failed
            ? Warning(
                "base.runtime.events.observerFailed",
                "A committed mutation observer failed.")
            : null;
    }

    private static OperationResult<EventReference[]> OkWithoutReferences(
        params OperationWarning?[] warnings) => new()
    {
        Status = OperationStatus.Ok,
        Value = [],
        Warnings = warnings.Where(static warning => warning is not null).Cast<OperationWarning>().ToArray()
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
