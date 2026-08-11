using HPD.Agent.Authority;

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

    private static T Id<T>(byte start) where T : struct
    {
        var bytes = Enumerable.Range(start, 16).Select(value => (byte)value).ToArray();
        var method = typeof(T).GetMethod("FromValue", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (T)method.Invoke(null, [StableId128.FromBytes(bytes)])!;
    }
}
