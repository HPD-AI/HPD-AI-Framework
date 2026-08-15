using HPD.Payments.Persistence.Ports;

namespace HPD.Payments.Runtime.CompositionContracts;

/// <summary>Holds the complete closed set of seventeen authority persistence ports for profile composition.</summary>
/// <remarks>Construction is explicit and reflection-free. This object performs no runtime discovery and grants no authority.</remarks>
public sealed record RuntimePersistencePorts
{
    /// <summary>Gets the Scoped Identity port.</summary>
    public IScopedIdentityPersistencePort ScopedIdentity { get; }
    /// <summary>Gets the Agreement port.</summary>
    public IAgreementPersistencePort Agreement { get; }
    /// <summary>Gets the Requested Transition port.</summary>
    public IRequestedTransitionPersistencePort RequestedTransition { get; }
    /// <summary>Gets the Effective Commercial Fact port.</summary>
    public IEffectiveCommercialFactPersistencePort EffectiveCommercialFact { get; }
    /// <summary>Gets the Measured Fact port.</summary>
    public IMeasuredFactPersistencePort MeasuredFact { get; }
    /// <summary>Gets the Measurement Generation port.</summary>
    public IMeasurementGenerationPersistencePort MeasurementGeneration { get; }
    /// <summary>Gets the Valuation port.</summary>
    public IValuationPersistencePort Valuation { get; }
    /// <summary>Gets the Obligation port.</summary>
    public IObligationPersistencePort Obligation { get; }
    /// <summary>Gets the Issuance Fact port.</summary>
    public IIssuanceFactPersistencePort IssuanceFact { get; }
    /// <summary>Gets the Held Position port.</summary>
    public IHeldPositionPersistencePort HeldPosition { get; }
    /// <summary>Gets the Value Movement port.</summary>
    public IValueMovementPersistencePort ValueMovement { get; }
    /// <summary>Gets the Entitlement port.</summary>
    public IEntitlementPersistencePort Entitlement { get; }
    /// <summary>Gets the Restriction port.</summary>
    public IRestrictionPersistencePort Restriction { get; }
    /// <summary>Gets the Capability Evidence port.</summary>
    public ICapabilityEvidencePersistencePort CapabilityEvidence { get; }
    /// <summary>Gets the External Effect port.</summary>
    public IExternalEffectPersistencePort ExternalEffect { get; }
    /// <summary>Gets the Work Requirement port.</summary>
    public IWorkRequirementPersistencePort WorkRequirement { get; }
    /// <summary>Gets the Publication Obligation port.</summary>
    public IPublicationObligationPersistencePort PublicationObligation { get; }

    /// <summary>Creates a complete registration; every closed authority port is mandatory.</summary>
    public RuntimePersistencePorts(
        IScopedIdentityPersistencePort scopedIdentity, IAgreementPersistencePort agreement,
        IRequestedTransitionPersistencePort requestedTransition, IEffectiveCommercialFactPersistencePort effectiveCommercialFact,
        IMeasuredFactPersistencePort measuredFact, IMeasurementGenerationPersistencePort measurementGeneration,
        IValuationPersistencePort valuation, IObligationPersistencePort obligation, IIssuanceFactPersistencePort issuanceFact,
        IHeldPositionPersistencePort heldPosition, IValueMovementPersistencePort valueMovement, IEntitlementPersistencePort entitlement,
        IRestrictionPersistencePort restriction, ICapabilityEvidencePersistencePort capabilityEvidence,
        IExternalEffectPersistencePort externalEffect, IWorkRequirementPersistencePort workRequirement,
        IPublicationObligationPersistencePort publicationObligation)
    {
        ScopedIdentity = scopedIdentity ?? throw new ArgumentNullException(nameof(scopedIdentity));
        Agreement = agreement ?? throw new ArgumentNullException(nameof(agreement));
        RequestedTransition = requestedTransition ?? throw new ArgumentNullException(nameof(requestedTransition));
        EffectiveCommercialFact = effectiveCommercialFact ?? throw new ArgumentNullException(nameof(effectiveCommercialFact));
        MeasuredFact = measuredFact ?? throw new ArgumentNullException(nameof(measuredFact));
        MeasurementGeneration = measurementGeneration ?? throw new ArgumentNullException(nameof(measurementGeneration));
        Valuation = valuation ?? throw new ArgumentNullException(nameof(valuation));
        Obligation = obligation ?? throw new ArgumentNullException(nameof(obligation));
        IssuanceFact = issuanceFact ?? throw new ArgumentNullException(nameof(issuanceFact));
        HeldPosition = heldPosition ?? throw new ArgumentNullException(nameof(heldPosition));
        ValueMovement = valueMovement ?? throw new ArgumentNullException(nameof(valueMovement));
        Entitlement = entitlement ?? throw new ArgumentNullException(nameof(entitlement));
        Restriction = restriction ?? throw new ArgumentNullException(nameof(restriction));
        CapabilityEvidence = capabilityEvidence ?? throw new ArgumentNullException(nameof(capabilityEvidence));
        ExternalEffect = externalEffect ?? throw new ArgumentNullException(nameof(externalEffect));
        WorkRequirement = workRequirement ?? throw new ArgumentNullException(nameof(workRequirement));
        PublicationObligation = publicationObligation ?? throw new ArgumentNullException(nameof(publicationObligation));
    }
}
