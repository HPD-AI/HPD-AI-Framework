using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests;

public sealed class LiveAudioSessionFailureConvergenceTests
{
    [Fact]
    public void Failure_operation_ids_are_deterministic_and_domain_separated()
    {
        var request = GoldenRequest();
        var position = new JournalPositionV1(request.ExpectedAuthority.Session, 7);
        var first = LiveAudioSessionFailureOperationIds.Derive(request, position);
        var second = LiveAudioSessionFailureOperationIds.Derive(request, position);
        Assert.Equal(first, second);
        Assert.Equal(3, new[] { first.Begin, first.Advance, first.Complete }.Distinct().Count());
        Assert.Equal("op:0HGDNPYA054SE3RA7E7V617R02", first.Begin.ToString());
        Assert.Equal("op:2WXRZ46EZE8YDZYKTBW7S9SXGX", first.Advance.ToString());
        Assert.Equal("op:246ZJ5AYD54Z7B89YX49YQTPNW", first.Complete.ToString());
    }

    [Fact]
    public async Task Clean_abandoned_starting_converges_exact_chain_and_retry_does_not_grow()
    {
        var fixture = await LiveAudioSessionPreparationSupervisorTests.Fixture.CreateAsync();
        var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalog.CreateExplicit([new RefusingFactory(calls)]);
        var abandoned = Assert.IsType<LiveAudioSessionPreparationResult.ReservedNeedsConvergence>(
            await fixture.PrepareAsync(catalog));
        var before = (await fixture.FactsAsync()).Count;
        var completed = Assert.IsType<LiveAudioSessionFailureConvergenceResult.Completed>(
            await LiveAudioSessionFailureConvergence.ConvergeAsync(
                fixture.Journal, abandoned.Abandonment, new UtcInstant(200)));
        Assert.True(completed.BeginResult.Sequence < completed.AdvanceResult.Sequence);
        Assert.True(completed.AdvanceResult.Sequence < completed.CompleteResult.Sequence);
        Assert.Equal(before + 6, (await fixture.FactsAsync()).Count);
        var retry = Assert.IsType<LiveAudioSessionFailureConvergenceResult.AlreadyCompleted>(
            await LiveAudioSessionFailureConvergence.ConvergeAsync(
                fixture.Journal, abandoned.Abandonment, new UtcInstant(201)));
        Assert.Equal(completed.CompleteResult, retry.CompleteResult);
        Assert.Equal(before + 6, (await fixture.FactsAsync()).Count);
    }

    [Fact]
    public void Clean_abandonment_capability_has_no_callable_constructor()
    {
        Assert.Empty(typeof(LiveAudioSessionPreparationSupervisor.CleanAbandonment).GetConstructors());
        var request = GoldenRequest();
        Assert.Throws<ArgumentException>(() => new LiveAudioSessionPreparationSupervisor.CleanAbandonment(
            new object(), request, new JournalPositionV1(request.ExpectedAuthority.Session, 1), new BoundedAscii("forged")));
    }

    [Fact]
    public async Task Concurrent_convergers_share_one_deterministic_chain()
    {
        var fixture = await LiveAudioSessionPreparationSupervisorTests.Fixture.CreateAsync();
        var abandoned = Assert.IsType<LiveAudioSessionPreparationResult.ReservedNeedsConvergence>(
            await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalog.CreateExplicit([new RefusingFactory([])])));
        var before = (await fixture.FactsAsync()).Count;
        var first = LiveAudioSessionFailureConvergence.ConvergeAsync(
            fixture.Journal, abandoned.Abandonment, new UtcInstant(200)).AsTask();
        var second = LiveAudioSessionFailureConvergence.ConvergeAsync(
            fixture.Journal, abandoned.Abandonment, new UtcInstant(200)).AsTask();
        var results = await Task.WhenAll(first, second);
        Assert.All(results, result => Assert.True(result is LiveAudioSessionFailureConvergenceResult.Completed
            or LiveAudioSessionFailureConvergenceResult.AlreadyCompleted));
        Assert.Equal(before + 6, (await fixture.FactsAsync()).Count);
    }

    [Fact]
    public async Task Unrecorded_stage_uses_verified_current_owner_axis()
    {
        var fixture = await LiveAudioSessionPreparationSupervisorTests.Fixture.CreateAsync();
        var abandoned = Assert.IsType<LiveAudioSessionPreparationResult.ReservedNeedsConvergence>(
            await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalog.CreateExplicit([new RefusingFactory([])])));
        await fixture.AdvanceGraphAsync();
        Assert.IsType<LiveAudioSessionFailureConvergenceResult.Completed>(
            await LiveAudioSessionFailureConvergence.ConvergeAsync(
                fixture.Journal, abandoned.Abandonment, new UtcInstant(200)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Crash_after_each_committed_command_or_result_recovers_the_same_chain(int crashAfterAppend)
    {
        var fixture = await LiveAudioSessionPreparationSupervisorTests.Fixture.CreateAsync();
        var abandoned = Assert.IsType<LiveAudioSessionPreparationResult.ReservedNeedsConvergence>(
            await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalog.CreateExplicit([new RefusingFactory([])])));
        var before = (await fixture.FactsAsync()).Count;
        var faulting = new CommitThenThrowJournal(fixture.Journal, crashAfterAppend);

        Assert.IsType<LiveAudioSessionFailureConvergenceResult.OutcomeUnknown>(
            await LiveAudioSessionFailureConvergence.ConvergeAsync(
                faulting, abandoned.Abandonment, new UtcInstant(200)));
        var recovered = await LiveAudioSessionFailureConvergence.ConvergeAsync(
            fixture.Journal, abandoned.Abandonment, new UtcInstant(201));
        Assert.True(recovered is LiveAudioSessionFailureConvergenceResult.Completed
            or LiveAudioSessionFailureConvergenceResult.AlreadyCompleted);
        Assert.Equal(before + 6, (await fixture.FactsAsync()).Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Failure_before_each_command_or_result_append_recovers_without_a_phantom_fact(int failBeforeAppend)
    {
        var fixture = await LiveAudioSessionPreparationSupervisorTests.Fixture.CreateAsync();
        var abandoned = Assert.IsType<LiveAudioSessionPreparationResult.ReservedNeedsConvergence>(
            await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalog.CreateExplicit([new RefusingFactory([])])));
        var before = (await fixture.FactsAsync()).Count;
        var faulting = new ThrowBeforeAppendJournal(fixture.Journal, failBeforeAppend);

        Assert.IsType<LiveAudioSessionFailureConvergenceResult.OutcomeUnknown>(
            await LiveAudioSessionFailureConvergence.ConvergeAsync(
                faulting, abandoned.Abandonment, new UtcInstant(200)));
        Assert.Equal(before + failBeforeAppend - 1, (await fixture.FactsAsync()).Count);

        var recovered = await LiveAudioSessionFailureConvergence.ConvergeAsync(
            fixture.Journal, abandoned.Abandonment, new UtcInstant(201));
        Assert.True(recovered is LiveAudioSessionFailureConvergenceResult.Completed
            or LiveAudioSessionFailureConvergenceResult.AlreadyCompleted);
        Assert.Equal(before + 6, (await fixture.FactsAsync()).Count);
    }

    private sealed class RefusingFactory(List<string> calls) : ILiveAudioParticipantFactory
    {
        public LiveAudioParticipantDescriptor Descriptor { get; } = new(
            new BoundedAscii("media"), OwnerSliceId.S2, AuthorityAxisId.Graph, [], [CapacityDimensionsV1.QueueItems],
            new DurationNs(1_000_000), new DurationNs(1_000_000), new DurationNs(1_000_000));
        public ValueTask<LiveAudioParticipantFactoryResult> PrepareAsync(
            LiveAudioParticipantPreparationContext context, CancellationToken cancellationToken = default)
        { calls.Add("prepare:media"); return ValueTask.FromResult<LiveAudioParticipantFactoryResult>(
            new LiveAudioParticipantFactoryResult.Refused(new BoundedAscii("fixture-refused"))); }
    }

    private sealed class CommitThenThrowJournal(IAuthorityJournalV1 inner, int throwAfterAppend) : IAuthorityJournalV1
    {
        private int _appendCount;

        public async ValueTask<AppendAuthorityResultV1> AppendAsync(
            AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default)
        {
            var result = await inner.AppendAsync(request, cancellationToken);
            if (Interlocked.Increment(ref _appendCount) == throwAfterAppend)
            {
                Assert.IsType<AppendAuthorityResultV1.Committed>(result);
                throw new IOException("fixture crash after durable append");
            }
            return result;
        }

        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(
            ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(request, cancellationToken);
    }

    private sealed class ThrowBeforeAppendJournal(IAuthorityJournalV1 inner, int throwBeforeAppend) : IAuthorityJournalV1
    {
        private int _appendCount;

        public ValueTask<AppendAuthorityResultV1> AppendAsync(
            AppendAuthorityBatchV1 request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _appendCount) == throwBeforeAppend)
                throw new IOException("fixture failure before append");
            return inner.AppendAsync(request, cancellationToken);
        }

        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(
            ReadAuthorityRangeV1 request, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(request, cancellationToken);
    }

    private static LiveAudioSessionStartRequest GoldenRequest()
    {
        var session = new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(17)));
        var operation = OperationId.FromValue(Id(33));
        var authority = ExpectedAuthorityVectorV1.Create(session,
            [new AuthorityAxisValueV1.Graph(GraphGenerationId.FromValue(Id(49)))]);
        var position = new JournalPositionV1(session, 1);
        var capacity = new CapacityGrantSnapshotV1(CapacityGrantId.FromValue(Id(65)), operation, authority, position, position,
            new CapacityGrantExpiryV1.NoExpiry(), CapacityGrantStateV1.Reserved, [null!]);
        var capture = new CaptureGrantProofV1(CaptureGrantId.FromValue(Id(81)), AuthorizationId.FromValue(Id(97)), position,
            authority, Hash(3), Hash(4), CaptureGrantStateV1.Active, new UtcInstant(1_000));
        return new LiveAudioSessionStartRequest(operation, null,
            new CorrelationEnvelopeV1(TenantId.FromValue(Id(113)), operationId: operation),
            LiveAudioPlanId.FromValue(Id(129)), authority, capacity, capture, LiveAudioConcurrencyMode.Exclusive,
            new MonotonicStampV1(ClockDomainId.FromValue(Id(145)), BootId.FromValue(Id(161)), 1_000),
            [new LiveAudioParticipantSpec(new BoundedAscii("media"), OwnerSliceId.S2, true, Hash(5))]);
    }

    private static StableId128 Id(byte seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = unchecked((byte)(seed + index));
        return StableId128.FromBytes(bytes);
    }

    private static Hash256 Hash(byte value) => Hash256.FromBytes(Enumerable.Repeat(value, 32).ToArray());
}
