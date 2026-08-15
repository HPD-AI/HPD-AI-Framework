using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;
using HPD.Buffers;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityGraphStreamV1Tests
{
    [Fact]
    public void Compiler_prefers_exact_native_rate_then_nearest_supported_mono_rate()
    {
        var capabilities = MonoCapabilities(VoiceActivityInputOwnershipV1.BorrowedSynchronous, 1, 8_000, 16_000);
        var exact = Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Compiled>(
            VoiceActivityGraphStreamCompilerV1.Compile(Plan(capabilities), Format(16_000, 2))).Configuration;
        Assert.Equal(16_000, exact.OutputFormat.SampleRate);
        Assert.Equal(1, exact.OutputFormat.Channels);

        var nearest = Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Compiled>(
            VoiceActivityGraphStreamCompilerV1.Compile(Plan(capabilities), Format(11_000, 1))).Configuration;
        Assert.Equal(8_000, nearest.OutputFormat.SampleRate);
    }

    [Fact]
    public void Compiler_rejects_opaque_or_non_mono_source_geometry()
    {
        var opaque = MonoCapabilities(VoiceActivityInputOwnershipV1.ProviderOpaque, 1);
        Assert.Equal("source-format-conversion-unsupported",
            Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Rejected>(
                VoiceActivityGraphStreamCompilerV1.Compile(Plan(opaque), Format(16_000, 1))).SafeCode);

        var stereoOnly = Capabilities(VoiceActivityInputOwnershipV1.BorrowedSynchronous, 1, channels: 2, rates: [16_000]);
        Assert.Equal("source-format-conversion-unsupported",
            Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Rejected>(
                VoiceActivityGraphStreamCompilerV1.Compile(Plan(stereoOnly), Format(16_000, 2))).SafeCode);

        var strideCapabilities = new VoiceActivitySourceCapabilitiesV1(
            VoiceActivityInputOwnershipV1.BorrowedSynchronous,
            [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
            new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(20), 1),
            stereoOnly.Measurement, VoiceActivitySourceStateModelV1.Stateless,
            VoiceActivitySourceConcurrencyV1.Serial, VoiceActivitySourceControlV1.Unsupported,
            VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
            VoiceActivitySourceControlV1.ReplacementRequired, true, false, 1);
        var stridePlan = new VoiceActivityEffectiveSourcePlanV1(
            new ActivitySourceRequestV1("source", ActivitySourceKindV1.LocalDetector,
                ActivitySourceRoleV1.Authoritative, true), strideCapabilities,
            TimeSpan.FromMilliseconds(10), ProviderActivityVisibilityV1.Unknown);
        Assert.Equal("source-window-stride-unsupported",
            Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Rejected>(
                VoiceActivityGraphStreamCompilerV1.Compile(stridePlan, Format(16_000, 1))).SafeCode);
    }

    [Fact]
    public void Borrowed_stream_owns_conversion_and_dispatch_after_compilation()
    {
        var source = new BorrowedSource(MonoCapabilities(VoiceActivityInputOwnershipV1.BorrowedSynchronous, 1, 16_000));
        var configuration = Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Compiled>(
            VoiceActivityGraphStreamCompilerV1.Compile(Plan(source.Capabilities), Format(48_000, 2))).Configuration;
        var stream = new VoiceActivityGraphStreamV1(
            new VoiceActivitySourceProductV1.BorrowedSynchronous(source), configuration, null,
            new ResidenceCommit(16_000));
        Span<byte> bytes = stackalloc byte[480 * 2 * sizeof(short)];
        var frame = new AudioFrameView(bytes, Format(48_000, 2), 480);
        var window = Assert.Single(Assert.IsType<VoiceActivityWindowAssemblyResultV1.Produced>(
            stream.AssembleBorrowed(in frame, Range(1, bytes.Length))).Windows);

        Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(stream.Observe(window, Stamp()));
        Assert.Equal(320, source.Bytes);
    }

    [Fact]
    public async Task Transferred_stream_releases_input_lease_and_uses_participant_credit_registry()
    {
        var source = new TransferredSource(MonoCapabilities(
            VoiceActivityInputOwnershipV1.IsolatedTransferred, 1, 16_000));
        var registry = new VoiceActivityTransferredWorkRegistryV1(source);
        var configuration = Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Compiled>(
            VoiceActivityGraphStreamCompilerV1.Compile(Plan(source.Capabilities), Format(16_000, 1))).Configuration;
        var stream = new VoiceActivityGraphStreamV1(
            new VoiceActivitySourceProductV1.Transferred(source), configuration, registry,
            new ResidenceCommit(16_000));
        var lease = new Lease(320);
        var owned = new OwnedAudioFrame
        {
            Lease = lease,
            Frame = new HPD.Audio.Primitives.AudioFrame
            {
                Data = lease.Memory, Format = Format(16_000, 1), SamplesPerChannel = 160,
            },
        };
        var window = Assert.Single(Assert.IsType<VoiceActivityWindowAssemblyResultV1.Produced>(
            stream.AssembleOwned(owned, Range(1, 320))).Windows);
        Assert.Equal(1, lease.DisposeCalls);

        var operation = OperationId.Create();
        Assert.IsType<VoiceActivityTransferResultV1.Accepted>(
            await stream.TransferAsync(operation, window, Stamp(), default));
        Assert.Equal(1, registry.PendingCount);
        Assert.IsType<VoiceActivitySettlementResultV1.Settled>(await stream.SettleAsync(operation, default));
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public void Residence_commit_failure_discards_candidate_tail_and_exposes_no_window()
    {
        var source = new BorrowedSource(MonoCapabilities(
            VoiceActivityInputOwnershipV1.BorrowedSynchronous, 1, 16_000));
        var configuration = Assert.IsType<VoiceActivityGraphStreamCompilationResultV1.Compiled>(
            VoiceActivityGraphStreamCompilerV1.Compile(Plan(source.Capabilities), Format(16_000, 1))).Configuration;
        var residence = new ResidenceCommit(16_000) { AllowCommit = false };
        var stream = new VoiceActivityGraphStreamV1(
            new VoiceActivitySourceProductV1.BorrowedSynchronous(source), configuration, null, residence);
        Span<byte> half = stackalloc byte[80 * sizeof(short)];

        Assert.Equal(VoiceActivityInputInvalidReasonV1.ExtentInvalid,
            Assert.IsType<VoiceActivityWindowAssemblyResultV1.Rejected>(
                stream.AssembleBorrowed(new AudioFrameView(half, Format(16_000, 1), 80),
                    Range(1, half.Length))).Reason);
        residence.AllowCommit = true;
        Assert.Empty(Assert.IsType<VoiceActivityWindowAssemblyResultV1.Produced>(
            stream.AssembleBorrowed(new AudioFrameView(half, Format(16_000, 1), 80),
                Range(1, half.Length))).Windows);
        Assert.True(residence.IsCommitted);
    }

    private static VoiceActivityEffectiveSourcePlanV1 Plan(VoiceActivitySourceCapabilitiesV1 capabilities) => new(
        new ActivitySourceRequestV1("source", ActivitySourceKindV1.LocalDetector,
            ActivitySourceRoleV1.Authoritative, true), capabilities,
        capabilities.Window.MaximumWindow, ProviderActivityVisibilityV1.Unknown);

    private static VoiceActivitySourceCapabilitiesV1 MonoCapabilities(
        VoiceActivityInputOwnershipV1 ownership, int maximumPending, params int[] rates) =>
        Capabilities(ownership, maximumPending, 1, rates);

    private static VoiceActivitySourceCapabilitiesV1 Capabilities(
        VoiceActivityInputOwnershipV1 ownership, int maximumPending, int channels, params int[] rates)
    {
        var formats = ownership == VoiceActivityInputOwnershipV1.ProviderOpaque
            ? [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.ProviderOpaque, 0, 0)]
            : rates.Select(rate => new VoiceActivityInputFormatV1(
                VoiceActivitySampleEncodingV1.SignedPcm16, rate, channels)).ToArray();
        return new VoiceActivitySourceCapabilitiesV1(ownership, formats,
            new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 4),
            new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.EngineScore,
                new BoundedAscii("measurement"), -1, 1, null),
            VoiceActivitySourceStateModelV1.StreamLocal,
            ownership == VoiceActivityInputOwnershipV1.BorrowedSynchronous
                ? VoiceActivitySourceConcurrencyV1.Serial : VoiceActivitySourceConcurrencyV1.ParallelWindows,
            VoiceActivitySourceControlV1.Unsupported,
            ownership == VoiceActivityInputOwnershipV1.BorrowedSynchronous
                ? VoiceActivitySourceControlV1.Unsupported : VoiceActivitySourceControlV1.Sequenced,
            ownership == VoiceActivityInputOwnershipV1.BorrowedSynchronous
                ? VoiceActivitySourceControlV1.Unsupported : VoiceActivitySourceControlV1.Sequenced,
            VoiceActivitySourceControlV1.ReplacementRequired, true, false, maximumPending);
    }

    private static AudioFormat Format(int rate, int channels) => new()
    {
        SampleRate = rate, ChannelCount = channels, SampleFormat = AudioSampleFormat.Pcm16,
    };

    private static GraphMediaRangeV1 Range(ulong start, int bytes) => new(
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
        GraphGenerationId.Create(), GraphDirectionV1.IngressForward, GraphTrafficDomainV1.Media,
        new GraphFramePositionV1(start), 1, (ulong)bytes, new DurationNs(10_000_000));

    private static MonotonicStampV1 Stamp() => new(ClockDomainId.Create(), BootId.Create(), 1);

    private sealed class BorrowedSource(VoiceActivitySourceCapabilitiesV1 capabilities) :
        IBorrowedSynchronousVoiceActivitySourceV1
    {
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = capabilities;
        public int Bytes { get; private set; }
        public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window)
        {
            Bytes = window.Bytes.Length;
            return new VoiceActivitySourceOutcomeV1.Observed(new VoiceActivityMeasurementV1.Numeric(0),
                Capabilities.Measurement, window.Extent, 1, window.ObservedAt, window.ObservedAt);
        }
    }

    private sealed class TransferredSource(VoiceActivitySourceCapabilitiesV1 capabilities) :
        ITransferredVoiceActivitySourceV1
    {
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = capabilities;
        public ValueTask<VoiceActivityTransferResultV1> TransferAsync(
            VoiceActivityOwnedWindowV1 window, CancellationToken cancellationToken) =>
            ValueTask.FromResult<VoiceActivityTransferResultV1>(
                new VoiceActivityTransferResultV1.Accepted(window.OperationId));
        public ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
            OperationId operationId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<VoiceActivitySettlementResultV1>(new VoiceActivitySettlementResultV1.Settled(
                operationId, new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.Gap)));
    }

    private sealed class Lease(int length) : IByteBufferLease
    {
        private readonly byte[] _bytes = new byte[length];
        public int DisposeCalls { get; private set; }
        public Memory<byte> Memory => _bytes;
        public void Dispose() => DisposeCalls++;
    }

    private sealed class ResidenceCommit : IVoiceActivityDerivedResidenceCommitV1
    {
        internal ResidenceCommit(int sampleRate)
        {
            Assert.True(GraphMediaBindingV1.TryCreate(0, 1_000, StableId128.CreateRandom(), 1,
                (uint)sampleRate, 1, 2, StableId128.CreateRandom(), 1, 0,
                GraphMediaDiscontinuityKindV1.ResetBefore, 2_000, 1_000, null, out var media));
            DestinationMedia = media!;
        }

        public GraphMediaBindingV1 DestinationMedia { get; }
        public bool IsCommitted { get; private set; }
        internal bool AllowCommit { get; set; } = true;
        public bool TryCommit()
        {
            if (!AllowCommit) return false;
            IsCommitted = true;
            return true;
        }
    }
}
