using System.Runtime.CompilerServices;
using HPD.Payments.Persistence.Ports;

namespace HPD.Payments.Persistence.Tests.Architecture;

internal static class PersistenceArchitectureConformance
{
    private static readonly Dictionary<Type, Type> ExpectedPortFacts = new()
    {
        [typeof(IScopedIdentityPersistencePort)] = typeof(Contracts.ScopedIdentity.ScopedIdentityReservation),
        [typeof(IAgreementPersistencePort)] = typeof(Contracts.Agreement.AcceptedAgreementFact),
        [typeof(IRequestedTransitionPersistencePort)] = typeof(Contracts.RequestedTransition.RequestedTransitionFact),
        [typeof(IEffectiveCommercialFactPersistencePort)] = typeof(Contracts.EffectiveCommercialFact.EffectiveCommercialFactRecord),
        [typeof(IMeasuredFactPersistencePort)] = typeof(Contracts.MeasuredFact.MeasuredFactRecord),
        [typeof(IMeasurementGenerationPersistencePort)] = typeof(Contracts.MeasurementGeneration.MeasurementGenerationFact),
        [typeof(IValuationPersistencePort)] = typeof(Contracts.Valuation.ValuationFact),
        [typeof(IObligationPersistencePort)] = typeof(Contracts.Obligation.ObligationFact),
        [typeof(IIssuanceFactPersistencePort)] = typeof(Contracts.IssuanceFact.IssuanceFactRecord),
        [typeof(IHeldPositionPersistencePort)] = typeof(Contracts.HeldPosition.HeldPositionFact),
        [typeof(IValueMovementPersistencePort)] = typeof(Contracts.ValueMovement.ValueMovementFact),
        [typeof(IEntitlementPersistencePort)] = typeof(Contracts.EntitlementGrantRemovalFact.EntitlementFact),
        [typeof(IRestrictionPersistencePort)] = typeof(Contracts.RestrictionFact.RestrictionFactRecord),
        [typeof(ICapabilityEvidencePersistencePort)] = typeof(Contracts.CapabilityEvidence.CapabilityEvidenceFact),
        [typeof(IExternalEffectPersistencePort)] = typeof(Contracts.ExternalEffect.ExternalEffectFact),
        [typeof(IWorkRequirementPersistencePort)] = typeof(Contracts.WorkRequirement.WorkRequirementFact),
        [typeof(IPublicationObligationPersistencePort)] = typeof(Contracts.PublicationObligation.PublicationObligationFact),
    };

    [ModuleInitializer]
    internal static void Run()
    {
        var persistence = typeof(IOwnerPersistencePort<>).Assembly;
        var references = persistence.GetReferencedAssemblies().Select(static reference => reference.Name).Where(static name => name is not null).ToHashSet(StringComparer.Ordinal);
        var hpdReferences = references.Where(static name => name!.StartsWith("HPD.Payments.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        Require(hpdReferences.SetEquals(["HPD.Payments.Primitives", "HPD.Payments.Contracts", "HPD.Payments.Supporting"]), "Persistence has an extra, missing, or outward HPD reference");

        foreach (var inward in new[] { typeof(Primitives.Identity.ScopeId).Assembly, typeof(Contracts.WorkRequirement.WorkRequirementFact).Assembly, typeof(Supporting.Ownership.OwnerReference).Assembly })
            Require(!inward.GetReferencedAssemblies().Any(static reference => reference.Name == "HPD.Payments.Persistence"), $"{inward.GetName().Name} references Persistence in the forbidden direction");

        var actualClosedPorts = persistence.GetExportedTypes()
            .Where(static candidate => candidate.IsInterface)
            .Select(candidate => (Port: candidate, OwnerInterface: candidate.GetInterfaces().SingleOrDefault(static parent => parent.IsGenericType && parent.GetGenericTypeDefinition() == typeof(IOwnerPersistencePort<>))))
            .Where(static pair => pair.OwnerInterface is not null)
            .ToArray();
        Require(actualClosedPorts.Length == ExpectedPortFacts.Count, "Persistence does not expose exactly 17 closed authority ports");
        foreach (var pair in actualClosedPorts)
        {
            Require(ExpectedPortFacts.TryGetValue(pair.Port, out var expected), $"unexpected closed authority port {pair.Port.FullName}");
            Require(pair.OwnerInterface!.GenericTypeArguments.Single() == expected, $"{pair.Port.Name} maps to the wrong authority fact");
        }

        var supportingPorts = new[] { typeof(IRelationPersistencePort), typeof(IContinuationPersistencePort), typeof(ICustodyPersistencePort) };
        Require(supportingPorts.All(static port => port.Assembly == typeof(IOwnerPersistencePort<>).Assembly), "supporting persistence port escaped the Persistence assembly");
        Console.WriteLine($"L5-03R architecture conformance passed: exact inward graph and {ExpectedPortFacts.Count} owner mappings.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"L5-03R: {message}.");
    }
}
