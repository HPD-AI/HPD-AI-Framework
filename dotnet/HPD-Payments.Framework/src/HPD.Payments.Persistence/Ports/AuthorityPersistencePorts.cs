namespace HPD.Payments.Persistence.Ports;

/// <summary>Closed owner port for Scoped Identity reservation facts.</summary>
public interface IScopedIdentityPersistencePort : IOwnerPersistencePort<Contracts.ScopedIdentity.ScopedIdentityReservation> { }
/// <summary>Closed owner port for Agreement facts.</summary>
public interface IAgreementPersistencePort : IOwnerPersistencePort<Contracts.Agreement.AcceptedAgreementFact> { }
/// <summary>Closed owner port for Requested Transition facts.</summary>
public interface IRequestedTransitionPersistencePort : IOwnerPersistencePort<Contracts.RequestedTransition.RequestedTransitionFact> { }
/// <summary>Closed owner port for Effective Commercial facts.</summary>
public interface IEffectiveCommercialFactPersistencePort : IOwnerPersistencePort<Contracts.EffectiveCommercialFact.EffectiveCommercialFactRecord> { }
/// <summary>Closed owner port for Measured facts.</summary>
public interface IMeasuredFactPersistencePort : IOwnerPersistencePort<Contracts.MeasuredFact.MeasuredFactRecord> { }
/// <summary>Closed owner port for Measurement Generation facts.</summary>
public interface IMeasurementGenerationPersistencePort : IOwnerPersistencePort<Contracts.MeasurementGeneration.MeasurementGenerationFact> { }
/// <summary>Closed owner port for Valuation facts.</summary>
public interface IValuationPersistencePort : IOwnerPersistencePort<Contracts.Valuation.ValuationFact> { }
/// <summary>Closed owner port for Obligation facts.</summary>
public interface IObligationPersistencePort : IOwnerPersistencePort<Contracts.Obligation.ObligationFact> { }
/// <summary>Closed owner port for Issuance facts.</summary>
public interface IIssuanceFactPersistencePort : IOwnerPersistencePort<Contracts.IssuanceFact.IssuanceFactRecord> { }
/// <summary>Closed owner port for Held Position facts.</summary>
public interface IHeldPositionPersistencePort : IOwnerPersistencePort<Contracts.HeldPosition.HeldPositionFact> { }
/// <summary>Closed owner port for Value Movement facts.</summary>
public interface IValueMovementPersistencePort : IOwnerPersistencePort<Contracts.ValueMovement.ValueMovementFact> { }
/// <summary>Closed owner port for Entitlement Grant/Removal facts.</summary>
public interface IEntitlementPersistencePort : IOwnerPersistencePort<Contracts.EntitlementGrantRemovalFact.EntitlementFact> { }
/// <summary>Closed owner port for Restriction facts.</summary>
public interface IRestrictionPersistencePort : IOwnerPersistencePort<Contracts.RestrictionFact.RestrictionFactRecord> { }
/// <summary>Closed owner port for Capability Evidence facts.</summary>
public interface ICapabilityEvidencePersistencePort : IOwnerPersistencePort<Contracts.CapabilityEvidence.CapabilityEvidenceFact> { }
/// <summary>Closed owner port for External Effect facts.</summary>
public interface IExternalEffectPersistencePort : IOwnerPersistencePort<Contracts.ExternalEffect.ExternalEffectFact> { }
/// <summary>Closed owner port for Work Requirement facts.</summary>
public interface IWorkRequirementPersistencePort : IOwnerPersistencePort<Contracts.WorkRequirement.WorkRequirementFact> { }
/// <summary>Closed owner port for Publication Obligation facts.</summary>
public interface IPublicationObligationPersistencePort : IOwnerPersistencePort<Contracts.PublicationObligation.PublicationObligationFact> { }
