using HPD.Agent.Audio;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class LiveAudioSessionReservationCoordinatorV1Tests
{
    [Fact]
    public async Task Reservation_admits_starting_and_identical_retry_joins()
    {
        var fixture = await Fixture.CreateAsync();
        var first = Assert.IsType<LiveAudioSessionStartResultV1.Reserved>(await fixture.ReserveAsync(fixture.Request));
        var retry = Assert.IsType<LiveAudioSessionStartResultV1.Joined>(await fixture.ReserveAsync(fixture.Request, utcNow: 101));
        Assert.Equal(first.Position, retry.Position);
        Assert.Equal(first.Fingerprint, retry.Fingerprint);
        var facts = await fixture.FactsAsync();
        Assert.Equal(5, facts.Count); // capacity, capture command/result, lifecycle command/result
    }

    [Fact]
    public async Task Changed_request_under_same_operation_is_conflict()
    {
        var fixture = await Fixture.CreateAsync(); await fixture.ReserveAsync(fixture.Request);
        var changed = fixture.CreateRequest(Hash(9));
        var conflict = Assert.IsType<LiveAudioSessionStartResultV1.Conflict>(await fixture.ReserveAsync(changed));
        Assert.NotEqual(changed.Fingerprint, conflict.ExistingFingerprint);
        Assert.Equal(4, conflict.ExistingPosition.Sequence);
    }

    [Fact]
    public async Task Expired_capture_is_rejected_before_lifecycle_append()
    {
        var fixture = await Fixture.CreateAsync();
        var rejected = Assert.IsType<LiveAudioSessionStartResultV1.Rejected>(await fixture.ReserveAsync(fixture.Request, utcNow: 900));
        Assert.Equal(LiveAudioSessionStartRejectionV1.CaptureUnauthorized, rejected.Reason);
        Assert.Equal(3, (await fixture.FactsAsync()).Count);
    }

    [Fact]
    public async Task Settled_capacity_is_rejected_before_lifecycle_append()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.ReleaseCapacityAsync();
        var rejected = Assert.IsType<LiveAudioSessionStartResultV1.Rejected>(await fixture.ReserveAsync(fixture.Request));
        Assert.Equal(LiveAudioSessionStartRejectionV1.CapacityUnavailable, rejected.Reason);
        Assert.Equal(4, (await fixture.FactsAsync()).Count);
    }

    [Fact]
    public async Task Reached_terminal_deadline_reads_or_appends_nothing()
    {
        var fixture = await Fixture.CreateAsync();
        var before = (await fixture.FactsAsync()).Count;
        var rejected = Assert.IsType<LiveAudioSessionStartResultV1.Rejected>(await fixture.ReserveAsync(fixture.Request, monotonicNow: 1_000));
        Assert.Equal(LiveAudioSessionStartRejectionV1.DeadlineReached, rejected.Reason);
        Assert.Equal(before, (await fixture.FactsAsync()).Count);
    }

    private sealed class Fixture
    {
        private readonly TenantId _tenant = TenantId.Create();
        private readonly ClockDomainId _clock = ClockDomainId.Create();
        private readonly BootId _boot = BootId.Create();
        private readonly CapacityRequestV1 _capacityRequest;
        private Fixture()
        {
            Session = new(RuntimeGenerationId.Create(), LiveSessionId.Create()); Operation = OperationId.Create();
            Authority = ExpectedAuthorityVectorV1.Create(Session, []);
            Journal = new InMemoryAuthorityJournalV1(new AuthorityPayloadAdmissionRegistryV1([
                new CapacityReservationPayloadRegistrationV1(), new CapacitySettlementPayloadRegistrationV1(),
                new CaptureAuthorizationPayloadRegistrationV1(), new CaptureGrantCommittedPayloadRegistrationV1(),
                new SessionLifecycleCommandPayloadRegistrationV1(), new SessionLifecycleFactPayloadRegistrationV1()]),
                () => new UtcInstant(1), new AuthorityJournalCapacityV1(8, 64, 2_000_000));
            _capacityRequest = new CapacityRequestV1(Operation, Authority,
                [new CapacityChargeV1(CapacityDimensionsV1.QueueItems,
                    new CapacityScopeV1(_tenant, null, new CapacitySubjectV1.Operation(Operation)), 1,
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
            var capacity = Assert.IsType<CapacityAdmissionResultV1.Granted>(await CapacityAdmissionCoordinatorV1.ReserveAsync(
                fixture.Journal, fixture._capacityRequest, new CapacityGrantExpiryV1.NoExpiry(), fixture.Correlation(), fixture.Stamp(10), new UtcInstant(10)));
            var command = new CaptureAuthorizationCommandV1(fixture.Session, fixture.Authority,
                new CaptureAuthorizationBodyV1(fixture.Operation, CaptureGrantId.Create(), AuthorizationId.Create(), Hash(3), Hash(4), new UtcInstant(900)));
            var capture = Assert.IsType<CaptureGrantAdmissionResultV1.Granted>(await CaptureGrantAdmissionV1.AuthorizeAsync(
                fixture.Journal, command, fixture.Correlation(), new UtcInstant(20)));
            fixture.Request = fixture.CreateRequest(Hash(1), capacity.Grant, capture.Proof);
            return fixture;
        }

        internal LiveAudioSessionStartRequestV1 CreateRequest(Hash256 configurationHash) =>
            CreateRequest(configurationHash, Request.CapacityGrant, Request.CaptureGrant);
        private LiveAudioSessionStartRequestV1 CreateRequest(Hash256 configurationHash, CapacityGrantSnapshotV1 capacity, CaptureGrantProofV1 capture) =>
            new(Operation, null, Correlation(), LiveAudioPlanId.Create(), Authority, capacity, capture,
                LiveAudioConcurrencyModeV1.Exclusive, Stamp(1_000),
                [new LiveAudioParticipantSpecV1(new BoundedAscii("media"), OwnerSliceId.S2, true, configurationHash)]);
        internal ValueTask<LiveAudioSessionStartResultV1> ReserveAsync(LiveAudioSessionStartRequestV1 request, ulong monotonicNow = 100, long utcNow = 100) =>
            LiveAudioSessionReservationCoordinatorV1.ReserveAsync(Journal, request, Stamp(monotonicNow), new UtcInstant(utcNow));
        internal async Task ReleaseCapacityAsync()
        {
            var charge = _capacityRequest.Charges.Single();
            var body = new CapacitySettlementFactBodyV1(Request.CapacityGrant.GrantId, OperationId.Create(), Request.CapacityGrant.CurrentFact,
                CapacitySettlementKindV1.Released,
                [new CapacitySettlementChargeV1(charge.DimensionId, charge.Scope, charge.Purpose, charge.Amount)], Stamp(50));
            Assert.IsType<CapacityAdmissionResultV1.Settled>(await CapacityAdmissionCoordinatorV1.SettleAsync(
                Journal, Session, body, new CorrelationEnvelopeV1(_tenant, operationId: body.OperationId), new UtcInstant(50)));
        }
        internal async Task<IReadOnlyList<AuthorityFactEnvelopeV1>> FactsAsync() =>
            Assert.IsType<ReadAuthorityRangeResultV1.Batch>(await Journal.ReadAsync(
                new ReadAuthorityRangeV1(Session, 0, long.MaxValue, 32, 1_048_576))).Facts;
        private CorrelationEnvelopeV1 Correlation() => new(_tenant, operationId: Operation);
        private MonotonicStampV1 Stamp(ulong value) => new(_clock, _boot, value);
    }

    private static Hash256 Hash(byte value) => Hash256.FromBytes(Enumerable.Repeat(value, 32).ToArray());
}
