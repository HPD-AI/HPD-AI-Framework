using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;
using HPD.Buffers;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityGraphStreamTests
{
    [Fact]
    public void Compiler_prefers_exact_native_rate_then_nearest_supported_mono_rate()
    {
        var capabilities = MonoCapabilities(VoiceActivityInputOwnership.BorrowedSynchronous, 1, 8_000, 16_000);
        var exact = Assert.IsType<VoiceActivityGraphStreamCompilationResult.Compiled>(
            VoiceActivityGraphStreamCompiler.Compile(Plan(capabilities), Format(16_000, 2))).Configuration;
        Assert.Equal(16_000, exact.OutputFormat.SampleRate);
        Assert.Equal(1, exact.OutputFormat.Channels);

        var nearest = Assert.IsType<VoiceActivityGraphStreamCompilationResult.Compiled>(
            VoiceActivityGraphStreamCompiler.Compile(Plan(capabilities), Format(11_000, 1))).Configuration;
        Assert.Equal(8_000, nearest.OutputFormat.SampleRate);
    }

    [Fact]
    public void Compiler_rejects_opaque_or_non_mono_source_geometry()
    {
        var opaque = MonoCapabilities(VoiceActivityInputOwnership.ProviderOpaque, 1);
        Assert.Equal("source-format-conversion-unsupported",
            Assert.IsType<VoiceActivityGraphStreamCompilationResult.Rejected>(
                VoiceActivityGraphStreamCompiler.Compile(Plan(opaque), Format(16_000, 1))).SafeCode);

        var stereoOnly = Capabilities(VoiceActivityInputOwnership.BorrowedSynchronous, 1, channels: 2, rates: [16_000]);
        Assert.Equal("source-format-conversion-unsupported",
            Assert.IsType<VoiceActivityGraphStreamCompilationResult.Rejected>(
                VoiceActivityGraphStreamCompiler.Compile(Plan(stereoOnly), Format(16_000, 2))).SafeCode);

        var strideCapabilities = new VoiceActivitySourceCapabilities(
            VoiceActivityInputOwnership.BorrowedSynchronous,
            [new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
            new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(20), 1),
            stereoOnly.Measurement, VoiceActivitySourceStateModel.Stateless,
            VoiceActivitySourceConcurrency.Serial, VoiceActivitySourceControl.Unsupported,
            VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
            VoiceActivitySourceControl.ReplacementRequired, true, false, 1);
        var stridePlan = new VoiceActivityEffectiveSourcePlan(
            new ActivitySourceRequest("source", ActivitySourceKind.LocalDetector,
                ActivitySourceRole.Authoritative, true), strideCapabilities,
            TimeSpan.FromMilliseconds(10), ProviderActivityVisibility.Unknown);
        Assert.Equal("source-window-stride-unsupported",
            Assert.IsType<VoiceActivityGraphStreamCompilationResult.Rejected>(
                VoiceActivityGraphStreamCompiler.Compile(stridePlan, Format(16_000, 1))).SafeCode);
    }

    [Fact]
    public void Borrowed_stream_owns_conversion_and_dispatch_after_compilation()
    {
        var source = new BorrowedSource(MonoCapabilities(VoiceActivityInputOwnership.BorrowedSynchronous, 1, 16_000));
        var configuration = Assert.IsType<VoiceActivityGraphStreamCompilationResult.Compiled>(
            VoiceActivityGraphStreamCompiler.Compile(Plan(source.Capabilities), Format(48_000, 2))).Configuration;
        var stream = new VoiceActivityGraphStream(
            new VoiceActivitySourceProduct.BorrowedSynchronous(source), configuration, null,
            new ResidenceCommit(16_000));
        Span<byte> bytes = stackalloc byte[480 * 2 * sizeof(short)];
        var frame = new AudioFrameView(bytes, Format(48_000, 2), 480);
        var window = Assert.Single(Assert.IsType<VoiceActivityWindowAssemblyResult.Produced>(
            stream.AssembleBorrowed(in frame, Range(1, bytes.Length))).Windows);

        Assert.IsType<VoiceActivitySourceOutcome.Observed>(stream.Observe(window, Stamp()));
        Assert.Equal(320, source.Bytes);
    }

    [Fact]
    public async Task Transferred_stream_releases_input_lease_and_uses_participant_credit_registry()
    {
        var source = new TransferredSource(MonoCapabilities(
            VoiceActivityInputOwnership.IsolatedTransferred, 1, 16_000));
        var registry = new VoiceActivityTransferredWorkRegistry(source);
        var configuration = Assert.IsType<VoiceActivityGraphStreamCompilationResult.Compiled>(
            VoiceActivityGraphStreamCompiler.Compile(Plan(source.Capabilities), Format(16_000, 1))).Configuration;
        var stream = new VoiceActivityGraphStream(
            new VoiceActivitySourceProduct.Transferred(source), configuration, registry,
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
        var window = Assert.Single(Assert.IsType<VoiceActivityWindowAssemblyResult.Produced>(
            stream.AssembleOwned(owned, Range(1, 320))).Windows);
        Assert.Equal(1, lease.DisposeCalls);

        var operation = OperationId.Create();
        Assert.IsType<VoiceActivityTransferResult.Accepted>(
            await stream.TransferAsync(operation, window, Stamp(), default));
        Assert.Equal(1, registry.PendingCount);
        Assert.IsType<VoiceActivitySettlementResult.Settled>(await stream.SettleAsync(operation, default));
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public void Residence_commit_failure_discards_candidate_tail_and_exposes_no_window()
    {
        var source = new BorrowedSource(MonoCapabilities(
            VoiceActivityInputOwnership.BorrowedSynchronous, 1, 16_000));
        var configuration = Assert.IsType<VoiceActivityGraphStreamCompilationResult.Compiled>(
            VoiceActivityGraphStreamCompiler.Compile(Plan(source.Capabilities), Format(16_000, 1))).Configuration;
        var residence = new ResidenceCommit(16_000) { AllowCommit = false };
        var stream = new VoiceActivityGraphStream(
            new VoiceActivitySourceProduct.BorrowedSynchronous(source), configuration, null, residence);
        Span<byte> half = stackalloc byte[80 * sizeof(short)];

        Assert.Equal(VoiceActivityInputInvalidReason.ExtentInvalid,
            Assert.IsType<VoiceActivityWindowAssemblyResult.Rejected>(
                stream.AssembleBorrowed(new AudioFrameView(half, Format(16_000, 1), 80),
                    Range(1, half.Length))).Reason);
        residence.AllowCommit = true;
        Assert.Empty(Assert.IsType<VoiceActivityWindowAssemblyResult.Produced>(
            stream.AssembleBorrowed(new AudioFrameView(half, Format(16_000, 1), 80),
                Range(1, half.Length))).Windows);
        Assert.True(residence.IsCommitted);
    }

    private static VoiceActivityEffectiveSourcePlan Plan(VoiceActivitySourceCapabilities capabilities) => new(
        new ActivitySourceRequest("source", ActivitySourceKind.LocalDetector,
            ActivitySourceRole.Authoritative, true), capabilities,
        capabilities.Window.MaximumWindow, ProviderActivityVisibility.Unknown);

    private static VoiceActivitySourceCapabilities MonoCapabilities(
        VoiceActivityInputOwnership ownership, int maximumPending, params int[] rates) =>
        Capabilities(ownership, maximumPending, 1, rates);

    private static VoiceActivitySourceCapabilities Capabilities(
        VoiceActivityInputOwnership ownership, int maximumPending, int channels, params int[] rates)
    {
        var formats = ownership == VoiceActivityInputOwnership.ProviderOpaque
            ? [new VoiceActivityInputFormat(VoiceActivitySampleEncoding.ProviderOpaque, 0, 0)]
            : rates.Select(rate => new VoiceActivityInputFormat(
                VoiceActivitySampleEncoding.SignedPcm16, rate, channels)).ToArray();
        return new VoiceActivitySourceCapabilities(ownership, formats,
            new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(10), 4),
            new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.EngineScore,
                new BoundedAscii("measurement"), -1, 1, null),
            VoiceActivitySourceStateModel.StreamLocal,
            ownership == VoiceActivityInputOwnership.BorrowedSynchronous
                ? VoiceActivitySourceConcurrency.Serial : VoiceActivitySourceConcurrency.ParallelWindows,
            VoiceActivitySourceControl.Unsupported,
            ownership == VoiceActivityInputOwnership.BorrowedSynchronous
                ? VoiceActivitySourceControl.Unsupported : VoiceActivitySourceControl.Sequenced,
            ownership == VoiceActivityInputOwnership.BorrowedSynchronous
                ? VoiceActivitySourceControl.Unsupported : VoiceActivitySourceControl.Sequenced,
            VoiceActivitySourceControl.ReplacementRequired, true, false, maximumPending);
    }

    private static AudioFormat Format(int rate, int channels) => new()
    {
        SampleRate = rate, ChannelCount = channels, SampleFormat = AudioSampleFormat.Pcm16,
    };

    private static GraphMediaRange Range(ulong start, int bytes) => new(
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
        GraphGenerationId.Create(), GraphDirection.IngressForward, GraphTrafficDomain.Media,
        new GraphFramePosition(start), 1, (ulong)bytes, new DurationNs(10_000_000));

    private static MonotonicStampV1 Stamp() => new(ClockDomainId.Create(), BootId.Create(), 1);

    private sealed class BorrowedSource(VoiceActivitySourceCapabilities capabilities) :
        IBorrowedSynchronousVoiceActivitySource
    {
        public VoiceActivitySourceCapabilities Capabilities { get; } = capabilities;
        public int Bytes { get; private set; }
        public VoiceActivitySourceOutcome Observe(scoped in VoiceActivityBorrowedWindow window)
        {
            Bytes = window.Bytes.Length;
            return new VoiceActivitySourceOutcome.Observed(new VoiceActivityMeasurement.Numeric(0),
                Capabilities.Measurement, window.Extent, 1, window.ObservedAt, window.ObservedAt);
        }
    }

    private sealed class TransferredSource(VoiceActivitySourceCapabilities capabilities) :
        ITransferredVoiceActivitySource
    {
        public VoiceActivitySourceCapabilities Capabilities { get; } = capabilities;
        public ValueTask<VoiceActivityTransferResult> TransferAsync(
            VoiceActivityOwnedWindow window, CancellationToken cancellationToken) =>
            ValueTask.FromResult<VoiceActivityTransferResult>(
                new VoiceActivityTransferResult.Accepted(window.OperationId));
        public ValueTask<VoiceActivitySettlementResult> SettleAsync(
            OperationId operationId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<VoiceActivitySettlementResult>(new VoiceActivitySettlementResult.Settled(
                operationId, new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.Gap)));
    }

    private sealed class Lease(int length) : IByteBufferLease
    {
        private readonly byte[] _bytes = new byte[length];
        public int DisposeCalls { get; private set; }
        public Memory<byte> Memory => _bytes;
        public void Dispose() => DisposeCalls++;
    }

    private sealed class ResidenceCommit : IVoiceActivityDerivedResidenceCommit
    {
        internal ResidenceCommit(int sampleRate)
        {
            Assert.True(GraphMediaBinding.TryCreate(0, 1_000, StableId128.CreateRandom(), 1,
                (uint)sampleRate, 1, 2, StableId128.CreateRandom(), 1, 0,
                GraphMediaDiscontinuityKind.ResetBefore, 2_000, 1_000, null, out var media));
            DestinationMedia = media!;
        }

        public GraphMediaBinding DestinationMedia { get; }
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
