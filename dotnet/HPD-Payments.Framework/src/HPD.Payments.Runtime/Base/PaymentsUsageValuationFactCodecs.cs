using System.Globalization;
using System.Text.Json.Serialization;
using HPD.Payments.Contracts.MeasuredFact;
using HPD.Payments.Contracts.MeasurementGeneration;
using HPD.Payments.Contracts.Valuation;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides closed AOT-safe codecs for the admitted usage-to-valuation authority facts.</summary>
public static class PaymentsUsageValuationFactCodecs
{
    /// <summary>Gets the exact measured-fact codec.</summary>
    public static PaymentsFactJsonCodec<MeasuredFactRecord> MeasuredFact { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.measured-fact.v1", PaymentsUsageValuationJsonContext.Default.MeasuredFactPayload, MeasuredFactPayload.From, static value => value.ToValue());

    /// <summary>Gets the exact measurement-generation codec.</summary>
    public static PaymentsFactJsonCodec<MeasurementGenerationFact> MeasurementGeneration { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.measurement-generation.v1", PaymentsUsageValuationJsonContext.Default.MeasurementGenerationPayload, MeasurementGenerationPayload.From, static value => value.ToValue());

    /// <summary>Gets the exact valuation-fact codec.</summary>
    public static PaymentsFactJsonCodec<ValuationFact> Valuation { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.valuation.v1", PaymentsUsageValuationJsonContext.Default.ValuationPayload, ValuationPayload.From, static value => value.ToValue());
}

internal sealed record NamedTimePayload(int Kind, string Value)
{
    internal static NamedTimePayload From(NamedTime value) => new((int)value.Kind, value.Value.ToString("O", CultureInfo.InvariantCulture));
    internal NamedTime ToValue() => NamedTime.Create((TimeKind)Kind, DateTimeOffset.Parse(Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
}

internal sealed record RevisionPayload(string Kind, ulong Value)
{
    internal static RevisionPayload From(Revision value) => new(value.Kind, value.Value);
    internal Revision ToValue() => Revision.Create(Kind, Value);
}

internal sealed record HistoricalOwnerCutPayload(string Tenant, string Environment, string Authority, PaymentsIdentityPayload Subject, ulong Through)
{
    internal static HistoricalOwnerCutPayload From(OwnerCut value) => new(value.OwnerScope.Tenant, value.OwnerScope.Environment, value.OwnerScope.Authority,
        PaymentsIdentityPayload.From(value.Subject), value.Through.Value);
    internal OwnerCut ToValue() => new(ScopeId.Create(Tenant, Environment, Authority), Subject.ToValue(), OwnerGeneration.Create(Through));
}

internal sealed record HistoricalCutPayload(int Frame, NamedTimePayload KnowledgeThrough, HistoricalOwnerCutPayload[] OwnerCuts, ushort Major, ushort Minor)
{
    internal static HistoricalCutPayload From(HistoricalCut value) => new((int)value.Frame, NamedTimePayload.From(value.KnowledgeThrough),
        value.OwnerCuts.Select(HistoricalOwnerCutPayload.From).ToArray(), value.Version.Major, value.Version.Minor);
    internal HistoricalCut ToValue() => new((HistoricalFrameKind)Frame, KnowledgeThrough.ToValue(), OwnerCuts.Select(static value => value.ToValue()), ContractVersion.Create(Major, Minor));
}

internal sealed record MeasuredFactPayload(
    PaymentsIdentityPayload FactId, PaymentsIdentityPayload SubjectId, PaymentsIdentityPayload SourceId, PaymentsDigestPayload SemanticDigest,
    decimal Quantity, string Unit, NamedTimePayload OccurredFrom, NamedTimePayload OccurredUntil, RevisionPayload DefinitionRevision,
    ulong ExpectedGeneration, ulong Generation, NamedTimePayload AcceptedAt, ushort Major, ushort Minor)
{
    internal static MeasuredFactPayload From(MeasuredFactRecord value) => new(PaymentsIdentityPayload.From(value.Admission.FactId),
        PaymentsIdentityPayload.From(value.Admission.SubjectId), PaymentsIdentityPayload.From(value.Admission.SourceId), PaymentsDigestPayload.From(value.Admission.SemanticDigest),
        value.Admission.Quantity.Value, value.Admission.Quantity.Unit, NamedTimePayload.From(value.Admission.OccurredFrom), NamedTimePayload.From(value.Admission.OccurredUntil),
        RevisionPayload.From(value.Admission.DefinitionRevision), value.Admission.ExpectedGeneration.Value, value.Generation.Value, NamedTimePayload.From(value.AcceptedAt),
        value.ContractVersion.Major, value.ContractVersion.Minor);
    internal MeasuredFactRecord ToValue() => new(new AdmitMeasuredFactCommand(FactId.ToValue(), SubjectId.ToValue(), SourceId.ToValue(), SemanticDigest.ToValue(),
        MeasuredQuantity.Create(Quantity, Unit), OccurredFrom.ToValue(), OccurredUntil.ToValue(), DefinitionRevision.ToValue(), OwnerGeneration.Create(ExpectedGeneration)),
        OwnerGeneration.Create(Generation), AcceptedAt.ToValue(), ContractVersion.Create(Major, Minor));
}

internal sealed record MeasurementGenerationPayload(
    PaymentsIdentityPayload GenerationId, PaymentsIdentityPayload SubjectId, NamedTimePayload WindowFrom, NamedTimePayload WindowUntil,
    HistoricalCutPayload SourceCut, int AlgebraKind, RevisionPayload AlgebraRevision, bool SupportsPartitionMerge, bool IsOrderSensitive,
    bool HasDeclaredInverse, bool RequiresRecomputeOnRemoval, PaymentsIdentityPayload[] Members, int Completeness, ulong ExpectedGeneration,
    decimal Result, string Unit, ulong Generation, NamedTimePayload CalculatedAt)
{
    internal static MeasurementGenerationPayload From(MeasurementGenerationFact value) => new(PaymentsIdentityPayload.From(value.Command.GenerationId),
        PaymentsIdentityPayload.From(value.Command.SubjectId), NamedTimePayload.From(value.Command.WindowFrom), NamedTimePayload.From(value.Command.WindowUntil),
        HistoricalCutPayload.From(value.Command.SourceCut), (int)value.Command.Algebra.Kind, RevisionPayload.From(value.Command.Algebra.Revision),
        value.Command.Algebra.SupportsPartitionMerge, value.Command.Algebra.IsOrderSensitive, value.Command.Algebra.HasDeclaredInverse,
        value.Command.Algebra.RequiresRecomputeOnRemoval, value.Command.Members.Select(PaymentsIdentityPayload.From).ToArray(), (int)value.Command.Completeness,
        value.Command.ExpectedGeneration.Value, value.Result, value.Unit, value.Generation.Value, NamedTimePayload.From(value.CalculatedAt));
    internal MeasurementGenerationFact ToValue()
    {
        var algebra = new MeasurementAlgebraContract((MeasurementAlgebraKind)AlgebraKind, AlgebraRevision.ToValue(), SupportsPartitionMerge,
            IsOrderSensitive, HasDeclaredInverse, RequiresRecomputeOnRemoval);
        var command = new CreateMeasurementGenerationCommand(GenerationId.ToValue(), SubjectId.ToValue(), WindowFrom.ToValue(), WindowUntil.ToValue(),
            SourceCut.ToValue(), algebra, Members.Select(static value => value.ToValue()), (GenerationCompleteness)Completeness, OwnerGeneration.Create(ExpectedGeneration));
        return new(command, Result, Unit, OwnerGeneration.Create(Generation), CalculatedAt.ToValue());
    }
}

internal sealed record ValuationPayload(
    PaymentsIdentityPayload ValuationId, PaymentsIdentityPayload ManifestId, PaymentsIdentityPayload MeasurementGenerationId,
    HistoricalCutPayload HistoricalCut, RevisionPayload PricingRevision, RevisionPayload AlgorithmRevision, byte Scale, int RoundingMode,
    string RoundingStage, int Reproducibility, PaymentsIdentityPayload[] Inputs, PaymentsDigestPayload ManifestDigest,
    decimal Precise, decimal Rounded, string Currency, ulong ExpectedGeneration, NamedTimePayload CalculatedAt,
    ulong Generation, NamedTimePayload AcceptedAt)
{
    internal static ValuationPayload From(ValuationFact value) => new(PaymentsIdentityPayload.From(value.Admission.ValuationId),
        PaymentsIdentityPayload.From(value.Admission.Manifest.ManifestId), PaymentsIdentityPayload.From(value.Admission.Manifest.MeasurementGenerationId),
        HistoricalCutPayload.From(value.Admission.Manifest.HistoricalCut), RevisionPayload.From(value.Admission.Manifest.PricingRevision),
        RevisionPayload.From(value.Admission.Manifest.AlgorithmRevision), value.Admission.Manifest.Rounding.Scale, (int)value.Admission.Manifest.Rounding.Mode,
        value.Admission.Manifest.Rounding.Stage, (int)value.Admission.Manifest.Reproducibility, value.Admission.Manifest.Inputs.Select(PaymentsIdentityPayload.From).ToArray(),
        PaymentsDigestPayload.From(value.Admission.Manifest.Digest), value.Admission.Result.Precise, value.Admission.Result.Rounded, value.Admission.Result.Currency,
        value.Admission.ExpectedGeneration.Value, NamedTimePayload.From(value.Admission.CalculatedAt), value.Generation.Value, NamedTimePayload.From(value.AcceptedAt));
    internal ValuationFact ToValue()
    {
        var rounding = new RoundingContract(Scale, (MidpointRounding)RoundingMode, RoundingStage);
        var manifest = new ValuationInputManifest(ManifestId.ToValue(), MeasurementGenerationId.ToValue(), HistoricalCut.ToValue(), PricingRevision.ToValue(),
            AlgorithmRevision.ToValue(), rounding, (ReproducibilityKind)Reproducibility, Inputs.Select(static value => value.ToValue()), ManifestDigest.ToValue());
        var command = new AdmitValuationCommand(ValuationId.ToValue(), manifest, new EconomicValue(Precise, Rounded, Currency, rounding),
            OwnerGeneration.Create(ExpectedGeneration), CalculatedAt.ToValue());
        return new(command, OwnerGeneration.Create(Generation), AcceptedAt.ToValue());
    }
}

[JsonSerializable(typeof(MeasuredFactPayload))]
[JsonSerializable(typeof(MeasurementGenerationPayload))]
[JsonSerializable(typeof(ValuationPayload))]
internal sealed partial class PaymentsUsageValuationJsonContext : JsonSerializerContext;
