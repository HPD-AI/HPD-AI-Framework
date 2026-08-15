using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Agent.Runtime;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivitySourceParticipantV1Tests
{
    [Fact]
    public async Task Product_is_created_during_prepare_and_hidden_until_admitted_start()
    {
        var source = new Source(Capabilities());
        var calls = 0;
        await using var participant = new VoiceActivitySourceParticipantV1(Descriptor(), _ =>
        {
            calls++;
            return ValueTask.FromResult<VoiceActivitySourceProductV1>(
                new VoiceActivitySourceProductV1.BorrowedSynchronous(source));
        });

        var prepared = await participant.PrepareAsync(Context(), default);
        Assert.Equal(RuntimeParticipantDispositionV1.Succeeded, prepared.Disposition);
        Assert.Equal(1, calls);
        Assert.Throws<InvalidOperationException>(() => participant.StartedProduct);

        var started = await participant.StartAsync(prepared.Handle!, default);
        Assert.True(started.IsSuccess);
        Assert.Same(source, Assert.IsType<VoiceActivitySourceProductV1.BorrowedSynchronous>(
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
        await using var cancelledParticipant = new VoiceActivitySourceParticipantV1(Descriptor(), token =>
            ValueTask.FromCanceled<VoiceActivitySourceProductV1>(token));
        var cancelledResult = await cancelledParticipant.PrepareAsync(Context(), cancelled.Token);
        Assert.Equal(RuntimeParticipantDispositionV1.Cancelled, cancelledResult.Disposition);
        Assert.Null(cancelledResult.Handle);

        await using var failedParticipant = new VoiceActivitySourceParticipantV1(Descriptor(), _ =>
            ValueTask.FromException<VoiceActivitySourceProductV1>(new InvalidOperationException("fault")));
        var failed = await failedParticipant.PrepareAsync(Context(), default);
        Assert.Equal(RuntimeParticipantDispositionV1.Failed, failed.Disposition);
        Assert.Equal("participant-prepare-failed", failed.Code.ToString());
    }

    [Fact]
    public async Task Drain_and_termination_are_monotone_and_dispose_the_source_once()
    {
        var source = new Source(Capabilities());
        var participant = new VoiceActivitySourceParticipantV1(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProductV1>(
                new VoiceActivitySourceProductV1.BorrowedSynchronous(source)));
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
        var participant = new VoiceActivitySourceParticipantV1(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProductV1>(
                new VoiceActivitySourceProductV1.BorrowedSynchronous(source)));
        var plan = RuntimeParticipantPlanV1.Compile([participant.Descriptor]);
        await using var coordinator = new RuntimeParticipantCoordinatorV1(plan, [participant]);

        Assert.True((await coordinator.PrepareAsync([
            new RuntimeParticipantAdmissionV1(participant.Descriptor.Id, Context())])).IsSuccess);
        Assert.True((await coordinator.StartAsync()).IsSuccess);
        Assert.Same(source, Assert.IsType<VoiceActivitySourceProductV1.BorrowedSynchronous>(
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
        await using var first = new VoiceActivitySourceParticipantV1(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProductV1>(new VoiceActivitySourceProductV1.Transferred(firstSource)));
        await using var second = new VoiceActivitySourceParticipantV1(Descriptor(), _ =>
            ValueTask.FromResult<VoiceActivitySourceProductV1>(new VoiceActivitySourceProductV1.Transferred(secondSource)));
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
        var rejected = Assert.IsType<VoiceActivityTransferResultV1.Rejected>(await firstWork.TransferAsync(
            new VoiceActivityOwnedWindowV1(OperationId.Create(), new byte[320],
                new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1),
                new VoiceActivityMediaExtentV1(GraphGenerationId.Create(), 1, 2, true),
                new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 1)), default));
        Assert.Equal(VoiceActivityNoObservationReasonV1.SourceRevoked,
            Assert.IsType<VoiceActivitySourceOutcomeV1.NoObservation>(rejected.Outcome).Reason);
    }

    private static VoiceActivitySourceParticipantV1 Participant(Action created) =>
        new(Descriptor(), _ =>
        {
            created();
            return ValueTask.FromResult<VoiceActivitySourceProductV1>(
                new VoiceActivitySourceProductV1.BorrowedSynchronous(new Source(Capabilities())));
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

    private static VoiceActivitySourceCapabilitiesV1 Capabilities() => new(
        VoiceActivityInputOwnershipV1.BorrowedSynchronous,
        [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModelV1.Stateless, VoiceActivitySourceConcurrencyV1.Serial,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
        true, false, 1);

    private static VoiceActivitySourceCapabilitiesV1 TransferredCapabilities() => new(
        VoiceActivityInputOwnershipV1.IsolatedTransferred,
        [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.EngineScore,
            new BoundedAscii("measurement"), -1, 1, null),
        VoiceActivitySourceStateModelV1.StreamLocal, VoiceActivitySourceConcurrencyV1.ParallelWindows,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Sequenced,
        VoiceActivitySourceControlV1.Sequenced, VoiceActivitySourceControlV1.ReplacementRequired,
        true, false, 1);

    private sealed class Source(VoiceActivitySourceCapabilitiesV1 capabilities) :
        IBorrowedSynchronousVoiceActivitySourceV1, IDisposable
    {
        public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = capabilities;
        public int DisposeCalls { get; private set; }
        public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window) =>
            throw new NotSupportedException();
        public void Dispose() => DisposeCalls++;
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
            ValueTask.FromResult<VoiceActivitySettlementResultV1>(
                new VoiceActivitySettlementResultV1.NotFound(operationId));
    }
}
