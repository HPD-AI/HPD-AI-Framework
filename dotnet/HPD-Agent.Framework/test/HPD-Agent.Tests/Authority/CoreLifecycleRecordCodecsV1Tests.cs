using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CoreLifecycleRecordCodecsV1Tests
{
    [Fact]
    public void Semantic_lifecycle_records_round_trip_and_hash_independently()
    {
        var p=new JournalPositionV1(Session(),3);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);var o=OperationId.Create();
        var acceptance=new SemanticAcceptanceBoundV1(o,p,a,1);var reservation=new SemanticReservationCreatedV1(o,p,a,2);var admitted=new AuthorityFactAdmittedV1(o,p,a,3);
        var bytes=CoreLifecycleRecordCodecsV1.Encode(acceptance);Assert.True(CoreLifecycleRecordCodecsV1.TryDecodeAcceptance(bytes,out var decoded));Assert.Equal(acceptance,decoded);
        bytes=CoreLifecycleRecordCodecsV1.Encode(reservation);Assert.True(CoreLifecycleRecordCodecsV1.TryDecodeReservation(bytes,out var decodedReservation));Assert.Equal(reservation,decodedReservation);
        bytes=CoreLifecycleRecordCodecsV1.Encode(admitted);Assert.True(CoreLifecycleRecordCodecsV1.TryDecodeAdmitted(bytes,out var decodedAdmitted));Assert.Equal(admitted,decodedAdmitted);
        Assert.NotEqual(CoreLifecycleRecordCodecsV1.ComputeHash(acceptance),CoreLifecycleRecordCodecsV1.ComputeHash(reservation));
        Assert.NotEqual(CoreLifecycleRecordCodecsV1.ComputeHash(reservation),CoreLifecycleRecordCodecsV1.ComputeHash(admitted));
    }
    [Fact]
    public void Core_lifecycle_contract_fails_closed()
    {var p=new JournalPositionV1(Session(),1);var a=ExpectedAuthorityVectorV1.Create(p.Session,[new AuthorityAxisValueV1.Graph(GraphGenerationId.Create())]);Assert.Throws<ArgumentException>(()=>new SemanticAcceptanceBoundV1(default,p,a,1));Assert.False(CoreLifecycleRecordCodecsV1.TryDecodeAcceptance(new byte[]{0xff},out _));}
    [Fact]
    public void Reservation_and_binding_have_distinct_registered_schemas_and_deterministic_fact_ids()
    {
        var p=new JournalPositionV1(Session(),3);var a=ExpectedAuthorityVectorV1.Create(p.Session,[]);var o=OperationId.Create();
        var reservation=new SemanticReservationCreatedV1(o,p,a,1);var binding=new SemanticAcceptanceBoundV1(o,p,a,1);
        var reservationBytes=CoreLifecycleRecordCodecsV1.Encode(reservation);var bindingBytes=CoreLifecycleRecordCodecsV1.Encode(binding);
        var reservationRegistration=new SemanticReservationCreatedPayloadRegistrationV1();var bindingRegistration=new SemanticAcceptanceBoundPayloadRegistrationV1();
        Assert.True(reservationRegistration.Validate(reservationBytes,p.Session));Assert.True(bindingRegistration.Validate(bindingBytes,p.Session));
        Assert.Equal(CoreLifecycleRecordCodecsV1.ReservationSchema,reservationRegistration.Schema);
        Assert.Equal(CoreLifecycleRecordCodecsV1.AcceptanceSchema,bindingRegistration.Schema);
        var reservationId=SemanticHandoffFactIdsV1.Reservation(CoreLifecycleRecordCodecsV1.ComputeHash(reservation));
        var bindingId=SemanticHandoffFactIdsV1.AcceptanceBinding(CoreLifecycleRecordCodecsV1.ComputeHash(binding));
        Assert.Equal(reservationId,SemanticHandoffFactIdsV1.Reservation(CoreLifecycleRecordCodecsV1.ComputeHash(reservation)));
        Assert.NotEqual(reservationId,bindingId);
    }
    private static SessionAuthorityStampV1 Session()=>new(RuntimeGenerationId.Create(),LiveSessionId.Create());
}
