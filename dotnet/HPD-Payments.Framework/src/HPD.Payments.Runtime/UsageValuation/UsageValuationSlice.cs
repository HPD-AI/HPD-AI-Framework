using HPD.Payments.Contracts.MeasuredFact;
using HPD.Payments.Contracts.MeasurementGeneration;
using HPD.Payments.Contracts.Valuation;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Runtime.UsageValuation;

/// <summary>Calculates a closed measurement algebra over an exact admitted membership set.</summary>
public static class MeasurementGenerationCalculator
{
    /// <summary>Calculates a generation without consulting storage or ambient time.</summary>
    public static MeasurementGenerationFact Calculate(CreateMeasurementGenerationCommand command, IReadOnlyCollection<MeasuredFactRecord> members, NamedTime calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(command); ArgumentNullException.ThrowIfNull(members);
        if (!command.ExpectedGeneration.TryNext(out var generation)) throw new ArgumentException("Generation overflow.", nameof(command));
        if (members.Count != command.Members.Count || members.Select(x => x.Admission.FactId).ToHashSet().SetEquals(command.Members) is false)
            throw new ArgumentException("The supplied facts must exactly equal the declared membership.", nameof(members));
        if (members.Any(x => x.Admission.SubjectId != command.SubjectId || x.Admission.OccurredFrom.Value < command.WindowFrom.Value || x.Admission.OccurredUntil.Value > command.WindowUntil.Value))
            throw new ArgumentException("Every member must belong to the declared subject and window.", nameof(members));

        var ordered = members.OrderBy(x => x.Admission.OccurredUntil.Value).ThenBy(x => x.Admission.FactId.ToString(), StringComparer.Ordinal).ToArray();
        var unit = command.Algebra.Kind is MeasurementAlgebraKind.Count or MeasurementAlgebraKind.UniqueCount ? "count" : RequireSingleUnit(ordered);
        var result = command.Algebra.Kind switch
        {
            MeasurementAlgebraKind.Count => ordered.Length,
            MeasurementAlgebraKind.Sum => ordered.Sum(x => x.Admission.Quantity.Value),
            MeasurementAlgebraKind.Maximum when ordered.Length > 0 => ordered.Max(x => x.Admission.Quantity.Value),
            MeasurementAlgebraKind.Latest when ordered.Length > 0 => ordered[^1].Admission.Quantity.Value,
            MeasurementAlgebraKind.Maximum or MeasurementAlgebraKind.Latest => throw new ArgumentException("The algebra requires at least one member.", nameof(members)),
            _ => throw new NotSupportedException($"Algebra {command.Algebra.Kind} requires a separately revisioned evaluator."),
        };
        return new(command, result, unit, generation, calculatedAt);
    }

    private static string RequireSingleUnit(IReadOnlyCollection<MeasuredFactRecord> members)
    {
        var units = members.Select(x => x.Admission.Quantity.Unit).Distinct(StringComparer.Ordinal).ToArray();
        return units.Length == 1 ? units[0] : throw new ArgumentException("This algebra requires exactly one measurement unit.", nameof(members));
    }
}

/// <summary>Applies an exact revisioned unit price and the manifest's rounding contract.</summary>
public sealed class UnitRateValuationAlgorithm
{
    /// <summary>Gets the algorithm revision accepted by this implementation.</summary>
    public Revision AlgorithmRevision { get; }
    /// <summary>Gets the pricing revision accepted by this implementation.</summary>
    public Revision PricingRevision { get; }
    /// <summary>Gets the exact price per generated unit.</summary>
    public decimal UnitRate { get; }
    /// <summary>Gets the output currency.</summary>
    public string Currency { get; }

    /// <summary>Creates a closed deterministic unit-rate algorithm.</summary>
    public UnitRateValuationAlgorithm(Revision algorithmRevision, Revision pricingRevision, decimal unitRate, string currency)
    {
        if (!algorithmRevision.IsValid || !pricingRevision.IsValid || string.IsNullOrEmpty(currency) || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Invalid valuation algorithm declaration.");
        (AlgorithmRevision, PricingRevision, UnitRate, Currency) = (algorithmRevision, pricingRevision, unitRate, currency);
    }

    /// <summary>Calculates an immutable valuation admission and rejects revision drift.</summary>
    public AdmitValuationCommand Calculate(SemanticId valuationId, ValuationInputManifest manifest, MeasurementGenerationFact generation, OwnerGeneration expectedGeneration, NamedTime calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(generation);
        if (manifest.MeasurementGenerationId != generation.Command.GenerationId || manifest.AlgorithmRevision != AlgorithmRevision || manifest.PricingRevision != PricingRevision)
            throw new ArgumentException("The manifest does not bind this generation and algorithm revision.");
        var precise = checked(generation.Result * UnitRate);
        var value = new EconomicValue(precise, manifest.Rounding.Apply(precise), Currency, manifest.Rounding);
        return new(valuationId, manifest, value, expectedGeneration, calculatedAt);
    }
}

/// <summary>Performs storage-neutral authority-local admissions for the P14D usage-to-valuation slice.</summary>
public sealed class UsageValuationAdmissions
{
    private readonly IOwnerPersistencePort<MeasuredFactRecord> _measured;
    private readonly IOwnerPersistencePort<MeasurementGenerationFact> _generations;
    private readonly IOwnerPersistencePort<ValuationFact> _valuations;

    /// <summary>Creates the slice over inward persistence ports only.</summary>
    public UsageValuationAdmissions(IOwnerPersistencePort<MeasuredFactRecord> measured, IOwnerPersistencePort<MeasurementGenerationFact> generations, IOwnerPersistencePort<ValuationFact> valuations)
        => (_measured, _generations, _valuations) = (measured ?? throw new ArgumentNullException(nameof(measured)), generations ?? throw new ArgumentNullException(nameof(generations)), valuations ?? throw new ArgumentNullException(nameof(valuations)));

    /// <summary>Admits one measured fact with replay/conflict delegated to compare-bind persistence.</summary>
    public ValueTask<OwnerAppendReceipt<MeasuredFactRecord>> AdmitMeasuredAsync(AdmitMeasuredFactCommand command, AtomicDomain domain, NamedTime acceptedAt, ContractVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.ExpectedGeneration.TryNext(out var next)) throw new ArgumentException("Generation overflow.", nameof(command));
        var fact = new MeasuredFactRecord(command, next, acceptedAt, version);
        return _measured.CompareBindAppendAsync(new(new OwnerReference(FrozenAuthority.MeasuredFact, command.FactId, command.ExpectedGeneration), command.SemanticDigest, domain, fact), cancellationToken);
    }

    /// <summary>Calculates and admits one immutable measurement generation.</summary>
    public ValueTask<OwnerAppendReceipt<MeasurementGenerationFact>> AdmitGenerationAsync(CreateMeasurementGenerationCommand command, IReadOnlyCollection<MeasuredFactRecord> members, CanonicalDigest digest, AtomicDomain domain, NamedTime calculatedAt, CancellationToken cancellationToken = default)
    {
        var fact = MeasurementGenerationCalculator.Calculate(command, members, calculatedAt);
        return _generations.CompareBindAppendAsync(new(new OwnerReference(FrozenAuthority.MeasurementGeneration, command.GenerationId, command.ExpectedGeneration), digest, domain, fact), cancellationToken);
    }

    /// <summary>Admits a previously calculated valuation without creating an obligation.</summary>
    public ValueTask<OwnerAppendReceipt<ValuationFact>> AdmitValuationAsync(AdmitValuationCommand command, CanonicalDigest digest, AtomicDomain domain, NamedTime acceptedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.ExpectedGeneration.TryNext(out var next)) throw new ArgumentException("Generation overflow.", nameof(command));
        var fact = new ValuationFact(command, next, acceptedAt);
        return _valuations.CompareBindAppendAsync(new(new OwnerReference(FrozenAuthority.Valuation, command.ValuationId, command.ExpectedGeneration), digest, domain, fact), cancellationToken);
    }
}
