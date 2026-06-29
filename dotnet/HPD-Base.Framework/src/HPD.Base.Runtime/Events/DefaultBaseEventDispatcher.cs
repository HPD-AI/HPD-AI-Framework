using HPD.Base.Events;
using HPD.Base.Results;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.Results;
using Microsoft.Extensions.Options;

namespace HPD.Base.Runtime.Events;

internal sealed class DefaultBaseEventDispatcher : IBaseEventDispatcher
{
    private readonly IBaseEventPublisher _publisher;
    private readonly HPDBaseRuntimeEventOptions _options;

    public DefaultBaseEventDispatcher(
        IBaseEventPublisher publisher,
        IOptions<HPDBaseRuntimeOptions> options)
    {
        _publisher = publisher;
        _options = options.Value.Events;
    }

    public async ValueTask<OperationResult<EventReference[]>> DispatchMutationAsync(
        BaseEventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled || _options.PublishFailureMode == BaseEventPublishFailureMode.Disabled)
        {
            return OkWithoutReferences(Warning(
                "base.runtime.events.disabled",
                "Event publishing is disabled."));
        }

        var result = await _publisher.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess() || result.Value is null)
        {
            return OkWithoutReferences(Warning(
                "base.runtime.events.publishFailed",
                "Event publishing failed after the mutation committed."));
        }

        if (_options.PublishFailureMode == BaseEventPublishFailureMode.RequireEnqueue
            && result.Value.Guarantee == EventDeliveryGuarantee.BestEffort)
        {
            return OkWithoutReferences(Warning(
                "base.runtime.events.enqueueRequired",
                "Durable event enqueue is required but not available."));
        }

        return OperationResults.Ok(new[]
        {
            new EventReference
            {
                EventId = result.Value.EventId,
                Type = envelope.Type,
                Stream = result.Value.Stream,
                Resource = envelope.Resource.ResourcePath,
                PublishedAt = result.Value.PublishedAt
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
}
