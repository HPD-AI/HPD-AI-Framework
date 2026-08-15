using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CoreLifecycleRecordCodecsV1Tests
{
    [Fact]
    public void Semantic_lifecycle_records_round_trip_and_hash_independently()
    {
        var p=new JournalPositionV1(Session(),3);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);var o=OperationId.Create();
        var acceptance=new SemanticAcceptanceBoundV1(o,p,a,1);var reservation=new SemanticReservationCreatedV1(o,p,a,2);
        var bytes=CoreLifecycleRecordCodecsV1.Encode(acceptance);Assert.True(CoreLifecycleRecordCodecsV1.TryDecodeAcceptance(bytes,out var decoded));Assert.Equal(acceptance,decoded);
        bytes=CoreLifecycleRecordCodecsV1.Encode(reservation);Assert.True(CoreLifecycleRecordCodecsV1.TryDecodeReservation(bytes,out var decodedReservation));Assert.Equal(reservation,decodedReservation);
        Assert.NotEqual(CoreLifecycleRecordCodecsV1.ComputeHash(acceptance),CoreLifecycleRecordCodecsV1.ComputeHash(reservation));
    }
    [Fact]
    public void Core_lifecycle_contract_fails_closed()
    {var p=new JournalPositionV1(Session(),1);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);Assert.Throws<ArgumentException>(()=>new SemanticAcceptanceBoundV1(default,p,a,1));Assert.False(CoreLifecycleRecordCodecsV1.TryDecodeAcceptance(new byte[]{0xff},out _));}
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.Create(),LiveSessionId.Create());
}
