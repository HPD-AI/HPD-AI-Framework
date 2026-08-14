using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Supporting.Custody;

/// <summary>Names the scoped observation state of one custody instance generation.</summary>
public enum CustodyState
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>The named representation is known present.</summary>
    KnownPresent,
    /// <summary>Presence cannot currently be established.</summary>
    UnknownPresence,
    /// <summary>A hold applies to this instance generation.</summary>
    Held,
    /// <summary>Retention policy currently requires this instance.</summary>
    RetentionRequired,
    /// <summary>The instance is eligible for a requested disposition.</summary>
    Eligible,
    /// <summary>A disposition was requested but not verified.</summary>
    Requested,
    /// <summary>The named instance postcondition was freshly verified.</summary>
    VerifiedAbsent,
    /// <summary>Known or unverifiable residue remains.</summary>
    Residual,
}

/// <summary>Inventories one owned representation generation at one controller.</summary>
/// <remarks>The instance describes only its named copy; it never implies global deletion or changes source authority state.</remarks>
public sealed record CustodyInstance
{
    /// <summary>Gets the custody instance identity.</summary>
    public SemanticId InstanceId { get; }
    /// <summary>Gets the owner subject represented by this instance.</summary>
    public OwnerReference Subject { get; }
    /// <summary>Gets the controller or store identity responsible for the named copy.</summary>
    public SemanticId ControllerId { get; }
    /// <summary>Gets the instance inventory generation.</summary>
    public OwnerGeneration InventoryGeneration { get; }
    /// <summary>Gets the classification and retention mark.</summary>
    public ClassificationMark Classification { get; }
    /// <summary>Gets the retention-policy revision applied.</summary>
    public Revision PolicyRevision { get; }
    /// <summary>Gets the legal/business hold-set revision applied.</summary>
    public Revision HoldRevision { get; }
    /// <summary>Gets the scoped custody state.</summary>
    public CustodyState State { get; }
    /// <summary>Gets when the state was observed or verified.</summary>
    public NamedTime ObservedAt { get; }

    /// <summary>Creates one per-controller custody observation.</summary>
    /// <exception cref="ArgumentException">Identity, scope, revisions, classification, state, or time is invalid.</exception>
    public CustodyInstance(SemanticId instanceId, OwnerReference subject, SemanticId controllerId, OwnerGeneration inventoryGeneration,
        ClassificationMark classification, Revision policyRevision, Revision holdRevision, CustodyState state, NamedTime observedAt)
    {
        if (!instanceId.IsValid || !subject.IsValid || !controllerId.IsValid || instanceId.Scope != subject.SubjectId.Scope || controllerId.Scope != instanceId.Scope ||
            !inventoryGeneration.IsValid || !classification.IsValid || !policyRevision.IsValid || !holdRevision.IsValid || state == CustodyState.None || !Enum.IsDefined(state) ||
            !observedAt.IsValid || observedAt.Kind is not (TimeKind.Observed or TimeKind.Verify))
            throw new ArgumentException("Custody instance requires valid same-scope identity, revisions, state, and named observation.");
        InstanceId = instanceId; Subject = subject; ControllerId = controllerId; InventoryGeneration = inventoryGeneration;
        Classification = classification; PolicyRevision = policyRevision; HoldRevision = holdRevision; State = state; ObservedAt = observedAt;
    }
}

/// <summary>Declares the claim epoch that binds reusable external claims to exact revisions and expiry.</summary>
public sealed record ClaimEpoch
{
    /// <summary>Gets the claim identity.</summary>
    public SemanticId ClaimId { get; }
    /// <summary>Gets the capability or authority owner to which the claim is routed.</summary>
    public OwnerReference Owner { get; }
    /// <summary>Gets the provider, API, configuration, or certification revision.</summary>
    public Revision ClaimRevision { get; }
    /// <summary>Gets the evidence digest for this exact epoch.</summary>
    public CanonicalDigest EvidenceDigest { get; }
    /// <summary>Gets when the claim epoch expires.</summary>
    public NamedTime ExpiresAt { get; }

    /// <summary>Creates an expiring, revision-bound claim epoch.</summary>
    /// <exception cref="ArgumentException">Identity, scope, owner, revision, digest, or expiry is invalid.</exception>
    public ClaimEpoch(SemanticId claimId, OwnerReference owner, Revision claimRevision, CanonicalDigest evidenceDigest, NamedTime expiresAt)
    {
        ArgumentNullException.ThrowIfNull(evidenceDigest);
        if (!claimId.IsValid || !owner.IsValid || claimId.Scope != owner.SubjectId.Scope || !claimRevision.IsValid || !expiresAt.IsValid || expiresAt.Kind != TimeKind.Expiry)
            throw new ArgumentException("Claim epoch requires same-scope owner routing, revision, digest, and expiry.");
        ClaimId = claimId; Owner = owner; ClaimRevision = claimRevision; EvidenceDigest = evidenceDigest; ExpiresAt = expiresAt;
    }
}
