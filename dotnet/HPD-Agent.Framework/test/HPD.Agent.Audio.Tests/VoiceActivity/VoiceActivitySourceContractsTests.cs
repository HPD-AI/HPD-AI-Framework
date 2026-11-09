using HPD.Agent.Authority;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivitySourceContractsTests
{
    [Fact]
    public void OutcomeArms_AreClosedAndKeepFaultDistinctFromSilence()
    {
        Assert.IsType<VoiceActivitySourceOutcome.NoObservation>(
            new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.ProviderNotObservable));
        Assert.IsType<VoiceActivitySourceOutcome.InvalidInput>(
            new VoiceActivitySourceOutcome.InvalidInput(VoiceActivityInputInvalidReason.FormatMismatch));
        Assert.IsType<VoiceActivitySourceOutcome.Unavailable>(
            new VoiceActivitySourceOutcome.Unavailable(VoiceActivitySourceUnavailableReason.ProviderUnavailable,
                VoiceActivityRetryability.AfterReplacement));
        var fault = new VoiceActivitySourceOutcome.Fault(VoiceActivitySourceFaultClass.InferenceFailure,
            VoiceActivityStateValidity.ResetRequired, VoiceActivityRetryability.SameGeneration);
        Assert.Equal(VoiceActivitySourceFaultClass.InferenceFailure, fault.Classification);
        Assert.DoesNotContain(typeof(VoiceActivitySourceOutcome).GetNestedTypes(),
            static type => type.Name.Contains("Silence", StringComparison.Ordinal));
    }

    [Fact]
    public void Observed_RequiresMatchingMeasurementDescriptorAndRange()
    {
        var descriptor = Descriptor(VoiceActivityMeasurementKind.EngineScore, null, -1, 1);
        var observed = new VoiceActivitySourceOutcome.Observed(new VoiceActivityMeasurement.Numeric(.25),
            descriptor, Extent(), 1, Stamp(1), Stamp(2));
        Assert.Equal(.25, Assert.IsType<VoiceActivityMeasurement.Numeric>(observed.Measurement).Value);
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcome.Observed(
            new VoiceActivityMeasurement.Binary(true), descriptor, Extent(), 1, Stamp(1), Stamp(2)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcome.Observed(
            new VoiceActivityMeasurement.Numeric(2), descriptor, Extent(), 1, Stamp(1), Stamp(2)));
    }

    [Fact]
    public void CalibratedLikelihood_RequiresCalibrationIdentity()
    {
        Assert.Throws<ArgumentException>(() => Descriptor(
            VoiceActivityMeasurementKind.CalibratedLikelihood, null, 0, 1));
        var descriptor = Descriptor(VoiceActivityMeasurementKind.CalibratedLikelihood, Hash(1), 0, 1);
        Assert.Equal(Hash(1), descriptor.CalibrationIdentity);
        Assert.Throws<ArgumentException>(() => new VoiceActivityMeasurementDescriptor(
            VoiceActivityMeasurementKind.BinaryDecision, new BoundedAscii("decision"), 0, 1, Hash(2)));
    }

    [Fact]
    public void ProviderOpaqueCategory_CannotMasqueradeAsNumericScore()
    {
        var descriptor = Descriptor(VoiceActivityMeasurementKind.ProviderOpaqueCategory, null, 0, 1);
        Assert.IsType<VoiceActivitySourceOutcome.Observed>(new VoiceActivitySourceOutcome.Observed(
            new VoiceActivityMeasurement.Category(new BoundedAscii("speech-started")),
            descriptor, Extent(), 1, Stamp(1), Stamp(1)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcome.Observed(
            new VoiceActivityMeasurement.Numeric(1), descriptor, Extent(), 1, Stamp(1), Stamp(1)));
    }

    [Fact]
    public void Observed_RejectsZeroSequenceAndIncomparableOrReversedTiming()
    {
        var descriptor = Descriptor(VoiceActivityMeasurementKind.BinaryDecision, null, 0, 1);
        var measurement = new VoiceActivityMeasurement.Binary(true);
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivitySourceOutcome.Observed(
            measurement, descriptor, Extent(), 0, Stamp(1), Stamp(2)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcome.Observed(
            measurement, descriptor, Extent(), 1, Stamp(2), Stamp(1)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceOutcome.Observed(
            measurement, descriptor, Extent(), 1, Stamp(1), new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 2)));
    }

    [Fact]
    public void MediaExtent_RequiresOneGraphGenerationAndPositiveNonemptyRange()
    {
        var extent = Extent();
        Assert.Equal((10L, 20L, true), (extent.StartInclusive, extent.EndExclusive, extent.Exact));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityMediaExtent(GraphGenerationId.Create(), 2, 2, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityMediaExtent(GraphGenerationId.Create(), -1, 2, true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void InvalidClosedEnumValues_AreRejected(int raw)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VoiceActivitySourceOutcome.NoObservation((VoiceActivityNoObservationReason)raw));
    }

    private static VoiceActivityMeasurementDescriptor Descriptor(
        VoiceActivityMeasurementKind kind, Hash256? calibration, double minimum, double maximum) =>
        new(kind, new BoundedAscii(kind == VoiceActivityMeasurementKind.ProviderOpaqueCategory ? "category" : "score"),
            minimum, maximum, calibration);

    private static VoiceActivityMediaExtent Extent() => new(GraphGenerationId.Create(), 10, 20, true);

    private static readonly ClockDomainId Domain = ClockDomainId.Create();
    private static readonly BootId Boot = BootId.Create();
    private static MonotonicStampV1 Stamp(ulong value) => new(Domain, Boot, value);

    private static Hash256 Hash(byte seed)
    {
        var bytes = Enumerable.Range(seed, 32).Select(static value => (byte)value).ToArray();
        return Hash256.FromBytes(bytes);
    }
}
