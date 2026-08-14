using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Contracts.ExternalEffect;

/// <summary>Names the local knowledge state of one irreversible external-effect attempt.</summary>
public enum ExternalEffectState
{
    /// <summary>Invalid default state.</summary>
    None = 0,
    /// <summary>The operation is recorded but no send boundary has been crossed.</summary>
    NotDispatched,
    /// <summary>Local execution began but has not yet produced evidence about the send boundary.</summary>
    Dispatching,
    /// <summary>The send boundary may have been crossed and occurrence remains unknown.</summary>
    PossibleDispatch,
    /// <summary>Admitted evidence establishes that the external effect occurred.</summary>
    ConfirmedOccurred,
    /// <summary>Admitted evidence establishes that the external effect did not occur.</summary>
    ConfirmedNotOccurred,
    /// <summary>A governed adjudication resolved conflicting external observations.</summary>
    Adjudicated,
}

/// <summary>Binds one semantic irreversible operation to one exact provider execution context.</summary>
public sealed record ExternalEffectOperation
{
    /// <summary>Gets the semantic operation identity shared across attempts and observations.</summary>
    public SemanticId OperationId { get; }
    /// <summary>Gets this immutable attempt identity.</summary>
    public SemanticId AttemptId { get; }
    /// <summary>Gets the externally scoped provider-account target.</summary>
    public SemanticId ProviderAccountId { get; }
    /// <summary>Gets the bounded idempotency token.</summary>
    public string IdempotencyKey { get; }
    /// <summary>Gets the digest of the exact request bytes and canonical profile.</summary>
    public CanonicalDigest RequestDigest { get; }
    /// <summary>Gets the credential generation pinned for this attempt.</summary>
    public Revision CredentialRevision { get; }
    /// <summary>Gets the configuration revision pinned for this attempt.</summary>
    public Revision ConfigurationRevision { get; }

    /// <summary>Creates an immutable operation-attempt binding.</summary>
    /// <exception cref="ArgumentException">Scope, provider target, token, or revision is invalid.</exception>
    public ExternalEffectOperation(SemanticId operationId, SemanticId attemptId, SemanticId providerAccountId,
        string idempotencyKey, CanonicalDigest requestDigest, Revision credentialRevision, Revision configurationRevision)
    {
        ArgumentNullException.ThrowIfNull(requestDigest);
        if (!operationId.IsValid || !attemptId.IsValid || !providerAccountId.IsValid || providerAccountId.Provider is null ||
            operationId.Scope != attemptId.Scope || attemptId.Scope != providerAccountId.Scope ||
            !ScopeId.TryCreate("token", "idempotency", idempotencyKey, out _) || !credentialRevision.IsValid || !configurationRevision.IsValid)
            throw new ArgumentException("External operation requires one scope, external target, bounded idempotency key, and current revisions.");
        OperationId = operationId; AttemptId = attemptId; ProviderAccountId = providerAccountId; IdempotencyKey = idempotencyKey;
        RequestDigest = requestDigest; CredentialRevision = credentialRevision; ConfigurationRevision = configurationRevision;
    }
}

/// <summary>Records one append-only observation about an external-effect attempt.</summary>
public sealed record ExternalEffectFact
{
    /// <summary>Gets the bound operation and attempt.</summary>
    public ExternalEffectOperation Operation { get; }
    /// <summary>Gets the newly observed knowledge state.</summary>
    public ExternalEffectState State { get; }
    /// <summary>Gets the exact predecessor fact digest, absent only for NotDispatched.</summary>
    public CanonicalDigest? PredecessorDigest { get; }
    /// <summary>Gets the digest of provider/local evidence supporting this fact.</summary>
    public CanonicalDigest EvidenceDigest { get; }
    /// <summary>Gets the named time appropriate to this state.</summary>
    public NamedTime ObservedAt { get; }
    /// <summary>Gets a bounded evidence or adjudication code.</summary>
    public string Code { get; }

    /// <summary>Creates one append-only external-effect fact without inferring external non-occurrence.</summary>
    /// <exception cref="ArgumentException">State, predecessor, time, or code is invalid.</exception>
    public ExternalEffectFact(ExternalEffectOperation operation, ExternalEffectState state, CanonicalDigest? predecessorDigest,
        CanonicalDigest evidenceDigest, NamedTime observedAt, string code)
    {
        ArgumentNullException.ThrowIfNull(operation); ArgumentNullException.ThrowIfNull(evidenceDigest);
        var initial = state == ExternalEffectState.NotDispatched;
        var expectedTime = state is ExternalEffectState.Dispatching or ExternalEffectState.PossibleDispatch ? TimeKind.Dispatch : TimeKind.Observed;
        if (state == ExternalEffectState.None || !Enum.IsDefined(state) || initial != (predecessorDigest is null) ||
            !observedAt.IsValid || observedAt.Kind != expectedTime || !ScopeId.TryCreate("token", "code", code, out _))
            throw new ArgumentException("External-effect fact requires explicit state, append lineage, matching named time, and bounded code.");
        Operation = operation; State = state; PredecessorDigest = predecessorDigest; EvidenceDigest = evidenceDigest; ObservedAt = observedAt; Code = code;
    }

    /// <summary>Gets whether another provider dispatch is safe from this local knowledge state.</summary>
    /// <remarks>Possible dispatch and confirmed occurrence always block retry or failover; policy cannot reinterpret uncertainty as failure.</remarks>
    public bool PermitsAnotherDispatch => State is ExternalEffectState.NotDispatched or ExternalEffectState.ConfirmedNotOccurred;
}
