
namespace HPD.Base;

/// <summary>Contains the internal authorization and redaction decision for one realtime event.</summary>
public sealed record BaseRealtimeEventProjectionDecision
{
    /// <summary>Gets whether the event may be projected to the subscriber.</summary>
    public bool Allow { get; init; }

    /// <summary>Gets the principal-derived visibility used for redaction.</summary>
    public VisibilityLevel View { get; init; }

    /// <summary>Gets whether the prior snapshot may be projected.</summary>
    public bool IncludeBefore { get; init; }

    /// <summary>Gets whether the resulting snapshot may be projected.</summary>
    public bool IncludeAfter { get; init; }

    /// <summary>Gets the effective record-read policy used for redaction.</summary>
    public BasePolicyEvaluation? Policy { get; init; }

    /// <summary>Gets the collection definition used for redaction.</summary>
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

        var view = request.Principal.AuthenticationState switch
        {
            PrincipalAuthenticationState.System => VisibilityLevel.Internal,
            PrincipalAuthenticationState.Admin => VisibilityLevel.Admin,
            _ => VisibilityLevel.Public,
        };

        if (request.Event.Visibility > view)
            return new BaseRealtimeEventProjectionDecision();

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
