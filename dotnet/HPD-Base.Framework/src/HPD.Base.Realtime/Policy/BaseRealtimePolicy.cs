using HPD.Base.Events;
using HPD.Base.Policy;
using HPD.Base.Records;
using HPD.Base.Realtime.Projection;
using HPD.Base.Runtime;
using HPD.Base.Runtime.Policy;
using HPD.Base.Runtime.Schema;
using HPD.Base.Schema;

namespace HPD.Base.Realtime.Policy;

public sealed record BaseRealtimeEventProjectionDecision
{
    public bool Allow { get; init; }
    public VisibilityLevel View { get; init; }
    public bool IncludeBefore { get; init; }
    public bool IncludeAfter { get; init; }
    public bool IncludePrincipal { get; init; }
    public bool IncludeExtensions { get; init; }
    public BasePolicyEvaluation? Policy { get; init; }
    public CollectionDefinition? Collection { get; init; }
}

public interface IBaseRealtimePolicy
{
    ValueTask<BaseRealtimeEventProjectionDecision> EvaluateAsync(
        BaseRealtimeProjectionRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class DefaultBaseRealtimePolicy : IBaseRealtimePolicy
{
    private readonly IBaseSchemaProvider _schema;
    private readonly IBasePolicyOrchestrator _policies;

    public DefaultBaseRealtimePolicy(
        IBaseSchemaProvider schema,
        IBasePolicyOrchestrator policies)
    {
        _schema = schema;
        _policies = policies;
    }

    public async ValueTask<BaseRealtimeEventProjectionDecision> EvaluateAsync(
        BaseRealtimeProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (!TenantAllowed(request.Principal, request.Event.TenantId, request.Join.TenantId))
            return new BaseRealtimeEventProjectionDecision();

        var view = request.Join.Visibility ?? VisibilityLevel.Public;
        if (request.Principal.AuthenticationState is PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System)
            view = request.Join.Visibility ?? VisibilityLevel.Admin;

        var collectionId = request.Event.Resource.CollectionId;
        if (string.IsNullOrWhiteSpace(collectionId))
            return new BaseRealtimeEventProjectionDecision();

        var collectionResult = await _schema.GetCollectionAsync(
            collectionId,
            request.Principal,
            request.Operation,
            view,
            cancellationToken).ConfigureAwait(false);
        if (collectionResult.Value is null)
            return new BaseRealtimeEventProjectionDecision();

        var existing = ToEnvelope(request.Event.After ?? request.Event.Before);
        var policy = await _policies.EvaluateReadAsync(new BasePolicyRequest
        {
            Principal = request.Principal,
            Operation = request.Operation,
            Collection = collectionResult.Value,
            ResourceKind = PolicyResourceKind.Record,
            ExistingRecord = existing,
            RecordId = request.Event.Resource.RecordId
        }, cancellationToken).ConfigureAwait(false);
        if (policy.Value is null)
            return new BaseRealtimeEventProjectionDecision();

        return new BaseRealtimeEventProjectionDecision
        {
            Allow = true,
            View = view,
            IncludeAfter = request.Join.IncludeSnapshots,
            IncludeBefore = request.Join.IncludeSnapshots
                && request.Join.IncludeBefore
                && request.Principal.AuthenticationState is PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System,
            IncludePrincipal = request.Join.IncludePrincipal,
            IncludeExtensions = request.Join.IncludeExtensions,
            Policy = policy.Value,
            Collection = collectionResult.Value
        };
    }

    private static bool TenantAllowed(PrincipalContext principal, string? eventTenantId, string? requestedTenantId)
    {
        if (!string.IsNullOrWhiteSpace(requestedTenantId)
            && !string.Equals(eventTenantId, requestedTenantId, StringComparison.Ordinal))
            return false;

        if (string.IsNullOrWhiteSpace(eventTenantId)
            || principal.AuthenticationState is PrincipalAuthenticationState.Admin or PrincipalAuthenticationState.System)
            return true;

        if (string.Equals(principal.CurrentTenantId, eventTenantId, StringComparison.Ordinal))
            return true;

        return principal.TenantMemberships?.Any(membership => string.Equals(membership.TenantId, eventTenantId, StringComparison.Ordinal)) == true;
    }

    private static RecordEnvelope? ToEnvelope(RecordSnapshot? snapshot) =>
        snapshot?.Payload is null || snapshot.Metadata is null
            ? null
            : new RecordEnvelope
            {
                CollectionId = snapshot.CollectionId,
                Id = snapshot.Id,
                Payload = snapshot.Payload,
                Metadata = snapshot.Metadata
            };
}
