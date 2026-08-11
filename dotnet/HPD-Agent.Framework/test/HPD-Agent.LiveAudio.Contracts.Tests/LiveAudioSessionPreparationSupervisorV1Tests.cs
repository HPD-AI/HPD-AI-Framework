using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.Audio.Tests;

public sealed class LiveAudioSessionPreparationSupervisorV1Tests
{
    [Fact]
    public async Task Compiles_before_reservation_and_invalid_plan_writes_or_prepares_nothing()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var before = (await fixture.FactsAsync()).Count;
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, calls, dependencies: [new BoundedAscii("provider")]),
            new Factory("provider", OwnerSliceId.S2, calls)]);
        var result = await fixture.PrepareAsync(catalog);
        Assert.IsType<LiveAudioSessionPreparationResultV1.Rejected>(result);
        Assert.Equal(before, (await fixture.FactsAsync()).Count);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Reservation_rejection_invokes_no_factory()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var result = await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, calls)]), reservationUtc: 900, acquisitionUtc: 900);
        var rejected = Assert.IsType<LiveAudioSessionPreparationResultV1.Rejected>(result);
        Assert.Equal(LiveAudioSessionStartRejectionV1.CaptureUnauthorized, rejected.Reason);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Acquisition_cut_rereads_deadline_and_requires_convergence_without_preparation()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var result = await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, calls)]), acquisitionMonotonic: 1_000);
        var convergence = Assert.IsType<LiveAudioSessionPreparationResultV1.ReservedNeedsConvergence>(result);
        Assert.Equal("acquisition-proof-stale", convergence.SafeCode.ToString());
        Assert.True(convergence.ReservationPosition.IsValid);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Prepared_session_owns_handles_and_unwinds_once_in_reverse()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, calls),
            new Factory("activity", OwnerSliceId.S3, calls, dependencies: [new BoundedAscii("media")])]);
        var prepared = Assert.IsType<LiveAudioSessionPreparationResultV1.Prepared>(await fixture.PrepareAsync(catalog, includeActivity: true));
        Assert.Equal(["prepare:media", "prepare:activity"], calls);
        Assert.Equal(2, prepared.Session.Participants.Count);
        var mutableView = Assert.IsAssignableFrom<IList<ILiveAudioPreparedParticipantV1>>(prepared.Session.Participants);
        Assert.Throws<NotSupportedException>(() => mutableView[0] = mutableView[1]);
        Assert.DoesNotContain(prepared.Session.GetType().GetMethods(), method =>
            method.Name.Contains("Start", StringComparison.Ordinal) || method.Name.Contains("Ready", StringComparison.Ordinal));
        var firstUnwind = prepared.Session.UnwindAsync().AsTask();
        var secondUnwind = prepared.Session.UnwindAsync().AsTask();
        var unwind = await Task.WhenAll(firstUnwind, secondUnwind);
        Assert.All(unwind, value => Assert.IsType<LiveAudioPreparedSessionUnwindResultV1.Clean>(value));
        Assert.Equal(["prepare:media", "prepare:activity", "dispose:activity", "dispose:media"], calls);
    }

    [Fact]
    public async Task Required_refusal_after_reservation_is_not_reported_as_rejection()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var result = await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, calls, refuse: true)]));
        var convergence = Assert.IsType<LiveAudioSessionPreparationResultV1.ReservedNeedsConvergence>(result);
        Assert.Equal("fixture-refused", convergence.SafeCode.ToString());
        Assert.Equal(["prepare:media"], calls);
        Assert.Equal(7, (await fixture.FactsAsync()).Count);
    }

    [Fact]
    public async Task Joined_retry_never_prepares_a_second_owner()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([new Factory("media", OwnerSliceId.S2, calls)]);
        var first = Assert.IsType<LiveAudioSessionPreparationResultV1.Prepared>(await fixture.PrepareAsync(catalog));
        var retry = Assert.IsType<LiveAudioSessionPreparationResultV1.JoinedExisting>(await fixture.PrepareAsync(catalog));
        Assert.Equal(first.Session.ReservationPosition, retry.ReservationPosition);
        Assert.Equal(fixture.Request.Fingerprint, retry.RequestFingerprint);
        Assert.Equal(["prepare:media"], calls);
        await first.Session.UnwindAsync();
    }

    [Fact]
    public async Task Rejects_reversed_or_incomparable_acquisition_observations_before_mutation()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var catalog = LiveAudioParticipantFactoryCatalogV1.CreateExplicit([new Factory("media", OwnerSliceId.S2, calls)]);
        var before = (await fixture.FactsAsync()).Count;
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.PrepareAsync(catalog,
            reservationMonotonic: 100, acquisitionMonotonic: 99).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.PrepareAsync(catalog,
            reservationUtc: 100, acquisitionUtc: 99).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => LiveAudioSessionPreparationSupervisorV1.PrepareAsync(
            fixture.Journal, fixture.Request, catalog, fixture.Stamp(100), new UtcInstant(100),
            new MonotonicStampV1(ClockDomainId.Create(), BootId.Create(), 101), new UtcInstant(101)).AsTask());
        Assert.Equal(before, (await fixture.FactsAsync()).Count);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Store_exceptions_remain_inside_the_closed_result_algebra()
    {
        var reservationFixture = await Fixture.CreateAsync(); var reservationCalls = new List<string>();
        var reservationFault = new FaultingJournal(reservationFixture.Journal, throwAppendAt: 1);
        var reservation = await reservationFixture.PrepareAsync(LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, reservationCalls)]), journal: reservationFault);
        var reservationUnknown = Assert.IsType<LiveAudioSessionPreparationResultV1.OutcomeUnknown>(reservation);
        Assert.Null(reservationUnknown.ReservationPosition);
        Assert.Equal("append-outcome-unknown", reservationUnknown.SafeCode.ToString());
        Assert.Empty(reservationCalls);

        var acquisitionFixture = await Fixture.CreateAsync(); var acquisitionCalls = new List<string>();
        var acquisitionFault = new FaultingJournal(acquisitionFixture.Journal, throwReadsAfterAppendCount: 2);
        var acquisition = await acquisitionFixture.PrepareAsync(LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, acquisitionCalls)]), journal: acquisitionFault);
        var acquisitionUnknown = Assert.IsType<LiveAudioSessionPreparationResultV1.OutcomeUnknown>(acquisition);
        Assert.True(acquisitionUnknown.ReservationPosition?.IsValid);
        Assert.Equal("capacity-snapshot-unknown", acquisitionUnknown.SafeCode.ToString());
        Assert.Empty(acquisitionCalls);
    }

    [Fact]
    public async Task Caller_cancellation_never_escapes_or_invokes_a_factory()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await fixture.PrepareAsync(LiveAudioParticipantFactoryCatalogV1.CreateExplicit([
            new Factory("media", OwnerSliceId.S2, calls)]), cancellationToken: cancellation.Token);
        Assert.IsType<LiveAudioSessionPreparationResultV1.OutcomeUnknown>(result);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Prepared_session_reports_unknown_for_bounded_hanging_unwind()
    {
        var fixture = await Fixture.CreateAsync(); var calls = new List<string>();
        var prepared = Assert.IsType<LiveAudioSessionPreparationResultV1.Prepared>(await fixture.PrepareAsync(
            LiveAudioParticipantFactoryCatalogV1.CreateExplicit([new Factory("media", OwnerSliceId.S2, calls, hangDispose: true)])));
        var result = await prepared.Session.UnwindAsync();
        Assert.IsType<LiveAudioPreparedSessionUnwindResultV1.OutcomeUnknown>(result);
        Assert.Equal(["prepare:media", "dispose:media"], calls);
    }

    private sealed class Factory(string key, OwnerSliceId owner, List<string> calls,
        BoundedAscii[]? dependencies = null, bool refuse = false, bool hangDispose = false) : ILiveAudioParticipantFactoryV1
    {
        public LiveAudioParticipantDescriptorV1 Descriptor { get; } = new(
            new BoundedAscii(key), owner, owner == OwnerSliceId.S2 ? AuthorityAxisId.Graph : AuthorityAxisId.Activity,
            dependencies ?? [], [CapacityDimensionsV1.QueueItems], new DurationNs(1_000_000_000),
            new DurationNs(1_000_000_000), new DurationNs(1_000_000_000));

        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(
            LiveAudioParticipantPreparationContextV1 context, CancellationToken cancellationToken = default)
        {
            calls.Add($"prepare:{key}");
            if (refuse) return ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
                new LiveAudioParticipantFactoryResultV1.Refused(new BoundedAscii("fixture-refused")));
            return ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
                new LiveAudioParticipantFactoryResultV1.Prepared(new Participant(key, owner, calls, hangDispose)));
        }
    }

    private sealed class Participant(string key, OwnerSliceId owner, List<string> calls, bool hangDispose) : ILiveAudioPreparedParticipantV1
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ParticipantId ParticipantId { get; } = ParticipantId.Create();
        public BoundedAscii FactoryKey { get; } = new(key);
        public OwnerSliceId Owner { get; } = owner;
        public ValueTask DisposeAsync() { calls.Add($"dispose:{key}"); return hangDispose ? new ValueTask(_never.Task) : ValueTask.CompletedTask; }
    }

    internal sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        private readonly ClockDomainId _clock = ClockDomainId.Create();
        private readonly BootId _boot = BootId.Create();
        private readonly CapacityRequestV1 _capacityRequest;

        private Fixture()
        {
            Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create()); Operation = OperationId.Create();
            Authority = ExpectedAuthorityVectorV1.Create(Session,
            [
                new AuthorityAxisValueV1.Graph(GraphGenerationId.Create()),
                new AuthorityAxisValueV1.Activity(ActivityGenerationId.Create()),
            ]);
            Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Graph),
                new AuthorityGenerationInitializationPayloadRegistrationV1(AuthorityAxisId.Activity),
                new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Graph),
                new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1(),
                new CaptureAuthorizationPayloadRegistrationV1(), new CaptureGrantCommittedPayloadRegistrationV1(),
                new SessionLifecycleCommandPayloadRegistrationV1(), new SessionLifecycleFactPayloadRegistrationV1()]),
                () => new UtcInstant(1), new AuthorityJournalCapacityV1(8, 64, 2_000_000));
            _capacityRequest = new CapacityRequestV1(Operation, Authority,
                [new CapacityChargeV1(CapacityDimensionsV1.QueueItems,
                    new CapacityScopeV1(_tenant, null, new CapacitySubjectV1.Operation(Operation)), 2,
                    CapacityPurposeId.Create(), new CapacityChargeWindowV1.NoWindow())], Stamp(1_000), CapacityPriorityV1.Normal);
        }

        internal SessionAuthorityStampV1 Session { get; }
        internal OperationId Operation { get; }
        internal ExpectedAuthorityVectorV1 Authority { get; }
        internal InMemoryAuthorityJournalV1 Journal { get; }
        internal LiveAudioSessionStartRequestV1 Request { get; private set; } = null!;

        internal static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var initializations = fixture.Authority.Axes.Select(axis => fixture.Initialization(axis.Value)).ToArray();
            Assert.IsType<AppendAuthorityResultV1.Committed>(await fixture.Journal.AppendAsync(
                new AppendAuthorityBatchV1(fixture.Session, 0, [], initializations, 16_384)));
            var capacity = Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(
                fixture.Journal, fixture._capacityRequest, new CapacityGrantExpiryV1.NoExpiry(), fixture.Correlation(),
                fixture.Stamp(10), new UtcInstant(10)));
            var command = new CaptureAuthorizationCommandV1(fixture.Session, fixture.Authority,
                new CaptureAuthorizationBodyV1(fixture.Operation, CaptureGrantId.Create(), AuthorizationId.Create(),
                    Hash(3), Hash(4), new UtcInstant(900)));
            var capture = Assert.IsType<CaptureGrantAdmissionResultV1.Granted>(await CaptureGrantAdmissionV1.AuthorizeAsync(
                fixture.Journal, command, fixture.Correlation(), new UtcInstant(20)));
            fixture.Request = fixture.MakeRequest(capacity.Grant, capture.Proof, false);
            return fixture;
        }

        internal ValueTask<LiveAudioSessionPreparationResultV1> PrepareAsync(LiveAudioParticipantFactoryCatalogV1 catalog,
            ulong reservationMonotonic = 100, long reservationUtc = 100, ulong acquisitionMonotonic = 101,
            long acquisitionUtc = 101, bool includeActivity = false, IAuthorityJournalV1? journal = null,
            CancellationToken cancellationToken = default)
        {
            if (includeActivity) Request = MakeRequest(Request.CapacityGrant, Request.CaptureGrant, true);
            return LiveAudioSessionPreparationSupervisorV1.PrepareAsync(journal ?? Journal, Request, catalog,
                Stamp(reservationMonotonic), new UtcInstant(reservationUtc), Stamp(acquisitionMonotonic),
                new UtcInstant(acquisitionUtc), cancellationToken);
        }

        internal async Task<IReadOnlyList<AuthorityFactEnvelopeV1>> FactsAsync() =>
            Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await Journal.ReadAsync(
                new ReadAuthorityRangeV1(Session, 0, long.MaxValue, 32, 1_048_576))).Facts;

        internal async Task AdvanceGraphAsync()
        {
            var old = Assert.IsType<AuthorityAxisValueV1.Graph>(Authority.Axes.Single(value =>
                value.AxisId == AuthorityAxisId.Graph).Value).Value;
            var next = GraphGenerationId.Create();
            Span<byte> oldBytes = stackalloc byte[16]; Span<byte> nextBytes = stackalloc byte[16];
            Assert.True(old.TryWriteBytes(oldBytes)); Assert.True(next.TryWriteBytes(nextBytes));
            var registration = new AuthorityGenerationTransitionPayloadRegistrationV1(AuthorityAxisId.Graph);
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(4);
            writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, Session);
            writer.WriteUInt64(2); writer.WriteByteString(oldBytes);
            writer.WriteUInt64(3); writer.WriteByteString(nextBytes);
            writer.WriteUInt64(4); writer.WriteUInt64((ushort)registration.Owner);
            writer.WriteEndMap(); var payload = writer.Encode(); var facts = await FactsAsync();
            var proposal = new ProposedAuthorityFactV1(JournalFactId.Create(), null, registration.Owner, registration.Schema,
                payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
                Correlation(), new UtcInstant(150));
            Assert.IsType<AppendAuthorityResultV1.Committed>(await Journal.AppendAsync(
                new AppendAuthorityBatchV1(Session, facts.Count, [], [proposal], 16_384)));
        }

        private LiveAudioSessionStartRequestV1 MakeRequest(CapacityGrantSnapshotV1 capacity,
            CaptureGrantProofV1 capture, bool includeActivity) => new(Operation, null, Correlation(), LiveAudioPlanId.Create(),
            Authority, capacity, capture, LiveAudioConcurrencyModeV1.Exclusive, Stamp(1_000), includeActivity
                ? [new LiveAudioParticipantSpecV1(new BoundedAscii("media"), OwnerSliceId.S2, true, Hash(1)),
                   new LiveAudioParticipantSpecV1(new BoundedAscii("activity"), OwnerSliceId.S3, true, Hash(2))]
                : [new LiveAudioParticipantSpecV1(new BoundedAscii("media"), OwnerSliceId.S2, true, Hash(1))]);

        private CorrelationEnvelopeV1 Correlation() => new(_tenant, operationId: Operation);
        internal MonotonicStampV1 Stamp(ulong value) => new(_clock, _boot, value);

        private ProposedAuthorityFactV1 Initialization(AuthorityAxisValueV1 axis)
        {
            var registration = new AuthorityGenerationInitializationPayloadRegistrationV1(axis.AxisId);
            Span<byte> value = stackalloc byte[16];
            Assert.True(axis switch
            {
                AuthorityAxisValueV1.Graph graph => graph.Value.TryWriteBytes(value),
                AuthorityAxisValueV1.Activity activity => activity.Value.TryWriteBytes(value),
                _ => false,
            });
            var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
            writer.WriteStartMap(3);
            writer.WriteUInt64(1); SessionAuthorityStampV1Codec.Write(writer, Session);
            writer.WriteUInt64(2); writer.WriteByteString(value);
            writer.WriteUInt64(3); writer.WriteUInt64((ushort)registration.Owner);
            writer.WriteEndMap(); var payload = writer.Encode();
            return new ProposedAuthorityFactV1(JournalFactId.Create(), null, registration.Owner, registration.Schema,
                payload, AuthorityPayloadHashV1.Compute(registration.SchemaToken, registration.Schema, payload),
                Correlation(), new UtcInstant(1));
        }
    }

    private sealed class FaultingJournal(IAuthorityJournalV1 inner, int? throwAppendAt = null,
        int? throwReadsAfterAppendCount = null) : IAuthorityJournalV1
    {
        private int _appendCount;

        public async ValueTask<AppendAuthorityResultV1> AppendAsync(AppendAuthorityBatchV1 request,
            CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _appendCount);
            if (throwAppendAt == count) throw new IOException("fixture append fault");
            return await inner.AppendAsync(request, cancellationToken);
        }

        public ValueTask<ReadAuthorityRangeResultV1> ReadAsync(ReadAuthorityRangeV1 request,
            CancellationToken cancellationToken = default)
        {
            if (throwReadsAfterAppendCount is { } threshold && Volatile.Read(ref _appendCount) >= threshold)
                throw new IOException("fixture read fault");
            return inner.ReadAsync(request, cancellationToken);
        }
    }

    private static Hash256 Hash(byte value) => Hash256.FromBytes(Enumerable.Repeat(value, 32).ToArray());
}
