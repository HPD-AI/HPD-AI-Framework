using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Agent.Runtime;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivitySessionReadinessCoordinatorV1Tests
{
    [Fact]
    public async Task Starts_every_participant_then_publishes_canonical_s1_readiness()
    {
        var fixture = await Fixture.CreateAsync();

        var ready = Assert.IsType<VoiceActivitySessionReadinessResultV1.Ready>(
            await fixture.Readiness.StartAndPublishAsync());

        Assert.False(ready.AlreadyCommitted);
        Assert.Equal(["prepare", "start"], fixture.Events);
        var read = Assert.IsType<SessionLifecycleSnapshotReadResultV1.Verified>(
            await SessionLifecycleSnapshotReaderV1.ReadAsync(fixture.Journal, fixture.Session));
        var current = Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(read.Fold);
        Assert.Equal(SessionLifecycleStateWireV1.Active, current.Snapshot!.State);
        Assert.Equal(SessionReadinessWireV1.Succeeded, current.Snapshot.Readiness);
    }

    [Fact]
    public async Task Participant_start_failure_never_writes_readiness()
    {
        var fixture = await Fixture.CreateAsync(RuntimeParticipantDispositionV1.Failed);

        var failed = Assert.IsType<VoiceActivitySessionReadinessResultV1.ParticipantStartFailed>(
            await fixture.Readiness.StartAndPublishAsync());

        Assert.Equal(RuntimeParticipantDispositionV1.Failed, failed.Result.Disposition);
        var read = Assert.IsType<SessionLifecycleSnapshotReadResultV1.Verified>(
            await SessionLifecycleSnapshotReaderV1.ReadAsync(fixture.Journal, fixture.Session));
        Assert.Equal(SessionReadinessWireV1.Unpublished,
            Assert.IsType<SessionLifecycleJournalFoldResultV1.Current>(read.Fold).Snapshot!.Readiness);
    }

    [Fact]
    public async Task Ambiguous_admission_preserves_started_participants_and_exact_retry_does_not_restart()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Script.FailReads = true;
        Assert.IsType<VoiceActivitySessionReadinessResultV1.Retryable>(
            await fixture.Readiness.StartAndPublishAsync());
        Assert.Equal(["prepare", "start"], fixture.Events);

        fixture.Script.FailReads = false;
        Assert.IsType<VoiceActivitySessionReadinessResultV1.Ready>(
            await fixture.Readiness.StartAndPublishAsync());
        Assert.Equal(["prepare", "start"], fixture.Events);
    }

    [Fact]
    public async Task Durable_readiness_rejection_is_not_reported_ready_and_terminates_started_participants()
    {
        var fixture = await Fixture.CreateAsync(wrongPredecessor: true);

        var failed = Assert.IsType<VoiceActivitySessionReadinessResultV1.AdmissionFailed>(
            await fixture.Readiness.StartAndPublishAsync());

        Assert.IsType<SessionLifecycleAdmissionResultV1.Committed>(failed.Admission);
        Assert.True(failed.Cleanup.IsSuccess);
        Assert.Equal(["prepare", "start", "terminate:StartFailed"], fixture.Events);
    }

    private sealed class Fixture
    {
        private Fixture(SessionAuthorityStampV1 session, InMemoryAuthorityJournalV1 journal,
            ToggleJournal script, RuntimeParticipantCoordinatorV1 participants,
            VoiceActivitySessionReadinessCoordinatorV1 readiness, List<string> events)
        { Session = session; Journal = journal; Script = script; Participants = participants; Readiness = readiness; Events = events; }

        internal SessionAuthorityStampV1 Session { get; }
        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal ToggleJournal Script { get; }
        internal RuntimeParticipantCoordinatorV1 Participants { get; }
        internal VoiceActivitySessionReadinessCoordinatorV1 Readiness { get; }
        internal List<string> Events { get; }

        internal static async Task<Fixture> CreateAsync(
            RuntimeParticipantDispositionV1 start = RuntimeParticipantDispositionV1.Succeeded,
            bool wrongPredecessor = false)
        {
            var session = new SessionAuthorityStampV1(RuntimeGenerationId.Create(), LiveSessionId.Create());
            var authority = ExpectedAuthorityVectorV1.Create(session, []);
            var journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new SessionLifecycleCommandPayloadRegistrationV1(), new SessionLifecycleFactPayloadRegistrationV1(),
            ]), () => new UtcInstant(100), new AuthorityJournalCapacityV1(2, 32, 1_000_000));
            var correlation = new CorrelationEnvelopeV1(TenantId.Create());
            var reserveBody = new SessionLifecycleCommandBodyV1.ReserveStarting(
                OperationId.Create(), Hash256.Compute("readiness-fixture"u8));
            var reserve = new SessionLifecycleCommandV1(
                session, authority, SessionLifecycleBodyCodecsV1.Encode(reserveBody));
            var reserved = Assert.IsType<SessionLifecycleAdmissionResultV1.Committed>(
                await SessionLifecycleAdmissionCoordinatorV1.AdmitAsync(
                    journal, reserve, correlation, new UtcInstant(1)));

            var events = new List<string>();
            var participant = new Participant(Descriptor(), events, start);
            var plan = RuntimeParticipantPlanV1.Compile([participant.Descriptor]);
            var participants = new RuntimeParticipantCoordinatorV1(plan, [participant]);
            Assert.True((await participants.PrepareAsync([
                new RuntimeParticipantAdmissionV1(participant.Descriptor.Id,
                    new RuntimeParticipantContextV1(ParticipantId.Create(), authority)),
            ])).IsSuccess);
            var script = new ToggleJournal(journal);
            var readiness = new VoiceActivitySessionReadinessCoordinatorV1(
                participants, script, session, authority, OperationId.Create(),
                wrongPredecessor ? reserved.Command.Position : reserved.Result.Position,
                correlation, new UtcInstant(2));
            return new Fixture(session, journal, script, participants, readiness, events);
        }
    }

    private sealed class ToggleJournal(IAuthorityJournalV1 inner) : IAuthorityJournalV1
    {
        internal bool FailReads { get; set; }
        public ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default) => inner.AppendAsync(request, cancellationToken);
        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default) => FailReads
            ? ValueTask.FromResult<ReadAuthorityRangeResultV1>(
                new ReadAuthorityRangeResultV1.StoreUnavailable(new BoundedAscii("readiness-store-unavailable")))
            : inner.ReadAsync(request, cancellationToken);
    }

    private sealed class Participant(RuntimeParticipantDescriptorV1 descriptor, List<string> events,
        RuntimeParticipantDispositionV1 start) : IRuntimeParticipantV1
    {
        public RuntimeParticipantDescriptorV1 Descriptor { get; } = descriptor;
        public ValueTask<RuntimeParticipantPrepareResultV1> PrepareAsync(
            RuntimeParticipantContextV1 context, CancellationToken cancellationToken)
        { events.Add("prepare"); return ValueTask.FromResult(new RuntimeParticipantPrepareResultV1(
            RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("prepared"),
            new RuntimePreparedHandleV1(Descriptor.Id, context))); }
        public ValueTask<RuntimeParticipantResultV1> StartAsync(
            RuntimePreparedHandleV1 handle, CancellationToken cancellationToken)
        { events.Add("start"); return ValueTask.FromResult(new RuntimeParticipantResultV1(start, new BoundedAscii("start"))); }
        public ValueTask<RuntimeParticipantResultV1> DrainAsync(RuntimeDrainIntentV1 intent,
            CancellationToken cancellationToken) => ValueTask.FromResult(new RuntimeParticipantResultV1(
                RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("drain")));
        public ValueTask<RuntimeParticipantResultV1> TerminateAsync(RuntimeTerminationCauseV1 cause,
            CancellationToken cancellationToken)
        { events.Add($"terminate:{cause}"); return ValueTask.FromResult(new RuntimeParticipantResultV1(
            RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("terminate"))); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static RuntimeParticipantDescriptorV1 Descriptor() => new(
        new BoundedAscii("voice-activity"), new BoundedAscii("S3"), new BoundedAscii("VoiceActivity"), [],
        AuthorityAxisId.Runtime, new DurationNs(5_000_000_000), new DurationNs(5_000_000_000),
        new DurationNs(5_000_000_000), new DurationNs(5_000_000_000), [new BoundedAscii("journal-bytes")]);
}
