using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.Tests.Authority;

public sealed class CapacityLedgerContractsV1Tests
{
    [Fact]
    public void FactIdentities_MatchTheFrozenGoldens()
    {
        var operation = Id<OperationId>(0);
        var grant = CapacityGrantIdDerivationV1.Derive(operation);
        var settlementOperation = Id<OperationId>(16);

        Assert.Equal("grt:7C2J4Q6RZSABAD69M7MSXV7BTH", grant.ToString());
        Assert.Equal("fct:5XNK8DSHX8NKWXTYG0AN6JZHNV", CapacityFactIdsV1.Reservation(grant).ToString());
        Assert.Equal("fct:63JWDJQBDZW0FQH5NRQWQQJZPV", CapacityFactIdsV1.Settlement(grant, settlementOperation).ToString());
    }

    [Fact]
    public void Reservation_RequiresDerivedGrantAndComparableLaterExpiry()
    {
        var request = Request();
        var grant = CapacityGrantIdDerivationV1.Derive(request.OperationId);

        var none = new CapacityReservationFactBodyV1(grant, request, new CapacityGrantExpiryV1.NoExpiry());
        var at = new CapacityReservationFactBodyV1(grant, request,
            new CapacityGrantExpiryV1.At(new(request.Deadline.ClockDomainId, request.Deadline.BootId, request.Deadline.Nanoseconds + 1)));

        Assert.IsType<CapacityGrantExpiryV1.NoExpiry>(none.ExpiresAt);
        Assert.IsType<CapacityGrantExpiryV1.At>(at.ExpiresAt);
        Assert.Equal(CapacityGrantExpiryKindV1.NoExpiry, none.ExpiresAt.Kind);
        Assert.Equal(CapacityGrantExpiryKindV1.At, at.ExpiresAt.Kind);
        Assert.Throws<ArgumentException>(() => new CapacityReservationFactBodyV1(CapacityGrantId.Create(), request, none.ExpiresAt));
        Assert.Throws<ArgumentException>(() => new CapacityReservationFactBodyV1(grant, request,
            new CapacityGrantExpiryV1.At(request.Deadline)));
        Assert.Throws<ArgumentException>(() => new CapacityReservationFactBodyV1(grant, request,
            new CapacityGrantExpiryV1.At(new(ClockDomainId.Create(), request.Deadline.BootId, request.Deadline.Nanoseconds + 1))));
    }

    [Fact]
    public void Settlement_DeeplyOwnsSortsBoundsAndRejectsDuplicateKeys()
    {
        var request = Request();
        var grant = CapacityGrantIdDerivationV1.Derive(request.OperationId);
        var first = SettlementCharge(CapacityDimensionsV1.QueueItems, 2);
        var second = SettlementCharge(CapacityDimensionsV1.MediaBytes, 1);
        var source = new[] { first, second };
        var fact = new CapacitySettlementFactBodyV1(grant, OperationId.Create(),
            new(request.Authority.Session, 3), CapacitySettlementKindV1.Activated, source, Evidence());
        source[0] = SettlementCharge(CapacityDimensionsV1.QueueItems, 9);

        Assert.Equal([CapacityDimensionsV1.MediaBytes, CapacityDimensionsV1.QueueItems], fact.Charges.Select(x => x.DimensionId));
        Assert.Throws<ArgumentException>(() => new CapacitySettlementFactBodyV1(grant, OperationId.Create(),
            fact.ExpectedFact, fact.Kind, [first, SettlementCharge(CapacityDimensionsV1.QueueItems, 1)], Evidence()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacitySettlementFactBodyV1(grant, OperationId.Create(),
            fact.ExpectedFact, fact.Kind, [], Evidence()));
        Assert.Throws<ArgumentException>(() => new CapacitySettlementFactBodyV1(grant, OperationId.Create(),
            fact.ExpectedFact, (CapacitySettlementKindV1)99, [first], Evidence()));
    }

    [Fact]
    public void CanonicalCodecs_RoundTripAndRejectTrailingOrNoncanonicalData()
    {
        var request = Request();
        var grant = CapacityGrantIdDerivationV1.Derive(request.OperationId);
        var reservation = new CapacityReservationFactBodyV1(grant, request,
            new CapacityGrantExpiryV1.At(new(request.Deadline.ClockDomainId, request.Deadline.BootId, 101)));
        var reservationBytes = CapacityLedgerCodecsV1.EncodeReservation(reservation);
        Assert.True(CapacityLedgerCodecsV1.TryDecodeReservation(reservationBytes, out var decodedReservation));
        Assert.Equal(reservationBytes, CapacityLedgerCodecsV1.EncodeReservation(decodedReservation!));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(reservationBytes.Concat([(byte)0x00]).ToArray(), out _));

        var rateScope = new CapacityScopeV1(Id<TenantId>(144), null, new CapacitySubjectV1.Exporter(Id<ExportId>(149)));
        var rateReservation = SingleReservation(Id<OperationId>(32), request.Authority,
            new(CapacityDimensionsV1.DiagnosticCardinality, rateScope, 1, Id<CapacityPurposeId>(98),
                new CapacityChargeWindowV1.EndsAt(Evidence(200))), CapacityPriorityV1.Normal, new CapacityGrantExpiryV1.NoExpiry());
        var rateBytes = CapacityLedgerCodecsV1.EncodeReservation(rateReservation);
        Assert.True(CapacityLedgerCodecsV1.TryDecodeReservation(rateBytes, out var decodedRate));
        Assert.Equal(rateBytes, CapacityLedgerCodecsV1.EncodeReservation(decodedRate!));

        var settlement = new CapacitySettlementFactBodyV1(grant, Id<OperationId>(16),
            new(request.Authority.Session, 3), CapacitySettlementKindV1.Activated,
            [SettlementCharge(CapacityDimensionsV1.MediaBytes, 1)], Evidence());
        var settlementBytes = CapacityLedgerCodecsV1.EncodeSettlement(settlement);
        Assert.True(CapacityLedgerCodecsV1.TryDecodeSettlement(settlementBytes, out var decodedSettlement));
        Assert.Equal(settlementBytes, CapacityLedgerCodecsV1.EncodeSettlement(decodedSettlement!));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeSettlement(settlementBytes[..^1], out _));
    }

    [Fact]
    public void CanonicalCodecs_RejectOversizedIdsUnknownArmsAndIndefiniteMaps()
    {
        var reservation = ReservationWithTwoCharges();
        var encoded = CapacityLedgerCodecsV1.EncodeReservation(reservation);

        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, oversizedGrant: true), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, unknownExpiry: true), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, requestMode: ArrayMode.UnknownWindow), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, indefinite: true), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, wrongFirstTag: true), out _));

        var settlement = SettlementWithTwoCharges(reservation);
        var settlementBytes = CapacityLedgerCodecsV1.EncodeSettlement(settlement);
        Assert.False(CapacityLedgerCodecsV1.TryDecodeSettlement(RewriteSettlement(settlementBytes, unknownKind: true), out _));
    }

    [Fact]
    public void CanonicalCodecs_RejectUnsortedDuplicateAndOverboundArrays()
    {
        var reservation = ReservationWithTwoCharges();
        var encoded = CapacityLedgerCodecsV1.EncodeReservation(reservation);
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, requestMode: ArrayMode.Reverse), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, requestMode: ArrayMode.Duplicate), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeReservation(RewriteReservation(encoded, requestMode: ArrayMode.Overbound), out _));

        var settlementBytes = CapacityLedgerCodecsV1.EncodeSettlement(SettlementWithTwoCharges(reservation));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeSettlement(RewriteSettlement(settlementBytes, ArrayMode.Reverse), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeSettlement(RewriteSettlement(settlementBytes, ArrayMode.Duplicate), out _));
        Assert.False(CapacityLedgerCodecsV1.TryDecodeSettlement(RewriteSettlement(settlementBytes, ArrayMode.Overbound), out _));
    }

    [Fact]
    public void Reducer_ReconstructsPartialLifecycleAndExactConservation()
    {
        var reservation = ReservationWithTwoCharges();
        var session = reservation.Request.Authority.Session;
        var entries = new List<CapacityLedgerEntryV1>
        {
            new CapacityLedgerEntryV1.Reservation(new(session, 1), session, reservation.Request.Authority, reservation),
        };
        var activation = new CapacitySettlementFactBodyV1(reservation.GrantId, Id<OperationId>(16), new(session, 1),
            CapacitySettlementKindV1.Activated, [new(CapacityDimensionsV1.MediaBytes, ParticipantScope(), Id<CapacityPurposeId>(96), 1)], Evidence(50));
        entries.Add(new CapacityLedgerEntryV1.Settlement(new(session, 2), session, reservation.Request.Authority, activation));
        var release = new CapacitySettlementFactBodyV1(reservation.GrantId, Id<OperationId>(32), new(session, 2),
            CapacitySettlementKindV1.Released, [new(CapacityDimensionsV1.MediaBytes, ParticipantScope(), Id<CapacityPurposeId>(96), 1)], Evidence(60));
        entries.Add(new CapacityLedgerEntryV1.Settlement(new(session, 3), session, reservation.Request.Authority, release));

        var result = Assert.IsType<CapacityLedgerFoldResultV1.Current>(CapacityLedgerReducerV1.Fold(entries));
        var grant = Assert.Single(result.Grants);
        Assert.Equal(CapacityGrantStateV1.Settling, grant.State);
        Assert.Equal(2, grant.Balances.Sum(x => x.Unactivated + x.Active + x.Released + x.Consumed + x.AgedOut + x.Revoked + x.ExplicitlyUnknown));
        Assert.Equal(new JournalPositionV1(session, 3), grant.CurrentFact);
    }

    [Fact]
    public void Reducer_IsolatesScopesAndRejectsOverdrawAndStalePredecessor()
    {
        var first = ReservationWithTwoCharges();
        var session = first.Request.Authority.Session;
        var overdraw = ReservationWithOperationAndParticipant(Id<OperationId>(32), Id<ParticipantId>(145), session, 16_777_216);
        var isolated = ReservationWithOperationAndParticipant(Id<OperationId>(48), Id<ParticipantId>(161), session, 16_777_216);

        Assert.IsType<CapacityLedgerFoldResultV1.InvalidHistory>(CapacityLedgerReducerV1.Fold(
            [new CapacityLedgerEntryV1.Reservation(new(session, 1), session, first.Request.Authority, first),
             new CapacityLedgerEntryV1.Reservation(new(session, 2), session, overdraw.Request.Authority, overdraw)]));
        Assert.IsType<CapacityLedgerFoldResultV1.Current>(CapacityLedgerReducerV1.Fold(
            [new CapacityLedgerEntryV1.Reservation(new(session, 1), session, first.Request.Authority, first),
             new CapacityLedgerEntryV1.Reservation(new(session, 2), session, isolated.Request.Authority, isolated)]));

        var stale = new CapacitySettlementFactBodyV1(first.GrantId, Id<OperationId>(64), new(session, 9),
            CapacitySettlementKindV1.Released, [new(CapacityDimensionsV1.MediaBytes, ParticipantScope(), Id<CapacityPurposeId>(96), 1)], Evidence());
        Assert.IsType<CapacityLedgerFoldResultV1.InvalidHistory>(CapacityLedgerReducerV1.Fold(
            [new CapacityLedgerEntryV1.Reservation(new(session, 1), session, first.Request.Authority, first),
             new CapacityLedgerEntryV1.Settlement(new(session, 2), session, first.Request.Authority, stale)]));
    }

    [Fact]
    public void Reducer_UsesOnlyMatchingEmergencyReserveAndRejectsWrongSettlementKind()
    {
        var seed = Request();
        var authorityRequest = new CapacityRequestV1(seed.OperationId, seed.Authority,
            [new(CapacityDimensionsV1.JournalBytes, SessionScope(), 983_041, Id<CapacityPurposeId>(96), NoWindow())], seed.Deadline, CapacityPriorityV1.Authority);
        var reservation = new CapacityReservationFactBodyV1(CapacityGrantIdDerivationV1.Derive(seed.OperationId), authorityRequest, new CapacityGrantExpiryV1.NoExpiry());
        var session = seed.Authority.Session;
        var current = Assert.IsType<CapacityLedgerFoldResultV1.Current>(CapacityLedgerReducerV1.Fold(
            [new CapacityLedgerEntryV1.Reservation(new(session, 1), session, seed.Authority, reservation)]));
        Assert.Equal(1, Assert.Single(Assert.Single(current.Grants).Balances).ReserveAllocation);

        var consumed = new CapacitySettlementFactBodyV1(reservation.GrantId, Id<OperationId>(16), new(session, 1),
            CapacitySettlementKindV1.Consumed, [new(CapacityDimensionsV1.JournalBytes, SessionScope(), Id<CapacityPurposeId>(96), 1)], Evidence());
        Assert.IsType<CapacityLedgerFoldResultV1.InvalidHistory>(CapacityLedgerReducerV1.Fold(
            [new CapacityLedgerEntryV1.Reservation(new(session, 1), session, seed.Authority, reservation),
             new CapacityLedgerEntryV1.Settlement(new(session, 2), session, seed.Authority, consumed)]));
    }

    [Theory]
    [InlineData(CapacitySettlementKindV1.MarkedUnknown)]
    [InlineData(CapacitySettlementKindV1.Revoked)]
    public void Reducer_UnknownAndRevokedRemainEncumberedUntilPredecessorFencedRepair(CapacitySettlementKindV1 unresolvedKind)
    {
        var seed = Request(); var session = seed.Authority.Session; var scope = ParticipantScope();
        var reservation = SingleReservation(seed.OperationId, seed.Authority,
            new(CapacityDimensionsV1.MediaBytes, scope, 1, Id<CapacityPurposeId>(96), NoWindow()), CapacityPriorityV1.Normal, new CapacityGrantExpiryV1.NoExpiry());
        var unresolved = Settlement(reservation, Id<OperationId>(16), 1, unresolvedKind, 1, scope, 102);
        var blocked = SingleReservation(Id<OperationId>(32), seed.Authority,
            new(CapacityDimensionsV1.MediaBytes, scope, 16_777_216, Id<CapacityPurposeId>(97), NoWindow()), CapacityPriorityV1.Normal, new CapacityGrantExpiryV1.NoExpiry());
        var repair = Settlement(reservation, Id<OperationId>(48), 2, CapacitySettlementKindV1.RecoveredReleased, 1, scope, 103);

        Assert.IsType<CapacityLedgerFoldResultV1.InvalidHistory>(CapacityLedgerReducerV1.Fold(
            [Entry(1, reservation), Entry(2, reservation, unresolved), Entry(3, blocked)]));
        var repairedFold = CapacityLedgerReducerV1.Fold([Entry(1, reservation), Entry(2, reservation, unresolved), Entry(3, reservation, repair), Entry(4, blocked)]);
        var current = AssertCurrent(repairedFold);
        var repaired = current.Grants[0].Balances[0];
        Assert.Equal(0, repaired.ExplicitlyUnknown);
        Assert.Equal(0, repaired.Revoked);
        Assert.Equal(1, repaired.Released);
        Assert.Equal(0, repaired.EncumberedNormal + repaired.EncumberedReserve);
    }

    [Fact]
    public void Reducer_ReplenishesReserveBeforeNormalForMixedAllocations()
    {
        var seed = Request(); var session = seed.Authority.Session; var scope = SessionScope();
        var mixed = SingleReservation(seed.OperationId, seed.Authority,
            new(CapacityDimensionsV1.JournalBytes, scope, 983_041, Id<CapacityPurposeId>(96), NoWindow()), CapacityPriorityV1.Authority, new CapacityGrantExpiryV1.NoExpiry());
        var release = Settlement(mixed, Id<OperationId>(16), 1, CapacitySettlementKindV1.Released, 1, scope, 102,
            CapacityDimensionsV1.JournalBytes);
        var ordinary = SingleReservation(Id<OperationId>(32), seed.Authority,
            new(CapacityDimensionsV1.JournalBytes, scope, 1, Id<CapacityPurposeId>(97), NoWindow()), CapacityPriorityV1.Normal, new CapacityGrantExpiryV1.NoExpiry());
        var emergency = SingleReservation(Id<OperationId>(48), seed.Authority,
            new(CapacityDimensionsV1.JournalBytes, scope, 1, Id<CapacityPurposeId>(98), NoWindow()), CapacityPriorityV1.Authority, new CapacityGrantExpiryV1.NoExpiry());

        Assert.IsType<CapacityLedgerFoldResultV1.InvalidHistory>(CapacityLedgerReducerV1.Fold(
            [Entry(1, mixed), Entry(2, mixed, release), Entry(3, ordinary)]));
        var current = AssertCurrent(CapacityLedgerReducerV1.Fold([Entry(1, mixed), Entry(2, mixed, release), Entry(3, emergency)]));
        Assert.Equal(1, current.Grants[1].Balances[0].ReserveAllocation);
    }

    [Fact]
    public void Reducer_RateWindowConsumptionFreesOnlyAfterComparableExpiryEvidence()
    {
        var seed = Request(); var session = seed.Authority.Session;
        var scope = new CapacityScopeV1(Id<TenantId>(144), null, new CapacitySubjectV1.Exporter(Id<ExportId>(149)));
        var expiry = new CapacityGrantExpiryV1.NoExpiry();
        var reservation = SingleReservation(seed.OperationId, seed.Authority,
            new(CapacityDimensionsV1.DiagnosticCardinality, scope, 1024, Id<CapacityPurposeId>(96), new CapacityChargeWindowV1.EndsAt(Evidence(200))), CapacityPriorityV1.Normal, expiry);
        var activate = Settlement(reservation, Id<OperationId>(16), 1, CapacitySettlementKindV1.Activated, 1024, scope, 102,
            CapacityDimensionsV1.DiagnosticCardinality);
        var consumed = Settlement(reservation, Id<OperationId>(32), 2, CapacitySettlementKindV1.Consumed, 1024, scope, 103,
            CapacityDimensionsV1.DiagnosticCardinality);
        var early = Settlement(reservation, Id<OperationId>(48), 3, CapacitySettlementKindV1.WindowAgedOut, 1024, scope, 199,
            CapacityDimensionsV1.DiagnosticCardinality, 199);
        var aged = Settlement(reservation, Id<OperationId>(64), 3, CapacitySettlementKindV1.WindowAgedOut, 1024, scope, 200,
            CapacityDimensionsV1.DiagnosticCardinality, 200);

        Assert.IsType<CapacityLedgerFoldResultV1.InvalidHistory>(CapacityLedgerReducerV1.Fold(
            [Entry(1, reservation), Entry(2, reservation, activate), Entry(3, reservation, consumed), Entry(4, reservation, early)]));
        var current = AssertCurrent(CapacityLedgerReducerV1.Fold([Entry(1, reservation), Entry(2, reservation, activate), Entry(3, reservation, consumed), Entry(4, reservation, aged)]));
        Assert.Equal(1024, current.Grants[0].Balances[0].AgedOut);
        Assert.Equal(0, current.Grants[0].Balances[0].EncumberedNormal);
    }

    private static CapacityRequestV1 Request()
    {
        var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(64), Id<LiveSessionId>(80));
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        return new(Id<OperationId>(0), authority,
            [new(CapacityDimensionsV1.MediaBytes, ParticipantScope(), 1, Id<CapacityPurposeId>(96), NoWindow())],
            new(Id<ClockDomainId>(112), Id<BootId>(128), 100), CapacityPriorityV1.Normal);
    }

    private static CapacitySettlementChargeV1 SettlementCharge(CapacityDimensionId dimension, long amount) =>
        new(dimension, dimension == CapacityDimensionsV1.QueueItems ? OperationScope() : ParticipantScope(), Id<CapacityPurposeId>(96), amount);

    private static CapacityScopeV1 TenantScope() => new(Id<TenantId>(144));
    private static CapacityScopeV1 ParticipantScope(ParticipantId? participant = null) =>
        new(Id<TenantId>(144), null, new CapacitySubjectV1.Participant(participant ?? Id<ParticipantId>(145)));
    private static CapacityScopeV1 OperationScope(OperationId? operation = null) =>
        new(Id<TenantId>(144), null, new CapacitySubjectV1.Operation(operation ?? Id<OperationId>(146)));
    private static CapacityScopeV1 SessionScope() => new(Id<TenantId>(144), Id<SessionId>(147));
    private static MonotonicStampV1 Evidence(ulong nanoseconds = 102) => new(Id<ClockDomainId>(112), Id<BootId>(128), nanoseconds);
    private static CapacityChargeWindowV1 NoWindow() => new CapacityChargeWindowV1.NoWindow();

    private static CapacityReservationFactBodyV1 SingleReservation(OperationId operation, ExpectedAuthorityVectorV1 authority,
        CapacityChargeV1 charge, CapacityPriorityV1 priority, CapacityGrantExpiryV1 expiry)
    {
        var request = new CapacityRequestV1(operation, authority, [charge], Evidence(100), priority);
        return new(CapacityGrantIdDerivationV1.Derive(operation), request, expiry);
    }

    private static CapacitySettlementFactBodyV1 Settlement(CapacityReservationFactBodyV1 reservation, OperationId operation,
        long expectedSequence, CapacitySettlementKindV1 kind, long amount, CapacityScopeV1 scope, ulong evidence,
        CapacityDimensionId? dimension = null, ulong? evidenceOverride = null) =>
        new(reservation.GrantId, operation, new(reservation.Request.Authority.Session, expectedSequence), kind,
            [new(dimension ?? CapacityDimensionsV1.MediaBytes, scope, Id<CapacityPurposeId>(96), amount)], Evidence(evidenceOverride ?? evidence));

    private static CapacityLedgerEntryV1.Reservation Entry(long sequence, CapacityReservationFactBodyV1 reservation) =>
        new(new(reservation.Request.Authority.Session, sequence), reservation.Request.Authority.Session, reservation.Request.Authority, reservation);

    private static CapacityLedgerEntryV1.Settlement Entry(long sequence, CapacityReservationFactBodyV1 reservation,
        CapacitySettlementFactBodyV1 settlement) =>
        new(new(settlement.ExpectedFact.Session, sequence), settlement.ExpectedFact.Session,
            reservation.Request.Authority, settlement);

    private static CapacityLedgerFoldResultV1.Current AssertCurrent(CapacityLedgerFoldResultV1 result)
    {
        if (result is CapacityLedgerFoldResultV1.InvalidHistory invalid)
            Assert.Fail($"Capacity history was rejected: {invalid.SafeCode} at {invalid.LastVerifiedPosition}.");
        return Assert.IsType<CapacityLedgerFoldResultV1.Current>(result);
    }

    private static CapacityReservationFactBodyV1 ReservationWithTwoCharges()
    {
        var seed = Request();
        var request = new CapacityRequestV1(seed.OperationId, seed.Authority,
            [new(CapacityDimensionsV1.MediaBytes, ParticipantScope(), 1, Id<CapacityPurposeId>(96), NoWindow()),
             new(CapacityDimensionsV1.QueueItems, OperationScope(), 1, Id<CapacityPurposeId>(97), NoWindow())],
            seed.Deadline, seed.Priority);
        return new(CapacityGrantIdDerivationV1.Derive(request.OperationId), request, new CapacityGrantExpiryV1.NoExpiry());
    }

    private static CapacityReservationFactBodyV1 ReservationWithOperationAndParticipant(OperationId operation, ParticipantId participant,
        SessionAuthorityStampV1 session, long amount)
    {
        var request = new CapacityRequestV1(operation, ExpectedAuthorityVectorV1.Create(session, []),
            [new(CapacityDimensionsV1.MediaBytes, ParticipantScope(participant), amount, Id<CapacityPurposeId>(96), NoWindow())],
            new(Id<ClockDomainId>(112), Id<BootId>(128), 100), CapacityPriorityV1.Normal);
        return new(CapacityGrantIdDerivationV1.Derive(operation), request, new CapacityGrantExpiryV1.NoExpiry());
    }

    private static CapacitySettlementFactBodyV1 SettlementWithTwoCharges(CapacityReservationFactBodyV1 reservation) =>
        new(reservation.GrantId, Id<OperationId>(16), new(reservation.Request.Authority.Session, 3),
            CapacitySettlementKindV1.Activated,
            [new(CapacityDimensionsV1.MediaBytes, ParticipantScope(), Id<CapacityPurposeId>(96), 1),
             new(CapacityDimensionsV1.QueueItems, OperationScope(), Id<CapacityPurposeId>(97), 1)], Evidence());

    private enum ArrayMode { Keep, Reverse, Duplicate, Overbound, UnknownWindow }

    private static byte[] RewriteReservation(byte[] encoded, bool oversizedGrant = false, bool unknownExpiry = false,
        bool indefinite = false, bool wrongFirstTag = false, ArrayMode requestMode = ArrayMode.Keep)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical);
        reader.ReadStartMap(); reader.ReadUInt64(); var grant = reader.ReadEncodedValue();
        reader.ReadUInt64(); var request = RewriteRequest(reader.ReadEncodedValue().ToArray(), requestMode);
        reader.ReadUInt64(); var expiry = reader.ReadEncodedValue(); reader.ReadEndMap();
        var writer = new CborWriter(indefinite || wrongFirstTag ? CborConformanceMode.Lax : CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(indefinite ? null : 3);
        writer.WriteUInt64(wrongFirstTag ? 2UL : 1UL);
        if (oversizedGrant) writer.WriteByteString(new byte[17]); else writer.WriteEncodedValue(grant.Span);
        writer.WriteUInt64(2); writer.WriteEncodedValue(request);
        writer.WriteUInt64(3);
        if (unknownExpiry) { writer.WriteStartMap(1); writer.WriteUInt64(1); writer.WriteUInt64(99); writer.WriteEndMap(); }
        else writer.WriteEncodedValue(expiry.Span);
        writer.WriteEndMap(); return writer.Encode();
    }

    private static byte[] RewriteRequest(byte[] encoded, ArrayMode mode)
    {
        if (mode == ArrayMode.Keep) return encoded;
        var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical);
        reader.ReadStartMap(); reader.ReadUInt64(); var op = reader.ReadEncodedValue();
        reader.ReadUInt64(); var authority = reader.ReadEncodedValue(); reader.ReadUInt64();
        var count = reader.ReadStartArray()!.Value; var charges = new List<ReadOnlyMemory<byte>>();
        for (var i = 0; i < count; i++) charges.Add(reader.ReadEncodedValue()); reader.ReadEndArray();
        if (mode == ArrayMode.UnknownWindow) charges[0] = RewriteChargeWithUnknownWindow(charges[0]);
        reader.ReadUInt64(); var deadline = reader.ReadEncodedValue(); reader.ReadUInt64(); var priority = reader.ReadEncodedValue(); reader.ReadEndMap();
        return WriteReorderedMap(op, authority, charges, deadline, priority, mode);
    }

    private static byte[] WriteReorderedMap(ReadOnlyMemory<byte> op, ReadOnlyMemory<byte> authority,
        List<ReadOnlyMemory<byte>> charges, ReadOnlyMemory<byte> deadline, ReadOnlyMemory<byte> priority, ArrayMode mode)
    {
        var items = Reorder(charges, mode); var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5); writer.WriteUInt64(1); writer.WriteEncodedValue(op.Span); writer.WriteUInt64(2); writer.WriteEncodedValue(authority.Span);
        writer.WriteUInt64(3); writer.WriteStartArray(items.Count); foreach (var item in items) writer.WriteEncodedValue(item.Span); writer.WriteEndArray();
        writer.WriteUInt64(4); writer.WriteEncodedValue(deadline.Span); writer.WriteUInt64(5); writer.WriteEncodedValue(priority.Span); writer.WriteEndMap(); return writer.Encode();
    }

    private static byte[] RewriteSettlement(byte[] encoded, ArrayMode mode = ArrayMode.Keep, bool unknownKind = false)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical); reader.ReadStartMap();
        reader.ReadUInt64(); var grant = reader.ReadEncodedValue(); reader.ReadUInt64(); var op = reader.ReadEncodedValue();
        reader.ReadUInt64(); var expected = reader.ReadEncodedValue(); reader.ReadUInt64(); var kind = reader.ReadUInt64(); reader.ReadUInt64();
        var count = reader.ReadStartArray()!.Value; var charges = new List<ReadOnlyMemory<byte>>();
        for (var i = 0; i < count; i++) charges.Add(reader.ReadEncodedValue()); reader.ReadEndArray();
        reader.ReadUInt64(); var evidenceAt = reader.ReadEncodedValue(); reader.ReadEndMap();
        var items = Reorder(charges, mode); var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(6); writer.WriteUInt64(1); writer.WriteEncodedValue(grant.Span); writer.WriteUInt64(2); writer.WriteEncodedValue(op.Span);
        writer.WriteUInt64(3); writer.WriteEncodedValue(expected.Span); writer.WriteUInt64(4); writer.WriteUInt64(unknownKind ? 99UL : kind);
        writer.WriteUInt64(5); writer.WriteStartArray(items.Count); foreach (var item in items) writer.WriteEncodedValue(item.Span); writer.WriteEndArray();
        writer.WriteUInt64(6); writer.WriteEncodedValue(evidenceAt.Span);
        writer.WriteEndMap(); return writer.Encode();
    }

    private static List<ReadOnlyMemory<byte>> Reorder(List<ReadOnlyMemory<byte>> source, ArrayMode mode) => mode switch
    {
        ArrayMode.Reverse => source.AsEnumerable().Reverse().ToList(),
        ArrayMode.Duplicate => [source[0], source[0]],
        ArrayMode.Overbound => Enumerable.Repeat(source[0], 257).ToList(),
        _ => source,
    };

    private static ReadOnlyMemory<byte> RewriteChargeWithUnknownWindow(ReadOnlyMemory<byte> encoded)
    {
        var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical); reader.ReadStartMap();
        reader.ReadUInt64(); var dimension = reader.ReadEncodedValue(); reader.ReadUInt64(); var scope = reader.ReadEncodedValue();
        reader.ReadUInt64(); var amount = reader.ReadEncodedValue(); reader.ReadUInt64(); var purpose = reader.ReadEncodedValue();
        reader.ReadUInt64(); reader.ReadEncodedValue(); reader.ReadEndMap();
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical); writer.WriteStartMap(5);
        writer.WriteUInt64(1); writer.WriteEncodedValue(dimension.Span); writer.WriteUInt64(2); writer.WriteEncodedValue(scope.Span);
        writer.WriteUInt64(3); writer.WriteEncodedValue(amount.Span); writer.WriteUInt64(4); writer.WriteEncodedValue(purpose.Span);
        writer.WriteUInt64(5); writer.WriteStartMap(1); writer.WriteUInt64(1); writer.WriteUInt64(99); writer.WriteEndMap();
        writer.WriteEndMap(); return writer.Encode();
    }

    private static T Id<T>(byte start) where T : struct
    {
        var bytes = Enumerable.Range(start, 16).Select(value => (byte)value).ToArray();
        var method = typeof(T).GetMethod("FromValue", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (T)method.Invoke(null, [StableId128.FromBytes(bytes)])!;
    }
}
