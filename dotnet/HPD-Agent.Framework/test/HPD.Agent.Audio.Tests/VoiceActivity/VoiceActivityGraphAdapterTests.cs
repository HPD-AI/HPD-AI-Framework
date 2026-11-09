using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;
using HPD.Buffers;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivityGraphAdapterTests
{
    [Fact]
    public void Borrowed_frame_is_stack_scoped_and_preserves_graph_extent()
    {
        var source = new BorrowedSource(Capabilities(VoiceActivityInputOwnership.BorrowedSynchronous));
        Span<byte> bytes = stackalloc byte[320];
        var frame = View(bytes);
        var outcome = Assert.IsType<VoiceActivitySourceOutcome.Observed>(
            VoiceActivityGraphAdapter.ObserveBorrowed(
                new VoiceActivitySourceProduct.BorrowedSynchronous(source), in frame,
                Range((ulong)bytes.Length), Stamp()));

        Assert.Equal(10L, outcome.Extent.StartInclusive);
        Assert.Equal(11L, outcome.Extent.EndExclusive);
        Assert.True(outcome.Extent.Exact);
        Assert.Equal(320, source.ObservedBytes);
    }

    [Fact]
    public void Format_range_and_discontinuity_fail_before_source_execution()
    {
        var source = new BorrowedSource(Capabilities(VoiceActivityInputOwnership.BorrowedSynchronous));
        Span<byte> bytes = stackalloc byte[320];
        var wrongGeometry = new AudioFrameView(bytes, Format(), 159);
        Assert.Equal(VoiceActivityInputInvalidReason.FormatMismatch,
            Assert.IsType<VoiceActivitySourceOutcome.InvalidInput>(VoiceActivityGraphAdapter.ObserveBorrowed(
                new VoiceActivitySourceProduct.BorrowedSynchronous(source), in wrongGeometry,
                Range((ulong)bytes.Length), Stamp())).Reason);

        var discontinuity = View(bytes, AudioFrameFlags.Discontinuity);
        Assert.Equal(VoiceActivityInputInvalidReason.DiscontinuousWindow,
            Assert.IsType<VoiceActivitySourceOutcome.InvalidInput>(VoiceActivityGraphAdapter.ObserveBorrowed(
                new VoiceActivitySourceProduct.BorrowedSynchronous(source), in discontinuity,
                Range((ulong)bytes.Length), Stamp())).Reason);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Transferred_frame_is_deeply_isolated_and_lease_is_released_before_await()
    {
        var lease = new Lease(320);
        lease.Memory.Span.Fill(7);
        var source = new TransferredSource(Capabilities(VoiceActivityInputOwnership.IsolatedTransferred), lease);
        var operation = OperationId.Create();
        var result = await VoiceActivityGraphAdapter.TransferOwnedAsync(
            new VoiceActivitySourceProduct.Transferred(source), operation,
            Owned(lease), Range(320), Stamp(), default);

        Assert.IsType<VoiceActivityTransferResult.Accepted>(result);
        Assert.Equal(1, lease.DisposeCalls);
        Assert.True(source.LeaseWasDisposed);
        Assert.All(source.Bytes, value => Assert.Equal(7, value));
    }

    [Fact]
    public async Task Invalid_transferred_input_releases_the_lease_without_invoking_provider()
    {
        var lease = new Lease(320);
        var source = new TransferredSource(Capabilities(VoiceActivityInputOwnership.IsolatedTransferred), lease);
        var rejected = Assert.IsType<VoiceActivityTransferResult.Rejected>(
            await VoiceActivityGraphAdapter.TransferOwnedAsync(
                new VoiceActivitySourceProduct.Transferred(source), OperationId.Create(), Owned(lease),
                Range(319), Stamp(), default));

        Assert.Equal(VoiceActivityInputInvalidReason.ExtentInvalid,
            Assert.IsType<VoiceActivitySourceOutcome.InvalidInput>(rejected.Outcome).Reason);
        Assert.Equal(1, lease.DisposeCalls);
        Assert.Equal(0, source.TransferCalls);
    }

    [Fact]
    public async Task Settlement_is_queryable_only_on_the_transferred_path()
    {
        var operation = OperationId.Create();
        var lease = new Lease(320);
        var source = new TransferredSource(Capabilities(VoiceActivityInputOwnership.IsolatedTransferred), lease);
        Assert.IsType<VoiceActivitySettlementResult.Settled>(await VoiceActivityGraphAdapter.SettleAsync(
            new VoiceActivitySourceProduct.Transferred(source), operation, default));
        Assert.IsType<VoiceActivitySettlementResult.NotFound>(await VoiceActivityGraphAdapter.SettleAsync(
            new VoiceActivitySourceProduct.BorrowedSynchronous(
                new BorrowedSource(Capabilities(VoiceActivityInputOwnership.BorrowedSynchronous))), operation, default));
    }

    [Fact]
    public void Graph_owned_conversion_dispatches_an_exact_window_without_exposing_its_buffer()
    {
        var input = new AudioFormat
        {
            SampleRate = 48_000, ChannelCount = 2, SampleFormat = AudioSampleFormat.Pcm16,
        };
        var assembler = new VoiceActivityPcm16WindowAssembler(input,
            new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1),
            TimeSpan.FromMilliseconds(10), 1);
        var bytes = new byte[480 * 2 * sizeof(short)];
        var assembled = Assert.Single(Assert.IsType<VoiceActivityWindowAssemblyResult.Produced>(
            assembler.Process(bytes, input, 480, AudioRecoveryKind.None, AudioFrameFlags.None,
                Range((ulong)bytes.Length))).Windows);
        var source = new BorrowedSource(Capabilities(VoiceActivityInputOwnership.BorrowedSynchronous));

        Assert.IsType<VoiceActivitySourceOutcome.Observed>(VoiceActivityGraphAdapter.ObserveAssembled(
            new VoiceActivitySourceProduct.BorrowedSynchronous(source), assembled, Stamp()));
        Assert.Equal(320, source.ObservedBytes);
    }

    private static AudioFrameView View(Span<byte> bytes, AudioFrameFlags flags = AudioFrameFlags.None) =>
        new(bytes, Format(), 160, flags: flags);

    private static OwnedAudioFrame Owned(Lease lease) => new()
    {
        Lease = lease,
        Frame = new HPD.Audio.Primitives.AudioFrame
        {
            Data = lease.Memory[..320], Format = Format(), SamplesPerChannel = 160,
        },
    };

    private static AudioFormat Format() => new()
    {
        SampleRate = 16_000,
        ChannelCount = 1,
        SampleFormat = AudioSampleFormat.Pcm16,
    };

    private static GraphMediaRange Range(ulong bytes) => new(
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
        GraphGenerationId.Create(), GraphDirection.IngressForward, GraphTrafficDomain.Media,
        new GraphFramePosition(10), 1, bytes, new DurationNs(10_000_000));

    private static MonotonicStampV1 Stamp() => new(ClockDomainId.Create(), BootId.Create(), 1);

    private static VoiceActivitySourceCapabilities Capabilities(VoiceActivityInputOwnership ownership) => new(
        ownership, [new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModel.Stateless,
        ownership == VoiceActivityInputOwnership.BorrowedSynchronous
            ? VoiceActivitySourceConcurrency.Serial : VoiceActivitySourceConcurrency.ParallelWindows,
        VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
        ownership == VoiceActivityInputOwnership.BorrowedSynchronous
            ? VoiceActivitySourceControl.Unsupported : VoiceActivitySourceControl.Sequenced,
        VoiceActivitySourceControl.ReplacementRequired, true, false, 1);

    private sealed class BorrowedSource(VoiceActivitySourceCapabilities capabilities) :
        IBorrowedSynchronousVoiceActivitySource
    {
        public VoiceActivitySourceCapabilities Capabilities { get; } = capabilities;
        public int Calls { get; private set; }
        public int ObservedBytes { get; private set; }
        public VoiceActivitySourceOutcome Observe(scoped in VoiceActivityBorrowedWindow window)
        {
            Calls++;
            ObservedBytes = window.Bytes.Length;
            return new VoiceActivitySourceOutcome.Observed(new VoiceActivityMeasurement.Numeric(0.5),
                Capabilities.Measurement, window.Extent, 1, window.ObservedAt, window.ObservedAt);
        }
    }

    private sealed class TransferredSource(VoiceActivitySourceCapabilities capabilities, Lease lease) :
        ITransferredVoiceActivitySource
    {
        public VoiceActivitySourceCapabilities Capabilities { get; } = capabilities;
        public int TransferCalls { get; private set; }
        public bool LeaseWasDisposed { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public ValueTask<VoiceActivityTransferResult> TransferAsync(
            VoiceActivityOwnedWindow window, CancellationToken cancellationToken)
        {
            TransferCalls++;
            LeaseWasDisposed = lease.DisposeCalls == 1;
            Bytes = window.Bytes.ToArray();
            return ValueTask.FromResult<VoiceActivityTransferResult>(
                new VoiceActivityTransferResult.Accepted(window.OperationId));
        }
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
}
