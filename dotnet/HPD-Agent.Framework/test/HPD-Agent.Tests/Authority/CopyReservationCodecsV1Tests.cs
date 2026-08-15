using HPD.Agent.Authority;
namespace HPD.Agent.Tests.Authority;
public sealed class CopyReservationCodecsV1Tests
{
 [Fact] public void Copy_reservation_round_trips_and_fails_closed()
 {var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var p=new JournalPositionV1(s,1);var a=ExpectedAuthorityVectorV1.Create(s,[]);var v=new CopyReservationV1(CopyId.Create(),new(p,p),CustodianDescriptorId.Create(),[SubjectId.Create()],DataClassificationV1.Confidential,PurposeId.Create(),AudienceId.Create(),new([new BoundedAscii("us")],false),AuthorizationId.Create(),new(new UtcInstant(1),new UtcInstant(2),RetentionBasisV1.Purpose),a,OperationId.Create(),0);var bytes=CopyReservationCodecsV1.Encode(v);Assert.True(CopyReservationCodecsV1.TryDecode(bytes,out var d));Assert.Equal(v.CopyId,d!.CopyId);Assert.Equal(CopyReservationCodecsV1.ComputeHash(v),CopyReservationCodecsV1.ComputeHash(d));Assert.False(CopyReservationCodecsV1.TryDecode(new byte[]{0xff},out _));}
 [Fact] public void Copy_reservation_rejects_unsorted_subjects()
 {var s=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());var p=new JournalPositionV1(s,1);var ids=new[]{SubjectId.Create(),SubjectId.Create()};Array.Reverse(ids);if(Compare(ids[0],ids[1])<0)Array.Reverse(ids);Assert.Throws<ArgumentException>(()=>new CopyReservationV1(CopyId.Create(),new(p,p),CustodianDescriptorId.Create(),ids,DataClassificationV1.Internal,PurposeId.Create(),AudienceId.Create(),new([],false),AuthorizationId.Create(),new(new UtcInstant(1),new UtcInstant(1),RetentionBasisV1.Legal),ExpectedAuthorityVectorV1.Create(s,[]),OperationId.Create(),1));}
 private static int Compare(SubjectId a,SubjectId b){Span<byte>x=stackalloc byte[16];Span<byte>y=stackalloc byte[16];a.TryWriteBytes(x);b.TryWriteBytes(y);return x.SequenceCompareTo(y);}
}
