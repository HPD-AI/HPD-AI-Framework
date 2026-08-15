using HPD.Payments.Primitives.Classification;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Supporting.Evidence;

/// <summary>Describes the role of bounded evidence without converting it into domain truth.</summary>
public enum EvidenceRole
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Evidence supplied by a named source.</summary>
    Source,
    /// <summary>Evidence authorizing a current action.</summary>
    Authorization,
    /// <summary>Evidence verifying a question-scoped postcondition.</summary>
    Verification,
    /// <summary>Evidence supporting a capability or certification claim.</summary>
    Capability,
}

/// <summary>Describes owned, classified evidence and the authority-local subject it concerns.</summary>
/// <remarks>The descriptor contains no executable policy and does not itself authorize an action.</remarks>
public sealed record EvidenceDescriptor
{
    /// <summary>Gets the stable evidence identity.</summary>
    public SemanticId EvidenceId { get; }
    /// <summary>Gets the evidence role.</summary>
    public EvidenceRole Role { get; }
    /// <summary>Gets the authority owner and generation to which the evidence applies.</summary>
    public OwnerReference Subject { get; }
    /// <summary>Gets the immutable source identity.</summary>
    public SemanticId SourceId { get; }
    /// <summary>Gets the digest of the exact evidence bytes or manifest.</summary>
    public CanonicalDigest Digest { get; }
    /// <summary>Gets the evidence classification and retention mark.</summary>
    public ClassificationMark Classification { get; }
    /// <summary>Gets when the evidence was observed, verified, or issued.</summary>
    public NamedTime ObservedAt { get; }

    /// <summary>Creates an immutable evidence descriptor.</summary>
    /// <exception cref="ArgumentException">A component, scope, role, classification, or named time is invalid.</exception>
    public EvidenceDescriptor(SemanticId evidenceId, EvidenceRole role, OwnerReference subject, SemanticId sourceId,
        CanonicalDigest digest, ClassificationMark classification, NamedTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(digest);
        if (!evidenceId.IsValid || !subject.IsValid || !sourceId.IsValid || evidenceId.Scope != subject.SubjectId.Scope || sourceId.Scope != evidenceId.Scope ||
            role == EvidenceRole.None || !Enum.IsDefined(role) || !classification.IsValid || !observedAt.IsValid || observedAt.Kind is not (TimeKind.Observed or TimeKind.Verify or TimeKind.Issue))
            throw new ArgumentException("Evidence requires same-scope identities, closed role, classification, and an observed/verify/issue time.");
        EvidenceId = evidenceId; Role = role; Subject = subject; SourceId = sourceId; Digest = digest; Classification = classification; ObservedAt = observedAt;
    }
}

/// <summary>Records a current, action-scoped authorization decision as evidence rather than authority state.</summary>
public sealed record AuthorizationDescriptor
{
    /// <summary>Gets the authorization evidence.</summary>
    public EvidenceDescriptor Evidence { get; }
    /// <summary>Gets the bounded action token authorized or denied.</summary>
    public string Action { get; }
    /// <summary>Gets the policy revision used for the decision.</summary>
    public Revision PolicyRevision { get; }
    /// <summary>Gets the authorization expiry time.</summary>
    public NamedTime ExpiresAt { get; }

    /// <summary>Creates an action-, subject-, revision-, and expiry-bound authorization descriptor.</summary>
    /// <exception cref="ArgumentException">The evidence role, action, revision, or expiry is invalid.</exception>
    public AuthorizationDescriptor(EvidenceDescriptor evidence, string action, Revision policyRevision, NamedTime expiresAt)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Role != EvidenceRole.Authorization || !ScopeId.TryCreate("action", "action", action, out _) || !policyRevision.IsValid || !expiresAt.IsValid || expiresAt.Kind != TimeKind.Expiry)
            throw new ArgumentException("Authorization evidence must be action-, policy-, and expiry-bound.");
        Evidence = evidence; Action = action; PolicyRevision = policyRevision; ExpiresAt = expiresAt;
    }
}
