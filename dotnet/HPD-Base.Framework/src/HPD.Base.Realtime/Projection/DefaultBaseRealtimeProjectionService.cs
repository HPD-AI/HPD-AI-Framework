using HPD.Base.Events;
using HPD.Base.Dependencies;
using HPD.Base.Records;
using HPD.Base.Runtime.Policy;
using HPD.Base.Realtime.Policy;

namespace HPD.Base.Realtime.Projection;

internal sealed class DefaultBaseRealtimeProjectionService : IBaseRealtimeProjectionService
{
    private readonly IBaseRealtimePolicy _policy;
    private readonly IBaseRecordRedactor _redactor;
    private readonly IBaseDependencyInvalidationMapper? _invalidations;

    public DefaultBaseRealtimeProjectionService(
        IBaseRealtimePolicy policy,
        IBaseRecordRedactor redactor,
        IBaseDependencyInvalidationMapper? invalidations = null)
    {
        _policy = policy;
        _redactor = redactor;
        _invalidations = invalidations;
    }

    public async ValueTask<BaseRealtimeEvent?> ProjectAsync(
        BaseRealtimeProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var decision = await _policy.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!decision.Allow || decision.Policy is null || decision.Collection is null)
            return null;

        var collectionId = request.Event.Resource.CollectionId;
        var recordId = request.Event.Resource.RecordId;
        if (string.IsNullOrWhiteSpace(collectionId) || recordId is null)
            return null;

        BaseDependencyInvalidation? invalidation = null;
        if (_invalidations is not null)
        {
            try
            {
                invalidation = await _invalidations
                    .MapAsync(request.Event, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (BaseDependencyInvalidationException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new BaseDependencyInvalidationException(
                    "Dependency invalidation mapping failed.");
            }
        }

        return new BaseRealtimeEvent
        {
            EventId = request.Event.EventId,
            Type = request.Event.Type,
            SchemaVersion = request.Event.SchemaVersion,
            OccurredAt = request.Event.Timestamp,
            Resource = new BaseRealtimeRecordResource
            {
                CollectionId = collectionId,
                RecordId = recordId.Value
            },
            Operation = request.Event.Operation,
            Before = decision.IncludeBefore ? Redact(request.Event.Before, decision) : null,
            After = decision.IncludeAfter ? Redact(request.Event.After, decision) : null,
            Invalidation = invalidation
        };
    }

    private BaseRealtimeRecordSnapshot? Redact(RecordSnapshot? snapshot, BaseRealtimeEventProjectionDecision decision)
    {
        if (snapshot?.Payload is null || snapshot.Metadata is null || decision.Collection is null || decision.Policy is null)
            return null;

        var redacted = _redactor.RedactRecord(new RecordEnvelope
        {
            CollectionId = snapshot.CollectionId,
            Id = snapshot.Id,
            Payload = snapshot.Payload,
            Metadata = snapshot.Metadata
        }, decision.Collection, decision.Policy, decision.View);

        return new BaseRealtimeRecordSnapshot
        {
            Payload = redacted.Payload
        };
    }
}
