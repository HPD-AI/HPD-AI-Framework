using HPD.Payments.Persistence.Ports;
using HPD.Payments.Runtime.Admission;
using HPD.Payments.Runtime.Authorization;

namespace HPD.Payments.Runtime.Orchestration;

/// <summary>Coordinates current-action authorization and one inward owner persistence port without owning authority semantics.</summary>
public sealed class OwnerFactOrchestrator<TAction, TFact> : ICurrentActionAdmission<TAction, TFact>
    where TAction : notnull where TFact : notnull
{
    private readonly ICurrentActionAuthorizer<TAction> _authorizer;
    private readonly IOwnerPersistencePort<TFact> _persistence;

    /// <summary>Creates a closed coordinator from explicitly supplied policy and persistence dependencies.</summary>
    public OwnerFactOrchestrator(ICurrentActionAuthorizer<TAction> authorizer, IOwnerPersistencePort<TFact> persistence)
    {
        _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    /// <inheritdoc />
    public async ValueTask<AdmissionReceipt<TFact>> AdmitAsync(AdmissionRequest<TAction, TFact> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var decision = await _authorizer.AuthorizeAsync(request.Authorization, cancellationToken).ConfigureAwait(false);
        if (decision.PolicyRevision != request.Authorization.PolicyRevision)
            return new(AdmissionDisposition.Indeterminate, decision, null);
        if (decision.Disposition == AuthorizationDisposition.Denied)
            return new(AdmissionDisposition.Denied, decision, null);
        if (decision.Disposition != AuthorizationDisposition.Authorized)
            return new(AdmissionDisposition.Indeterminate, decision, null);
        var persisted = await _persistence.CompareBindAppendAsync(request.Append, cancellationToken).ConfigureAwait(false);
        return new(AdmissionDisposition.Attempted, decision, persisted);
    }
}
