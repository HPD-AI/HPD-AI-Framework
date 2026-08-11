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
            new(request.Authority.Session, 3), CapacitySettlementKindV1.Activated, source);
        source[0] = SettlementCharge(CapacityDimensionsV1.QueueItems, 9);

        Assert.Equal([CapacityDimensionsV1.MediaBytes, CapacityDimensionsV1.QueueItems], fact.Charges.Select(x => x.DimensionId));
        Assert.Throws<ArgumentException>(() => new CapacitySettlementFactBodyV1(grant, OperationId.Create(),
            fact.ExpectedFact, fact.Kind, [first, SettlementCharge(CapacityDimensionsV1.QueueItems, 1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacitySettlementFactBodyV1(grant, OperationId.Create(),
            fact.ExpectedFact, fact.Kind, []));
        Assert.Throws<ArgumentException>(() => new CapacitySettlementFactBodyV1(grant, OperationId.Create(),
            fact.ExpectedFact, (CapacitySettlementKindV1)99, [first]));
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

        var settlement = new CapacitySettlementFactBodyV1(grant, Id<OperationId>(16),
            new(request.Authority.Session, 3), CapacitySettlementKindV1.Activated,
            [SettlementCharge(CapacityDimensionsV1.MediaBytes, 1)]);
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

    private static CapacityRequestV1 Request()
    {
        var session = new SessionAuthorityStampV1(Id<RuntimeGenerationId>(64), Id<LiveSessionId>(80));
        var authority = ExpectedAuthorityVectorV1.Create(session, []);
        return new(Id<OperationId>(0), authority,
            [new(CapacityDimensionsV1.MediaBytes, TenantScope(), 1, Id<CapacityPurposeId>(96))],
            new(Id<ClockDomainId>(112), Id<BootId>(128), 100), CapacityPriorityV1.Normal);
    }

    private static CapacitySettlementChargeV1 SettlementCharge(CapacityDimensionId dimension, long amount) =>
        new(dimension, TenantScope(), Id<CapacityPurposeId>(96), amount);

    private static CapacityScopeV1 TenantScope() => new(Id<TenantId>(144));

    private static CapacityReservationFactBodyV1 ReservationWithTwoCharges()
    {
        var seed = Request();
        var request = new CapacityRequestV1(seed.OperationId, seed.Authority,
            [new(CapacityDimensionsV1.MediaBytes, TenantScope(), 1, Id<CapacityPurposeId>(96)),
             new(CapacityDimensionsV1.QueueItems, TenantScope(), 1, Id<CapacityPurposeId>(97))],
            seed.Deadline, seed.Priority);
        return new(CapacityGrantIdDerivationV1.Derive(request.OperationId), request, new CapacityGrantExpiryV1.NoExpiry());
    }

    private static CapacitySettlementFactBodyV1 SettlementWithTwoCharges(CapacityReservationFactBodyV1 reservation) =>
        new(reservation.GrantId, Id<OperationId>(16), new(reservation.Request.Authority.Session, 3),
            CapacitySettlementKindV1.Activated,
            [new(CapacityDimensionsV1.MediaBytes, TenantScope(), Id<CapacityPurposeId>(96), 1),
             new(CapacityDimensionsV1.QueueItems, TenantScope(), Id<CapacityPurposeId>(97), 1)]);

    private enum ArrayMode { Keep, Reverse, Duplicate, Overbound }

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
        for (var i = 0; i < count; i++) charges.Add(reader.ReadEncodedValue()); reader.ReadEndArray(); reader.ReadEndMap();
        var items = Reorder(charges, mode); var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5); writer.WriteUInt64(1); writer.WriteEncodedValue(grant.Span); writer.WriteUInt64(2); writer.WriteEncodedValue(op.Span);
        writer.WriteUInt64(3); writer.WriteEncodedValue(expected.Span); writer.WriteUInt64(4); writer.WriteUInt64(unknownKind ? 99UL : kind);
        writer.WriteUInt64(5); writer.WriteStartArray(items.Count); foreach (var item in items) writer.WriteEncodedValue(item.Span); writer.WriteEndArray();
        writer.WriteEndMap(); return writer.Encode();
    }

    private static List<ReadOnlyMemory<byte>> Reorder(List<ReadOnlyMemory<byte>> source, ArrayMode mode) => mode switch
    {
        ArrayMode.Reverse => source.AsEnumerable().Reverse().ToList(),
        ArrayMode.Duplicate => [source[0], source[0]],
        ArrayMode.Overbound => Enumerable.Repeat(source[0], 257).ToList(),
        _ => source,
    };

    private static T Id<T>(byte start) where T : struct
    {
        var bytes = Enumerable.Range(start, 16).Select(value => (byte)value).ToArray();
        var method = typeof(T).GetMethod("FromValue", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (T)method.Invoke(null, [StableId128.FromBytes(bytes)])!;
    }
}
