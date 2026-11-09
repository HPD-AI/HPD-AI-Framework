using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivitySourceCapabilitiesTests
{
    [Fact]
    public void Borrowed_sync_capability_is_serial_nontransferring_and_deeply_owned()
    {
        var formats = new[] { new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1) };
        var capability = Create(VoiceActivityInputOwnership.BorrowedSynchronous, formats,
            VoiceActivitySourceConcurrency.Serial, VoiceActivitySourceControl.Unsupported, 1);

        formats[0] = new VoiceActivityInputFormat(VoiceActivitySampleEncoding.Float32, 48_000, 2);

        Assert.Equal(VoiceActivitySampleEncoding.SignedPcm16, capability.Formats[0].Encoding);
        Assert.Equal(VoiceActivityInputOwnership.BorrowedSynchronous, capability.InputOwnership);
        Assert.Equal(1, capability.MaximumPendingOperations);
    }

    [Fact]
    public void Async_and_opaque_capabilities_require_explicit_transfer_law()
    {
        var decoded = new[] { new VoiceActivityInputFormat(VoiceActivitySampleEncoding.Float32, 48_000, 1) };
        Assert.Throws<ArgumentException>(() => Create(VoiceActivityInputOwnership.IsolatedTransferred, decoded,
            VoiceActivitySourceConcurrency.ParallelWindows, VoiceActivitySourceControl.Unsupported, 4));

        var opaque = new[] { new VoiceActivityInputFormat(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0) };
        var capability = Create(VoiceActivityInputOwnership.ProviderOpaque, opaque,
            VoiceActivitySourceConcurrency.ProviderManaged, VoiceActivitySourceControl.Sequenced, 8);
        Assert.Equal(VoiceActivitySourceStateModel.ProviderOpaque, capability.StateModel);
    }

    [Fact]
    public void Opaque_and_decoded_format_claims_cannot_be_mixed_or_mislabeled()
    {
        var opaque = new VoiceActivityInputFormat(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0);
        var decoded = new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1);
        Assert.Throws<ArgumentException>(() => Create(VoiceActivityInputOwnership.ProviderOpaque,
            new[] { opaque, decoded }, VoiceActivitySourceConcurrency.ProviderManaged,
            VoiceActivitySourceControl.Sequenced, 2));
        Assert.Throws<ArgumentException>(() => Create(VoiceActivityInputOwnership.IsolatedTransferred,
            new[] { opaque }, VoiceActivitySourceConcurrency.Serial,
            VoiceActivitySourceControl.Sequenced, 2));
    }

    [Fact]
    public void Windows_formats_and_pending_work_are_finitely_bounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityWindowCapability(
            TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityInputFormat(
            VoiceActivitySampleEncoding.SignedPcm16, 1_000, 1));
        var decoded = new[] { new VoiceActivityInputFormat(VoiceActivitySampleEncoding.Float32, 48_000, 1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(VoiceActivityInputOwnership.IsolatedTransferred,
            decoded, VoiceActivitySourceConcurrency.Serial, VoiceActivitySourceControl.Sequenced, 0));
    }

    private static VoiceActivitySourceCapabilities Create(
        VoiceActivityInputOwnership ownership,
        IReadOnlyList<VoiceActivityInputFormat> formats,
        VoiceActivitySourceConcurrency concurrency,
        VoiceActivitySourceControl transfer,
        int pending)
    {
        var measurement = new VoiceActivityMeasurementDescriptor(
            VoiceActivityMeasurementKind.EngineScore, new BoundedAscii("score"), -1, 1, null);
        return new VoiceActivitySourceCapabilities(
            ownership, formats,
            new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 32),
            measurement,
            ownership == VoiceActivityInputOwnership.ProviderOpaque
                ? VoiceActivitySourceStateModel.ProviderOpaque
                : VoiceActivitySourceStateModel.GenerationLocal,
            concurrency,
            VoiceActivitySourceControl.Sequenced,
            VoiceActivitySourceControl.Sequenced,
            transfer,
            VoiceActivitySourceControl.ReplacementRequired,
            supportsCancellation: true,
            supportsWarmup: true,
            maximumPendingOperations: pending);
    }
}
