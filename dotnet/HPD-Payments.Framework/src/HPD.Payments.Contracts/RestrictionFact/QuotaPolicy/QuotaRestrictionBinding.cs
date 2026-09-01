using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Contracts.RestrictionFact.QuotaPolicy;

/// <summary>Binds one quota restriction to its exclusive owner, dimension and policy revision.</summary>
public sealed record QuotaRestrictionBinding
{
    /// <summary>Gets the restricted subject.</summary>
    public SemanticId SubjectId { get; }
    /// <summary>Gets the restriction owner that alone may release it.</summary>
    public SemanticId RestrictionOwnerId { get; }
    /// <summary>Gets the restriction fact provenance.</summary>
    public SemanticId RestrictionFactId { get; }
    /// <summary>Gets the quota feature.</summary>
    public string FeatureCode { get; }
    /// <summary>Gets the quota unit.</summary>
    public string UnitCode { get; }
    /// <summary>Gets the pinned policy revision.</summary>
    public Revision PolicyRevision { get; }
    /// <summary>Gets the exact restriction-owner generation.</summary>
    public OwnerGeneration OwnerGeneration { get; }

    /// <summary>Creates one exact quota restriction binding.</summary>
    public QuotaRestrictionBinding(SemanticId subjectId, SemanticId restrictionOwnerId, SemanticId restrictionFactId,
        string featureCode, string unitCode, Revision policyRevision, OwnerGeneration ownerGeneration)
    {
        bool sameScope = subjectId.IsValid && restrictionOwnerId.IsValid && restrictionFactId.IsValid &&
            subjectId.Scope == restrictionOwnerId.Scope && subjectId.Scope == restrictionFactId.Scope;
        if (!sameScope || !ScopeId.TryCreate("quota", "feature", featureCode, out _) ||
            !ScopeId.TryCreate("quota", "unit", unitCode, out _) || !policyRevision.IsValid ||
            policyRevision.Kind != "policy" || !ownerGeneration.IsValid)
            throw new ArgumentException("Quota restriction binding is invalid.");
        SubjectId = subjectId; RestrictionOwnerId = restrictionOwnerId; RestrictionFactId = restrictionFactId;
        FeatureCode = featureCode; UnitCode = unitCode; PolicyRevision = policyRevision; OwnerGeneration = ownerGeneration;
    }

    /// <summary>Returns true only when the exact restriction owner and generation authorize release.</summary>
    public bool CanRelease(SemanticId ownerId, OwnerGeneration expectedGeneration) =>
        ownerId == RestrictionOwnerId && expectedGeneration == OwnerGeneration;
}
