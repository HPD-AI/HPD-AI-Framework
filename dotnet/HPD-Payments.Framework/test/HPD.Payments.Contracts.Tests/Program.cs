using HPD.Payments.Contracts.Tests.Agreement;
using HPD.Payments.Contracts.Tests.CapabilityEvidence;
using HPD.Payments.Contracts.Tests.EntitlementGrantRemovalFact;
using HPD.Payments.Contracts.Tests.EffectiveCommercialFact;
using HPD.Payments.Contracts.Tests.ExternalEffect;
using HPD.Payments.Contracts.Tests.HeldPosition;
using HPD.Payments.Contracts.Tests.IssuanceFact;
using HPD.Payments.Contracts.Tests.MeasuredFact;
using HPD.Payments.Contracts.Tests.MeasurementGeneration;
using HPD.Payments.Contracts.Tests.Obligation;
using HPD.Payments.Contracts.Tests.PublicationObligation;
using HPD.Payments.Contracts.Tests.RestrictionFact;
using HPD.Payments.Contracts.Tests.RequestedTransition;
using HPD.Payments.Contracts.Tests.ScopedIdentity;
using HPD.Payments.Contracts.Tests.Valuation;
using HPD.Payments.Contracts.Tests.ValueMovement;
using HPD.Payments.Contracts.Tests.WorkRequirement;

var partitions = new (string Name, Action Run)[]
{
    ("ScopedIdentity", ScopedIdentityContractProofs.RunAll),
    ("Agreement", AgreementContractProofs.RunAll),
    ("RequestedTransition", RequestedTransitionContractProofs.RunAll),
    ("EffectiveCommercialFact", EffectiveCommercialFactContractProofs.RunAll),
    ("MeasuredFact", MeasuredFactContractTests.Run),
    ("MeasurementGeneration", MeasurementGenerationContractTests.Run),
    ("Valuation", ValuationContractTests.Run),
    ("Obligation", ObligationContractTests.Run),
    ("IssuanceFact", IssuanceContractTests.Run),
    ("EntitlementGrantRemovalFact", EntitlementContractTests.Run),
    ("RestrictionFact", RestrictionContractTests.Run),
    ("CapabilityEvidence", CapabilityEvidenceContractTests.Run),
    ("ExternalEffect", ExternalEffectContractTests.Run),
    ("WorkRequirement", WorkRequirementContractTests.Run),
    ("PublicationObligation", PublicationObligationContractTests.Run),
    ("HeldPosition", HeldPositionContractTests.Run),
    ("ValueMovement", ValueMovementContractTests.Run),
};

if (partitions.Length != 17 ||
    partitions.Select(static partition => partition.Name).Distinct(StringComparer.Ordinal).Count() != partitions.Length)
{
    throw new InvalidOperationException("Contracts test partition registration is missing or duplicated.");
}

foreach (var partition in partitions)
{
    partition.Run();
    Console.WriteLine($"PASS {partition.Name}");
}

Console.WriteLine($"Executed {partitions.Length} Contracts authority partitions exactly once.");
