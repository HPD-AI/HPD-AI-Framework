using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityOwnerAndCapacityPayloadCodecsV1Tests
{
    [Fact] public void Owner_and_capacity_outers_round_trip_with_separate_hashes()
    {
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());
        var authority=ExpectedAuthorityVectorV1.Create(session,[new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);
        var owner=new AuthorityOwnerPayloadV1(5,[1,2,3]);
        var reservation=new CapacityReservationCommandV1(session,authority,[4,5]);
        var settlement=new CapacitySettlementFactV1(session,authority,[6,7]);
        Assert.True(AuthorityOwnerAndCapacityPayloadCodecsV1.TryDecodeOwner(AuthorityOwnerAndCapacityPayloadCodecsV1.Encode(owner),out var decodedOwner));Assert.Equal(owner.Discriminator,decodedOwner!.Discriminator);Assert.Equal(owner.Payload,decodedOwner.Payload);
        Assert.True(AuthorityOwnerAndCapacityPayloadCodecsV1.TryDecodeReservation(AuthorityOwnerAndCapacityPayloadCodecsV1.Encode(reservation),out var decodedReservation));Assert.Equal(reservation.Body,decodedReservation!.Body);
        Assert.True(AuthorityOwnerAndCapacityPayloadCodecsV1.TryDecodeSettlement(AuthorityOwnerAndCapacityPayloadCodecsV1.Encode(settlement),out var decodedSettlement));Assert.Equal(settlement.Body,decodedSettlement!.Body);
        Assert.NotEqual(AuthorityOwnerAndCapacityPayloadCodecsV1.ComputeHash(reservation),AuthorityOwnerAndCapacityPayloadCodecsV1.ComputeHash(settlement));
    }
    [Fact] public void Owner_and_capacity_outers_fail_closed()
    {
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var authority=ExpectedAuthorityVectorV1.Create(session,[]);
        Assert.Throws<ArgumentException>(()=>new AuthorityOwnerPayloadV1(0,[]));Assert.Throws<ArgumentException>(()=>new CapacityReservationCommandV1(default,authority,[]));
        Assert.False(AuthorityOwnerAndCapacityPayloadCodecsV1.TryDecodeOwner(new byte[]{0xff},out _));Assert.False(AuthorityOwnerAndCapacityPayloadCodecsV1.TryDecodeReservation(new byte[]{0xff},out _));
    }
}
