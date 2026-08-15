using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;
using HPD.Buffers;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityGraphAdapterV1Tests
{
    [Fact]
    public void Borrowed_frame_is_stack_scoped_and_preserves_graph_extent()
    {
        var source = new BorrowedSource(Capabilities(VoiceActivityInputOwnershipV1.BorrowedSynchronous));
        Span<byte> bytes = stackalloc byte[320];
        var frame = View(bytes);
        var outcome = Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(
            VoiceActivityGraphAdapterV1.ObserveBorrowed(
                new VoiceActivitySourceProductV1.BorrowedSynchronous(source), in frame,
                Range((ulong)bytes.Length), Stamp()));

        Assert.Equal(10L, outcome.Extent.StartInclusive);
        Assert.Equal(11L, outcome.Extent.EndExclusive);
        Assert.True(outcome.Extent.Exact);
        Assert.Equal(320, source.ObservedBytes);
    }

    [Fact]
    public void Format_range_and_discontinuity_fail_before_source_execution()
    {
        var source = new BorrowedSource(Capabilities(VoiceActivityInputOwnershipV1.BorrowedSynchronous));
        Span<byte> bytes = stackalloc byte[320];
        var wrongGeometry = new AudioFrameView(bytes, Format(), 159);
        Assert.Equal(VoiceActivityInputInvalidReasonV1.FormatMismatch,
            Assert.IsType<VoiceActivitySourceOutcomeV1.InvalidInput>(VoiceActivityGraphAdapterV1.ObserveBorrowed(
                new VoiceActivitySourceProductV1.BorrowedSynchronous(source), in wrongGeometry,
                Range((ulong)bytes.Length), Stamp())).Reason);

        var discontinuity = View(bytes, AudioFrameFlags.Discontinuity);
        Assert.Equal(VoiceActivityInputInvalidReasonV1.DiscontinuousWindow,
            Assert.IsType<VoiceActivitySourceOutcomeV1.InvalidInput>(VoiceActivityGraphAdapterV1.ObserveBorrowed(
                new VoiceActivitySourceProductV1.BorrowedSynchronous(source), in discontinuity,
                Range((ulong)bytes.Length), Stamp())).Reason);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Transferred_frame_is_deeply_isolated_and_lease_is_released_before_await()
    {
        var lease = new Lease(320);
        lease.Memory.Span.Fill(7);
        var source = new TransferredSource(Capabilities(VoiceActivityInputOwnershipV1.IsolatedTransferred), lease);
        var operation = OperationId.Create();
        var result = await VoiceActivityGraphAdapterV1.TransferOwnedAsync(
            new VoiceActivitySourceProductV1.Transferred(source), operation,
            Owned(lease), Range(320), Stamp(), default);

        Assert.IsType<VoiceActivityTransferResultV1.Accepted>(result);
        Assert.Equal(1, lease.DisposeCalls);
        Assert.True(source.LeaseWasDisposed);
        Assert.All(source.Bytes, value => Assert.Equal(7, value));
    }

    [Fact]
    public async Task Invalid_transferred_input_releases_the_lease_without_invoking_provider()
    {
        var lease = new Lease(320);
        var source = new TransferredSource(Capabilities(VoiceActivityInputOwnershipV1.IsolatedTransferred), lease);
        var rejected = Assert.IsType<VoiceActivityTransferResultV1.Rejected>(
            await VoiceActivityGraphAdapterV1.TransferOwnedAsync(
                new VoiceActivitySourceProductV1.Transferred(source), OperationId.Create(), Owned(lease),
                Range(319), Stamp(), default));

        Assert.Equal(VoiceActivityInputInvalidReasonV1.ExtentInvalid,
            Assert.IsType<VoiceActivitySourceOutcomeV1.InvalidInput>(rejected.Outcome).Reason);
        Assert.Equal(1, lease.DisposeCalls);
        Assert.Equal(0, source.TransferCalls);
    }

    [Fact]
    public async Task Settlement_is_queryable_only_on_the_transferred_path()
    {
        var operation = OperationId.Create();
        var lease = new Lease(320);
        var source = new TransferredSource(Capabilities(VoiceActivityInputOwnershipV1.IsolatedTransferred), lease);
        Assert.IsType<VoiceActivitySettlementResultV1.Settled>(await VoiceActivityGraphAdapterV1.SettleAsync(
            new VoiceActivitySourceProductV1.Transferred(source), operation, default));
        Assert.IsType<VoiceActivitySettlementResultV1.NotFound>(await VoiceActivityGraphAdapterV1.SettleAsync(
            new VoiceActivitySourceProductV1.BorrowedSynchronous(
                new BorrowedSource(Capabilities(VoiceActivityInputOwnershipV1.BorrowedSynchronous))), operation, default));
    }

    [Fact]
    public void Graph_owned_conversion_dispatches_an_exact_window_without_exposing_its_buffer()
    {
        var input = new AudioFormat
        {
            SampleRate = 48_000, ChannelCount = 2, SampleFormat = AudioSampleFormat.Pcm16,
        };
        var assembler = new VoiceActivityPcm16WindowAssemblerV1(input,
            new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1),
            TimeSpan.FromMilliseconds(10), 1);
        var bytes = new byte[480 * 2 * sizeof(short)];
        var assembled = Assert.Single(Assert.IsType<VoiceActivityWindowAssemblyResultV1.Produced>(
            assembler.Process(bytes, input, 480, AudioRecoveryKind.None, AudioFrameFlags.None,
                Range((ulong)bytes.Length))).Windows);
        var source = new BorrowedSource(Capabilities(VoiceActivityInputOwnershipV1.BorrowedSynchronous));

        Assert.IsType<VoiceActivitySourceOutcomeV1.Observed>(VoiceActivityGraphAdapterV1.ObserveAssembled(
            new VoiceActivitySourceProductV1.BorrowedSynchronous(source), assembled, Stamp()));
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

    private static GraphMediaRangeV1 Range(ulong bytes) => new(
        new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()),
        GraphGenerationId.Create(), GraphDirectionV1.IngressForward, GraphTrafficDomainV1.Media,
        new GraphFramePositionV1(10), 1, bytes, new DurationNs(10_000_000));

    private static MonotonicStampV1 Stamp() => new(ClockDomainId.Create(), BootId.Create(), 1);

    private static VoiceActivitySourceCapabilitiesV1 Capabilities(VoiceActivityInputOwnershipV1 ownership) => new(
        ownership, [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModelV1.Stateless,
        ownership == VoiceActivityInputOwnershipV1.BorrowedSynchronous
            ? VoiceActivitySourceConcurrencyV1.Serial : VoiceActivitySourceConcurrencyV1.ParallelWindows,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
        ownership == VoiceActivityInputOwnershipV1.BorrowedSynchronous
            ? VoiceActivitySourceControlV1.Unsupported : VoiceActivitySourceControlV1.Sequenced,
        VoiceActivitySourceControlV1.ReplacementRequired, true, false, 1);

    private sealed class BorrowedSource(VoiceActivitySourceCapabilitiesV1 capabilities) :
        IBorrowedSynchronousVoiceActivitySourceV1
    {
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = capabilities;
        public int Calls { get; private set; }
        public int ObservedBytes { get; private set; }
        public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window)
        {
            Calls++;
            ObservedBytes = window.Bytes.Length;
            return new VoiceActivitySourceOutcomeV1.Observed(new VoiceActivityMeasurementV1.Numeric(0.5),
                Capabilities.Measurement, window.Extent, 1, window.ObservedAt, window.ObservedAt);
        }
    }

    private sealed class TransferredSource(VoiceActivitySourceCapabilitiesV1 capabilities, Lease lease) :
        ITransferredVoiceActivitySourceV1
    {
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = capabilities;
        public int TransferCalls { get; private set; }
        public bool LeaseWasDisposed { get; private set; }
        public byte[] Bytes { get; private set; } = [];
        public ValueTask<VoiceActivityTransferResultV1> TransferAsync(
            VoiceActivityOwnedWindowV1 window, CancellationToken cancellationToken)
        {
            TransferCalls++;
            LeaseWasDisposed = lease.DisposeCalls == 1;
            Bytes = window.Bytes.ToArray();
            return ValueTask.FromResult<VoiceActivityTransferResultV1>(
                new VoiceActivityTransferResultV1.Accepted(window.OperationId));
        }
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
}
