using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivitySourceCapabilitiesV1Tests
{
    [Fact]
    public void Borrowed_sync_capability_is_serial_nontransferring_and_deeply_owned()
    {
        var formats = new[] { new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1) };
        var capability = Create(VoiceActivityInputOwnershipV1.BorrowedSynchronous, formats,
            VoiceActivitySourceConcurrencyV1.Serial, VoiceActivitySourceControlV1.Unsupported, 1);

        formats[0] = new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.Float32, 48_000, 2);

        Assert.Equal(VoiceActivitySampleEncodingV1.SignedPcm16, capability.Formats[0].Encoding);
        Assert.Equal(VoiceActivityInputOwnershipV1.BorrowedSynchronous, capability.InputOwnership);
        Assert.Equal(1, capability.MaximumPendingOperations);
    }

    [Fact]
    public void Async_and_opaque_capabilities_require_explicit_transfer_law()
    {
        var decoded = new[] { new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.Float32, 48_000, 1) };
        Assert.Throws<ArgumentException>(() => Create(VoiceActivityInputOwnershipV1.IsolatedTransferred, decoded,
            VoiceActivitySourceConcurrencyV1.ParallelWindows, VoiceActivitySourceControlV1.Unsupported, 4));

        var opaque = new[] { new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0) };
        var capability = Create(VoiceActivityInputOwnershipV1.ProviderOpaque, opaque,
            VoiceActivitySourceConcurrencyV1.ProviderManaged, VoiceActivitySourceControlV1.Sequenced, 8);
        Assert.Equal(VoiceActivitySourceStateModelV1.ProviderOpaque, capability.StateModel);
    }

    [Fact]
    public void Opaque_and_decoded_format_claims_cannot_be_mixed_or_mislabeled()
    {
        var opaque = new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0);
        var decoded = new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1);
        Assert.Throws<ArgumentException>(() => Create(VoiceActivityInputOwnershipV1.ProviderOpaque,
            new[] { opaque, decoded }, VoiceActivitySourceConcurrencyV1.ProviderManaged,
            VoiceActivitySourceControlV1.Sequenced, 2));
        Assert.Throws<ArgumentException>(() => Create(VoiceActivityInputOwnershipV1.IsolatedTransferred,
            new[] { opaque }, VoiceActivitySourceConcurrencyV1.Serial,
            VoiceActivitySourceControlV1.Sequenced, 2));
    }

    [Fact]
    public void Windows_formats_and_pending_work_are_finitely_bounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityWindowCapabilityV1(
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityInputFormatV1(
            VoiceActivitySampleEncodingV1.SignedPcm16, 1_000, 1));
        var decoded = new[] { new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.Float32, 48_000, 1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(VoiceActivityInputOwnershipV1.IsolatedTransferred,
            decoded, VoiceActivitySourceConcurrencyV1.Serial, VoiceActivitySourceControlV1.Sequenced, 0));
    }

    private static VoiceActivitySourceCapabilitiesV1 Create(
        VoiceActivityInputOwnershipV1 ownership,
        IReadOnlyList<VoiceActivityInputFormatV1> formats,
        VoiceActivitySourceConcurrencyV1 concurrency,
        VoiceActivitySourceControlV1 transfer,
        int pending)
    {
        var measurement = new VoiceActivityMeasurementDescriptorV1(
            VoiceActivityMeasurementKindV1.EngineScore, new BoundedAscii("score"), -1, 1, null);
        return new VoiceActivitySourceCapabilitiesV1(
            ownership, formats,
            new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 32),
            measurement,
            ownership == VoiceActivityInputOwnershipV1.ProviderOpaque
                ? VoiceActivitySourceStateModelV1.ProviderOpaque
                : VoiceActivitySourceStateModelV1.GenerationLocal,
            concurrency,
            VoiceActivitySourceControlV1.Sequenced,
            VoiceActivitySourceControlV1.Sequenced,
            transfer,
            VoiceActivitySourceControlV1.ReplacementRequired,
            supportsCancellation: true,
            supportsWarmup: true,
            maximumPendingOperations: pending);
    }
}
