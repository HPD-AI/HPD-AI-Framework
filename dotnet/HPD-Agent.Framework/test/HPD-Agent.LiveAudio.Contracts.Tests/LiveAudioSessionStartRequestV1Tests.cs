using HPD.Agent.Audio;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class LiveAudioSessionStartRequestV1Tests
{
    [Fact]
    public void Construction_is_inert_canonical_and_deterministic()
    {
        var fixture = CreateFixture();
        var first = fixture.CreateRequest(
            new LiveAudioParticipantSpecV1(new BoundedAscii("zeta"), OwnerSliceId.S11, false, Hash(9)),
            new LiveAudioParticipantSpecV1(new BoundedAscii("alpha"), OwnerSliceId.S2, true, Hash(8)));
        var second = fixture.CreateRequest(
            new LiveAudioParticipantSpecV1(new BoundedAscii("alpha"), OwnerSliceId.S2, true, Hash(8)),
            new LiveAudioParticipantSpecV1(new BoundedAscii("zeta"), OwnerSliceId.S11, false, Hash(9)));

        Assert.Equal(new[] { "alpha", "zeta" }, first.Participants.Select(item => item.FactoryKey.ToString()));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.GetCanonicalBytes(), second.GetCanonicalBytes());
    }

    [Fact]
    public void Fingerprint_changes_when_participant_configuration_changes()
    {
        var fixture = CreateFixture();
        var first = fixture.CreateRequest(new LiveAudioParticipantSpecV1(new BoundedAscii("media"), OwnerSliceId.S2, true, Hash(1)));
        var second = fixture.CreateRequest(new LiveAudioParticipantSpecV1(new BoundedAscii("media"), OwnerSliceId.S2, true, Hash(2)));
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Rejects_duplicate_participant_factory_keys()
    {
        var fixture = CreateFixture();
        Assert.Throws<ArgumentException>(() => fixture.CreateRequest(
            new LiveAudioParticipantSpecV1(new BoundedAscii("media"), OwnerSliceId.S2, true, Hash(1)),
            new LiveAudioParticipantSpecV1(new BoundedAscii("media"), OwnerSliceId.S3, false, Hash(2))));
    }

    [Fact]
    public void Rejects_more_than_thirty_two_participants()
    {
        var fixture = CreateFixture();
        var participants = Enumerable.Range(0, 33).Select(index =>
            new LiveAudioParticipantSpecV1(new BoundedAscii($"participant-{index:D2}"), OwnerSliceId.S2, true, Hash((byte)(index + 1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.CreateRequest(participants.ToArray()));
    }

    [Fact]
    public void Rejects_capacity_grant_for_another_operation()
    {
        var fixture = CreateFixture();
        var other = fixture with { Capacity = Capacity(fixture.Authority, Operation(2), fixture.Capacity.CurrentFact) };
        Assert.Throws<ArgumentException>(() => other.CreateRequest(Participant()));
    }

    [Fact]
    public void Rejects_capture_grant_for_another_authority_session()
    {
        var fixture = CreateFixture();
        var otherAuthority = ExpectedAuthorityVectorV1.Create(
            new SessionAuthorityStampV1(Runtime(2), Live(2)), Array.Empty<AuthorityAxisValueV1>());
        var other = fixture with { Capture = Capture(otherAuthority, new JournalPositionV1(otherAuthority.Session, 1)) };
        Assert.Throws<ArgumentException>(() => other.CreateRequest(Participant()));
    }

    [Fact]
    public void Rejects_invalid_deadline_and_concurrency_discriminator()
    {
        var fixture = CreateFixture();
        Assert.Throws<ArgumentException>(() => (fixture with { Deadline = default }).CreateRequest(Participant()));
        Assert.Throws<ArgumentException>(() => (fixture with { Concurrency = (LiveAudioConcurrencyModeV1)99 }).CreateRequest(Participant()));
    }

    [Fact]
    public void Rejects_nonactive_capture_proof()
    {
        var fixture = CreateFixture();
        var revoked = new CaptureGrantProofV1(CaptureGrant(1), Authorization(1), fixture.Capacity.CurrentFact,
            fixture.Authority, Hash(3), Hash(4), CaptureGrantStateV1.Revoked, new UtcInstant(long.MaxValue));
        Assert.Throws<ArgumentException>(() => (fixture with { Capture = revoked }).CreateRequest(Participant()));
    }

    [Fact]
    public void Request_owns_the_participant_collection()
    {
        var fixture = CreateFixture();
        var source = new List<LiveAudioParticipantSpecV1> { Participant() };
        var request = fixture.CreateRequest(source.ToArray());
        source.Clear();
        Assert.Single(request.Participants);
    }

    [Fact]
    public void Result_union_keeps_reservation_join_and_conflict_distinct()
    {
        var fixture = CreateFixture();
        LiveAudioSessionStartResultV1 reserved = new LiveAudioSessionStartResultV1.Reserved(fixture.Capacity.CurrentFact, Hash(1));
        LiveAudioSessionStartResultV1 joined = new LiveAudioSessionStartResultV1.Joined(fixture.Capacity.CurrentFact, Hash(1));
        LiveAudioSessionStartResultV1 conflict = new LiveAudioSessionStartResultV1.Conflict(fixture.Capacity.CurrentFact, Hash(2));
        Assert.IsType<LiveAudioSessionStartResultV1.Reserved>(reserved);
        Assert.IsType<LiveAudioSessionStartResultV1.Joined>(joined);
        Assert.IsType<LiveAudioSessionStartResultV1.Conflict>(conflict);
    }

    [Fact]
    public void Result_union_does_not_promote_rejection_or_unknown_to_reservation()
    {
        var fixture = CreateFixture();
        LiveAudioSessionStartResultV1 rejected = new LiveAudioSessionStartResultV1.Rejected(LiveAudioSessionStartRejectionV1.CaptureUnauthorized);
        LiveAudioSessionStartResultV1 unknown = new LiveAudioSessionStartResultV1.OutcomeUnknown(fixture.Operation, new BoundedAscii("journal-outcome-unknown"));
        Assert.IsNotType<LiveAudioSessionStartResultV1.Reserved>(rejected);
        Assert.IsNotType<LiveAudioSessionStartResultV1.Reserved>(unknown);
    }

    private static Fixture CreateFixture()
    {
        var operation = Operation(1);
        var authority = ExpectedAuthorityVectorV1.Create(
            new SessionAuthorityStampV1(Runtime(1), Live(1)), Array.Empty<AuthorityAxisValueV1>());
        var position = new JournalPositionV1(authority.Session, 1);
        return new Fixture(
            operation,
            new CorrelationEnvelopeV1(Tenant(1), operationId: operation),
            Plan(1),
            authority,
            Capacity(authority, operation, position),
            Capture(authority, position),
            LiveAudioConcurrencyModeV1.Exclusive,
            new MonotonicStampV1(Clock(1), Boot(1), 1_000));
    }

    private static CapacityGrantSnapshotV1 Capacity(ExpectedAuthorityVectorV1 authority, OperationId operation, JournalPositionV1 position) =>
        new(CapacityGrant(1), operation, authority, position, position, new CapacityGrantExpiryV1.NoExpiry(),
            CapacityGrantStateV1.Reserved, new CapacityChargeBalanceV1[] { null! });

    private static CaptureGrantProofV1 Capture(ExpectedAuthorityVectorV1 authority, JournalPositionV1 position) =>
        new(CaptureGrant(1), Authorization(1), position, authority, Hash(3), Hash(4), CaptureGrantStateV1.Active,
            new UtcInstant(long.MaxValue));

    private static LiveAudioParticipantSpecV1 Participant() =>
        new(new BoundedAscii("media"), OwnerSliceId.S2, true, Hash(1));

    private static Hash256 Hash(byte fill) { Assert.True(Hash256.TryCreate(Enumerable.Repeat(fill, 32).ToArray(), out var value)); return value; }
    private static OperationId Operation(int value) => Parse<OperationId>(OperationId.TryParse, "op", value);
    private static TenantId Tenant(int value) => Parse<TenantId>(TenantId.TryParse, "ten", value);
    private static RuntimeGenerationId Runtime(int value) => Parse<RuntimeGenerationId>(RuntimeGenerationId.TryParse, "run", value);
    private static LiveSessionId Live(int value) => Parse<LiveSessionId>(LiveSessionId.TryParse, "liv", value);
    private static LiveAudioPlanId Plan(int value) => Parse<LiveAudioPlanId>(LiveAudioPlanId.TryParse, "pln", value);
    private static CapacityGrantId CapacityGrant(int value) => Parse<CapacityGrantId>(CapacityGrantId.TryParse, "grt", value);
    private static CaptureGrantId CaptureGrant(int value) => Parse<CaptureGrantId>(CaptureGrantId.TryParse, "cgr", value);
    private static AuthorizationId Authorization(int value) => Parse<AuthorizationId>(AuthorizationId.TryParse, "aut", value);
    private static ClockDomainId Clock(int value) => Parse<ClockDomainId>(ClockDomainId.TryParse, "clk", value);
    private static BootId Boot(int value) => Parse<BootId>(BootId.TryParse, "boo", value);

    private delegate bool Parser<T>(string? text, out T value);
    private static T Parse<T>(Parser<T> parser, string family, int value)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var text = $"{family}:{new string('0', 25)}{alphabet[value]}";
        Assert.True(parser(text, out var result));
        return result;
    }

    private sealed record Fixture(
        OperationId Operation,
        CorrelationEnvelopeV1 Correlation,
        LiveAudioPlanId Plan,
        ExpectedAuthorityVectorV1 Authority,
        CapacityGrantSnapshotV1 Capacity,
        CaptureGrantProofV1 Capture,
        LiveAudioConcurrencyModeV1 Concurrency,
        MonotonicStampV1 Deadline)
    {
        internal LiveAudioSessionStartRequestV1 CreateRequest(params LiveAudioParticipantSpecV1[] participants) =>
            new(Operation, null, Correlation, Plan, Authority, Capacity, Capture, Concurrency, Deadline, participants);
    }
}
