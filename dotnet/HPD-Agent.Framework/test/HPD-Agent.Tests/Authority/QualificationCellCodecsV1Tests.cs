using HPD.Agent.Authority;
namespace HPD.Agent.Tests.Authority;
public sealed class QualificationCellCodecsV1Tests
{
 [Fact]public void Qualification_cell_round_trips_optional_evidence(){var h=Hash256.Compute([1]);var e=new EvidenceReferenceV1(ContentId.Create(),h,1,new BoundedAscii("application/json"));var v=new QualificationCellV1(TfmId.Net10,RidId.OsxArm64,SurfaceId.Create(),new(1,0),EnvironmentProfileId.Create(),QualificationDeclarationV1.AdvertisedPositive,0,e,null,true,EmulationKindV1.None,[]);var b=QualificationCellCodecsV1.Encode(v);Assert.True(QualificationCellCodecsV1.TryDecode(b,out var d));Assert.Equal(v.SurfaceId,d!.SurfaceId);Assert.NotNull(d.BuildEvidence);Assert.Null(d.ExecutionEvidence);Assert.Equal(QualificationCellCodecsV1.ComputeHash(v),QualificationCellCodecsV1.ComputeHash(d));}
 [Fact]public void Qualification_cell_fails_closed(){Assert.False(QualificationCellCodecsV1.TryDecode(new byte[]{0xff},out _));Assert.Throws<ArgumentException>(()=>new QualificationCellV1((TfmId)1,RidId.OsxArm64,SurfaceId.Create(),new(1,0),EnvironmentProfileId.Create(),QualificationDeclarationV1.NotAdvertised,0,null,null,false,EmulationKindV1.None,[]));}
}
