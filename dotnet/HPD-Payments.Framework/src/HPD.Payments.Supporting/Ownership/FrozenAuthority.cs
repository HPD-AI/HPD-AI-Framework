namespace HPD.Payments.Supporting.Ownership;

/// <summary>Names one of the seventeen exclusive authorities to which a supporting declaration must route.</summary>
/// <remarks>The default value is invalid. This vocabulary describes ownership; it grants no mutation capability.</remarks>
public enum FrozenAuthority
{
    /// <summary>Invalid default.</summary>
    None = 0,
    /// <summary>Scoped Identity authority.</summary>
    ScopedIdentity,
    /// <summary>Agreement authority.</summary>
    Agreement,
    /// <summary>Requested Transition authority.</summary>
    RequestedTransition,
    /// <summary>Effective Commercial Fact authority.</summary>
    EffectiveCommercialFact,
    /// <summary>Measured Fact authority.</summary>
    MeasuredFact,
    /// <summary>Measurement Generation authority.</summary>
    MeasurementGeneration,
    /// <summary>Valuation authority.</summary>
    Valuation,
    /// <summary>Obligation authority.</summary>
    Obligation,
    /// <summary>Issuance Fact authority.</summary>
    IssuanceFact,
    /// <summary>Held Position authority.</summary>
    HeldPosition,
    /// <summary>Value Movement authority.</summary>
    ValueMovement,
    /// <summary>Entitlement Grant/Removal Fact authority.</summary>
    EntitlementGrantRemovalFact,
    /// <summary>Restriction Fact authority.</summary>
    RestrictionFact,
    /// <summary>Capability Evidence authority.</summary>
    CapabilityEvidence,
    /// <summary>External Effect authority.</summary>
    ExternalEffect,
    /// <summary>Work Requirement authority.</summary>
    WorkRequirement,
    /// <summary>Publication Obligation authority.</summary>
    PublicationObligation,
}

/// <summary>Binds a supporting subject to its frozen authority owner and exact owner generation.</summary>
/// <remarks>This is a routing declaration, not an authority guard or persistence compare-and-bind operation.</remarks>
public readonly record struct OwnerReference
{
    /// <summary>Gets the exclusive authority responsible for mutation truth.</summary>
    public FrozenAuthority Authority { get; }
    /// <summary>Gets the authority-local semantic subject.</summary>
    public HPD.Payments.Primitives.Identity.SemanticId SubjectId { get; }
    /// <summary>Gets the exact authority generation observed by the supporting declaration.</summary>
    public HPD.Payments.Primitives.Identity.OwnerGeneration Generation { get; }
    /// <summary>Gets whether every component is valid and the authority is defined.</summary>
    public bool IsValid => Authority != FrozenAuthority.None && Enum.IsDefined(Authority) && SubjectId.IsValid && Generation.IsValid;

    /// <summary>Creates an immutable owner reference.</summary>
    /// <param name="authority">One frozen exclusive authority.</param>
    /// <param name="subjectId">The authority-local semantic subject.</param>
    /// <param name="generation">The exact observed owner generation.</param>
    /// <exception cref="ArgumentException">Any component is invalid.</exception>
    public OwnerReference(FrozenAuthority authority, HPD.Payments.Primitives.Identity.SemanticId subjectId, HPD.Payments.Primitives.Identity.OwnerGeneration generation)
    {
        if (authority == FrozenAuthority.None || !Enum.IsDefined(authority) || !subjectId.IsValid || !generation.IsValid)
            throw new ArgumentException("A valid frozen authority, subject, and owner generation are required.");
        Authority = authority;
        SubjectId = subjectId;
        Generation = generation;
    }
}
