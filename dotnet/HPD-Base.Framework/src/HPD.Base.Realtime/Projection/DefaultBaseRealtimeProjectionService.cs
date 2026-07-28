using HPD.Base.Events;
using HPD.Base.Records;
using HPD.Base.Runtime.Policy;
using HPD.Base.Realtime.Policy;

namespace HPD.Base.Realtime.Projection;

internal sealed class DefaultBaseRealtimeProjectionService : IBaseRealtimeProjectionService
{
    private readonly IBaseRealtimePolicy _policy;
    private readonly IBaseRecordRedactor _redactor;

    public DefaultBaseRealtimeProjectionService(
        IBaseRealtimePolicy policy,
        IBaseRecordRedactor redactor)
    {
        _policy = policy;
        _redactor = redactor;
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

        return new BaseRealtimeEvent
        {
            EventId = request.Event.EventId,
            Type = request.Event.Type,
            SchemaVersion = request.Event.SchemaVersion,
            OccurredAt = request.Event.Timestamp,
            TenantId = request.Event.TenantId,
            CorrelationId = request.Event.CorrelationId,
            CausationId = request.Event.CausationId,
            Resource = new BaseRealtimeRecordResource
            {
                Kind = request.Event.Resource.Kind,
                CollectionId = request.Event.Resource.CollectionId,
                RecordId = request.Event.Resource.RecordId,
                ResourcePath = request.Event.Resource.ResourcePath
            },
            Operation = request.Event.Operation,
            ChangedFields = request.Event.ChangedFields,
            Before = decision.IncludeBefore ? Redact(request.Event.Before, decision) : null,
            After = decision.IncludeAfter ? Redact(request.Event.After, decision) : null,
            Visibility = decision.View,
            Principal = decision.IncludePrincipal ? Principal(request.Event.Principal) : null,
            Extensions = decision.IncludeExtensions ? request.Event.Extensions : null
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
            CollectionId = redacted.CollectionId,
            Id = redacted.Id,
            Payload = redacted.Payload,
            Metadata = redacted.Metadata,
            IncludedFields = snapshot.IncludedFields,
            Redacted = snapshot.Redacted || redacted.Policy?.Redacted == true
        };
    }

    private static BaseRealtimePrincipalSummary? Principal(EventPrincipalSummary? principal) =>
        principal is null
            ? null
            : new BaseRealtimePrincipalSummary
            {
                AuthenticationState = principal.AuthenticationState,
                SubjectId = principal.SubjectId,
                SubjectKind = principal.SubjectKind,
                TenantId = principal.TenantId,
                AuthSource = principal.AuthSource,
                IsServicePrincipal = principal.IsServicePrincipal,
                IsAdmin = principal.IsAdmin
            };
}
