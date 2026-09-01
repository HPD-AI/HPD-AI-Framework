using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Contracts.EntitlementGrantRemovalFact.QuotaPolicy;

/// <summary>Describes a freshness-aware quota entitlement decision.</summary>
public enum QuotaEligibilityKind
{
    /// <summary>No valid decision.</summary>
    None = 0,
    /// <summary>Fresh evidence permits quota admission to continue.</summary>
    Eligible = 1,
    /// <summary>Fresh negative or stale evidence fails closed.</summary>
    Rejected = 2,
    /// <summary>Conflicting or unverifiable evidence prevents a decision.</summary>
    Indeterminate = 3,
}

/// <summary>Binds quota eligibility to exact entitlement provenance and policy validity.</summary>
public sealed record QuotaEntitlementEvidence
{
    /// <summary>Gets the quota subject.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the immutable entitlement fact.</summary>
    public SemanticId EntitlementFactId { get; }
    /// <summary>Gets the entitlement fact digest.</summary>
    public CanonicalDigest EntitlementDigest { get; }
    /// <summary>Gets the bounded feature code.</summary>
    public string FeatureCode { get; }
    /// <summary>Gets the indivisible non-currency unit.</summary>
    public string UnitCode { get; }
    /// <summary>Gets the exact policy revision.</summary>
    public Revision PolicyRevision { get; }
    /// <summary>Gets the inclusive evidence validity start.</summary>
    public DateTimeOffset ValidFrom { get; }
    /// <summary>Gets the exclusive evidence validity end.</summary>
    public DateTimeOffset ValidUntil { get; }

    /// <summary>Creates exact quota entitlement evidence.</summary>
    public QuotaEntitlementEvidence(SemanticId subjectId, SemanticId entitlementFactId, CanonicalDigest entitlementDigest,
        string featureCode, string unitCode, Revision policyRevision, DateTimeOffset validFrom, DateTimeOffset validUntil)
    {
        if (!subjectId.IsValid || !entitlementFactId.IsValid || subjectId.Scope != entitlementFactId.Scope ||
            entitlementDigest is null || !ScopeId.TryCreate("quota", "feature", featureCode, out _) ||
            unitCode is null || !ScopeId.TryCreate("quota", "unit", unitCode, out _) || unitCode.Length == 3 && unitCode.All(char.IsUpper) ||
            !policyRevision.IsValid || policyRevision.Kind != "policy" || validUntil <= validFrom)
            throw new ArgumentException("Quota entitlement evidence is invalid.");
        SubjectId = subjectId; EntitlementFactId = entitlementFactId; EntitlementDigest = entitlementDigest;
        FeatureCode = featureCode; UnitCode = unitCode; PolicyRevision = policyRevision; ValidFrom = validFrom; ValidUntil = validUntil;
    }

    /// <summary>Evaluates freshness without treating cached existence as authority.</summary>
    public QuotaEligibilityKind Evaluate(DateTimeOffset at, bool entitlementGranted, bool evidenceConflicted = false) =>
        evidenceConflicted ? QuotaEligibilityKind.Indeterminate :
        at < ValidFrom || at >= ValidUntil || !entitlementGranted ? QuotaEligibilityKind.Rejected : QuotaEligibilityKind.Eligible;
}
