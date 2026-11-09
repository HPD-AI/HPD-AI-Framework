using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Agent.Runtime;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.Tests.VoiceActivity;

public sealed class VoiceActivitySourceParticipantTests
{
    [Fact]
    public async Task Product_is_created_during_prepare_and_hidden_until_admitted_start()
    {
        var source = new Source(Capabilities());
        var calls = 0;
        await using var participant = new VoiceActivitySourceParticipant(Descriptor(), _ =>
        {
            calls++;
            return ValueTask.FromResult<VoiceActivitySourceProduct>(
                new VoiceActivitySourceProduct.BorrowedSynchronous(source));
        });

        var prepared = await participant.PrepareAsync(Context(), default);
        Assert.Equal(RuntimeParticipantDispositionV1.Succeeded, prepared.Disposition);
        Assert.Equal(1, calls);
        Assert.Throws<InvalidOperationException>(() => participant.StartedProduct);

        var started = await participant.StartAsync(prepared.Handle!, default);
        Assert.True(started.IsSuccess);
        Assert.Same(source, Assert.IsType<VoiceActivitySourceProduct.BorrowedSynchronous>(
            participant.StartedProduct).Source);
    }

    [Fact]
    public async Task Exact_prepare_and_start_retries_are_idempotent_but_context_changes_refuse()
    {
        var calls = 0;
        await using var participant = Participant(() => calls++);
        var context = Context();

        var first = await participant.PrepareAsync(context, default);
        var retry = await participant.PrepareAsync(context, default);
        Assert.Same(first.Handle, retry.Handle);
        Assert.Equal(1, calls);

        var other = new RuntimeParticipantContextV1(
            ParticipantId.Create(), context.Authority);
        Assert.Equal(RuntimeParticipantDispositionV1.Refused,
            (await participant.PrepareAsync(other, default)).Disposition);

        Assert.True((await participant.StartAsync(first.Handle!, default)).IsSuccess);
        Assert.Equal("participant-already-started",
            (await participant.StartAsync(first.Handle!, default)).Code.ToString());
    }

    [Fact]
    public async Task Cancellation_and_factory_faults_remain_distinct_and_do_not_publish_a_product()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await using var cancelledParticipant = new VoiceActivitySourceParticipant(Descriptor(), token =>
            ValueTask.FromCanceled<VoiceActivitySourceProduct>(token));
        var cancelledResult = await cancelledParticipant.PrepareAsync(Context(), cancelled.Token);
        Assert.Equal(RuntimeParticipantDispositionV1.Cancelled, cancelledResult.Disposition);
        Assert.Null(cancelledResult.Handle);

        await using var failedParticipant = new VoiceActivitySourceParticipant(Descriptor(), _ =>
            ValueTask.FromException<VoiceActivitySourceProduct>(new InvalidOperationException("fault")));
        var failed = await failedParticipant.PrepareAsync(Context(), default);
        Assert.Equal(RuntimeParticipantDispositionV1.Failed, failed.Disposition);
        Assert.Equal("participant-prepare-failed", failed.Code.ToString());
    }

    [Fact]
    public async Task Drain_and_termination_are_monotone_and_dispose_the_source_once()
    {
        var source = new Source(Capabilities());
        var participant = new VoiceActivitySourceParticipant(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProduct>(
                new VoiceActivitySourceProduct.BorrowedSynchronous(source)));
        var prepared = await participant.PrepareAsync(Context(), default);
        await participant.StartAsync(prepared.Handle!, default);

        Assert.True((await participant.DrainAsync(RuntimeDrainIntentV1.Graceful, default)).IsSuccess);
        Assert.Equal("participant-already-drained",
            (await participant.DrainAsync(RuntimeDrainIntentV1.Forced, default)).Code.ToString());
        Assert.True((await participant.TerminateAsync(RuntimeTerminationCauseV1.Requested, default)).IsSuccess);
        Assert.Equal(1, source.DisposeCalls);
        Assert.Equal("participant-already-terminated",
            (await participant.TerminateAsync(RuntimeTerminationCauseV1.Requested, default)).Code.ToString());
        await participant.DisposeAsync();
        Assert.Equal(1, source.DisposeCalls);
    }

    [Fact]
    public async Task Neutral_runtime_coordinator_supervises_the_complete_source_lifecycle()
    {
        var source = new Source(Capabilities());
        var participant = new VoiceActivitySourceParticipant(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProduct>(
                new VoiceActivitySourceProduct.BorrowedSynchronous(source)));
        var plan = RuntimeParticipantPlanV1.Compile([participant.Descriptor]);
        await using var coordinator = new RuntimeParticipantCoordinatorV1(plan, [participant]);

        Assert.True((await coordinator.PrepareAsync([
            new RuntimeParticipantAdmissionV1(participant.Descriptor.Id, Context())])).IsSuccess);
        Assert.True((await coordinator.StartAsync()).IsSuccess);
        Assert.Same(source, Assert.IsType<VoiceActivitySourceProduct.BorrowedSynchronous>(
            participant.StartedProduct).Source);
        Assert.True((await coordinator.DrainAsync(RuntimeDrainIntentV1.Graceful)).IsSuccess);
        Assert.True((await coordinator.TerminateAsync(RuntimeTerminationCauseV1.Requested)).IsSuccess);
        Assert.Equal(RuntimeParticipantCoordinatorStateV1.Completed, coordinator.State);
        Assert.Equal(1, source.DisposeCalls);
    }

    [Fact]
    public async Task Transferred_work_credits_are_created_only_at_start_and_isolated_per_participant()
    {
        var firstSource = new TransferredSource(TransferredCapabilities());
        var secondSource = new TransferredSource(TransferredCapabilities());
        await using var first = new VoiceActivitySourceParticipant(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProduct>(new VoiceActivitySourceProduct.Transferred(firstSource)));
        await using var second = new VoiceActivitySourceParticipant(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProduct>(new VoiceActivitySourceProduct.Transferred(secondSource)));
        var firstPrepared = await first.PrepareAsync(Context(), default);
        var secondPrepared = await second.PrepareAsync(Context(), default);
        Assert.Throws<InvalidOperationException>(() => first.StartedTransferredWork);
        await first.StartAsync(firstPrepared.Handle!, default);
        await second.StartAsync(secondPrepared.Handle!, default);

        Assert.NotSame(first.StartedTransferredWork, second.StartedTransferredWork);
        Assert.Equal(0, first.StartedTransferredWork.PendingCount);
        Assert.Equal(0, second.StartedTransferredWork.PendingCount);
        var firstWork = first.StartedTransferredWork;
        await first.DrainAsync(RuntimeDrainIntentV1.Graceful, default);
        var rejected = Assert.IsType<VoiceActivityTransferResult.Rejected>(await firstWork.TransferAsync(
            new VoiceActivityOwnedWindow(OperationId.Create(), new byte[320],
                new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1),
                new VoiceActivityMediaExtent(GraphGenerationId.Create(), 1, 2, true),
                new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 1)), default));
        Assert.Equal(VoiceActivityNoObservationReason.SourceRevoked,
            Assert.IsType<VoiceActivitySourceOutcome.NoObservation>(rejected.Outcome).Reason);
    }

    [Fact]
    public async Task Compiled_graph_stream_is_owned_and_fenced_by_the_participant_lifecycle()
    {
        var source = new Source(Capabilities());
        var configuration = new VoiceActivityGraphStreamConfiguration(
            new AudioFormat
            {
                SampleRate = 16_000, ChannelCount = 1, SampleFormat = AudioSampleFormat.Pcm16,
            },
            new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1),
            TimeSpan.FromMilliseconds(10), 1);
        await using var participant = new VoiceActivitySourceParticipant(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProduct>(
                new VoiceActivitySourceProduct.BorrowedSynchronous(source)), configuration,
            new ResidenceCommit());
        var prepared = await participant.PrepareAsync(Context(), default);
        Assert.Throws<InvalidOperationException>(() => participant.StartedGraphStream);
        Assert.True((await participant.StartAsync(prepared.Handle!, default)).IsSuccess);
        var stream = participant.StartedGraphStream;

        Assert.True((await participant.DrainAsync(RuntimeDrainIntentV1.Graceful, default)).IsSuccess);
        Assert.Throws<InvalidOperationException>(() => participant.StartedGraphStream);
        Assert.Throws<InvalidOperationException>(() => stream.Observe(null!, default));
    }

    private static VoiceActivitySourceParticipant Participant(Action created) =>
        new(Descriptor(), _ =>
        {
            created();
            return ValueTask.FromResult<VoiceActivitySourceProduct>(
                new VoiceActivitySourceProduct.BorrowedSynchronous(new Source(Capabilities())));
        });

    private static RuntimeParticipantDescriptorV1 Descriptor() => new(
        new BoundedAscii("voice-activity-source"), new BoundedAscii("Audio"),
        new BoundedAscii("VoiceActivity"), [], AuthorityAxisId.Graph,
        new DurationNs(1_000_000_000), new DurationNs(1_000_000_000),
        new DurationNs(1_000_000_000), new DurationNs(1_000_000_000), []);

    private static RuntimeParticipantContextV1 Context() => new(
        ParticipantId.Create(),
        ExpectedAuthorityVectorV1.Create(
            new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create()), []));

    private static VoiceActivitySourceCapabilities Capabilities() => new(
        VoiceActivityInputOwnership.BorrowedSynchronous,
        [new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModel.Stateless, VoiceActivitySourceConcurrency.Serial,
        VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Unsupported,
        VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.ReplacementRequired,
        true, false, 1);

    private static VoiceActivitySourceCapabilities TransferredCapabilities() => new(
        VoiceActivityInputOwnership.IsolatedTransferred,
        [new VoiceActivityInputFormat(VoiceActivitySampleEncoding.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapability(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptor(VoiceActivityMeasurementKind.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModel.StreamLocal, VoiceActivitySourceConcurrency.ParallelWindows,
        VoiceActivitySourceControl.Unsupported, VoiceActivitySourceControl.Sequenced,
        VoiceActivitySourceControl.Sequenced, VoiceActivitySourceControl.ReplacementRequired,
        true, false, 1);

    private sealed class Source(VoiceActivitySourceCapabilities capabilities) :
        IBorrowedSynchronousVoiceActivitySource, IDisposable
    {
        public VoiceActivitySourceCapabilities Capabilities { get; } = capabilities;
        public int DisposeCalls { get; private set; }
        public VoiceActivitySourceOutcome Observe(scoped in VoiceActivityBorrowedWindow window) =>
            throw new NotSupportedException();
        public void Dispose() => DisposeCalls++;
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
            ValueTask.FromResult<VoiceActivitySettlementResult>(
                new VoiceActivitySettlementResult.NotFound(operationId));
    }

    private sealed class ResidenceCommit : IVoiceActivityDerivedResidenceCommit
    {
        internal ResidenceCommit()
        {
            Assert.True(GraphMediaBinding.TryCreate(0, 400, StableId128.CreateRandom(), 1,
                16_000, 1, 2, StableId128.CreateRandom(), 1, 0,
                GraphMediaDiscontinuityKind.ResetBefore, 800, 400, null, out var media));
            DestinationMedia = media!;
        }

        public GraphMediaBinding DestinationMedia { get; }
        public bool IsCommitted { get; private set; }
        public bool TryCommit() { IsCommitted = true; return true; }
    }
}
