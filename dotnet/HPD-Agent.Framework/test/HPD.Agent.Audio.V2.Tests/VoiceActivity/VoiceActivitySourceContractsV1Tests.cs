using HPD.Agent.Authority;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivitySourceContractsV1Tests
{
    [Fact]
    public void OutcomeArms_AreClosedAndKeepFaultDistinctFromSilence()
    {
        Assert.IsType<VoiceActivitySourceOutcomeV1.NoObservation>(
            new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.ProviderNotObservable));
        Assert.IsType<VoiceActivitySourceOutcomeV1.InvalidInput>(
            new VoiceActivitySourceOutcomeV1.InvalidInput(VoiceActivityInputInvalidReasonV1.FormatMismatch));
        Assert.IsType<VoiceActivitySourceOutcomeV1.Unavailable>(
            new VoiceActivitySourceOutcomeV1.Unavailable(VoiceActivitySourceUnavailableReasonV1.ProviderUnavailable,
                VoiceActivityRetryabilityV1.AfterReplacement));
        var fault = new VoiceActivitySourceOutcomeV1.Fault(VoiceActivitySourceFaultClassV1.InferenceFailure,
            VoiceActivityStateValidityV1.ResetRequired, VoiceActivityRetryabilityV1.SameGeneration);
        Assert.Equal(VoiceActivitySourceFaultClassV1.InferenceFailure, fault.Classification);
        Assert.DoesNotContain(typeof(VoiceActivitySourceOutcomeV1).GetNestedTypes(),
            static type => type.Name.Contains("Silence", StringComparison.Ordinal));
    }

    [Fact]
    public void Observed_RequiresMatchingMeasurementDescriptorAndRange()
    {
        var descriptor = Descriptor(VoiceActivityMeasurementKindV1.EngineScore, null, -1, 1);
        var observed = new VoiceActivitySourceOutcomeV1.Observed(new VoiceActivityMeasurementV1.Numeric(.25),
            descriptor, Extent(), 1, Stamp(1), Stamp(2));
        Assert.Equal(.25, Assert.IsType<VoiceActivityMeasurementV1.Numeric>(observed.Measurement).Value);
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcomeV1.Observed(
            new VoiceActivityMeasurementV1.Binary(true), descriptor, Extent(), 1, Stamp(1), Stamp(2)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcomeV1.Observed(
            new VoiceActivityMeasurementV1.Numeric(2), descriptor, Extent(), 1, Stamp(1), Stamp(2)));
    }

    [Fact]
    public void CalibratedLikelihood_RequiresCalibrationIdentity()
    {
        Assert.Throws<ArgumentException>(() => Descriptor(
            VoiceActivityMeasurementKindV1.CalibratedLikelihood, null, 0, 1));
        var descriptor = Descriptor(VoiceActivityMeasurementKindV1.CalibratedLikelihood, Hash(1), 0, 1);
        Assert.Equal(Hash(1), descriptor.CalibrationIdentity);
        Assert.Throws<ArgumentException>(() => new VoiceActivityMeasurementDescriptorV1(
            VoiceActivityMeasurementKindV1.BinaryDecision, new BoundedAscii("decision"), 0, 1, Hash(2)));
    }

    [Fact]
    public void ProviderOpaqueCategory_CannotMasqueradeAsNumericScore()
    {
        var descriptor = Descriptor(VoiceActivityMeasurementKindV1.ProviderOpaqueCategory, null, 0, 1);
        Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(new VoiceActivitySourceOutcomeV1.Observed(
            new VoiceActivityMeasurementV1.Category(new BoundedAscii("speech-started")),
            descriptor, Extent(), 1, Stamp(1), Stamp(1)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcomeV1.Observed(
            new VoiceActivityMeasurementV1.Numeric(1), descriptor, Extent(), 1, Stamp(1), Stamp(1)));
    }

    [Fact]
    public void Observed_RejectsZeroSequenceAndIncomparableOrReversedTiming()
    {
        var descriptor = Descriptor(VoiceActivityMeasurementKindV1.BinaryDecision, null, 0, 1);
        var measurement = new VoiceActivityMeasurementV1.Binary(true);
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivitySourceOutcomeV1.Observed(
            measurement, descriptor, Extent(), 0, Stamp(1), Stamp(2)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcomeV1.Observed(
            measurement, descriptor, Extent(), 1, Stamp(2), Stamp(1)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcomeV1.Observed(
            measurement, descriptor, Extent(), 1, Stamp(1), new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 2)));
    }

    [Fact]
    public void MediaExtent_RequiresOneGraphGenerationAndPositiveNonemptyRange()
    {
        var extent = Extent();
        Assert.Equal((10L, 20L, true), (extent.StartInclusive, extent.EndExclusive, extent.Exact));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityMediaExtentV1(GraphGenerationId.Create(), 2, 2, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityMediaExtentV1(GraphGenerationId.Create(), -1, 2, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void InvalidClosedEnumValues_AreRejected(int raw)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VoiceActivitySourceOutcomeV1.NoObservation((VoiceActivityNoObservationReasonV1)raw));
    }

    private static VoiceActivityMeasurementDescriptorV1 Descriptor(
        VoiceActivityMeasurementKindV1 kind, Hash256? calibration, double minimum, double maximum) =>
        new(kind, new BoundedAscii(kind == VoiceActivityMeasurementKindV1.ProviderOpaqueCategory ? "category" : "score"),
            minimum, maximum, calibration);

    private static VoiceActivityMediaExtentV1 Extent() => new(GraphGenerationId.Create(), 10, 20, true);

    private static readonly ClockDomainId Domain = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();
    private static MonotonicStampV1 Stamp(ulong value) => new(Domain, Boot, value);

    private static Hash256 Hash(byte seed)
    {
        var bytes = Enumerable.Range(seed, 32).Select(static value => (byte)value).ToArray();
        return Hash256.FromBytes(bytes);
    }
}
