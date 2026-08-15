using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class RegisteredAuthorityWireTypesV1Tests
{
    [Fact]
    public void Registered_wire_types_enforce_order_bounds_and_identity()
    {
        var session=new SessionAuthorityStampV1(RuntimeGenerationId.Create(),LiveSessionId.Create());
        var first=new JournalPositionV1(session,1);var last=new JournalPositionV1(session,2);
        Assert.Equal(last,new FactRangeV1(first,last).Last);
        Assert.Throws<ArgumentException>(()=>new FactRangeV1(last,first));
        Assert.Equal(2,new SchemaVersionV1(2,3).Major);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SchemaVersionV1(0,0));
        Assert.Equal(RetentionBasisV1.Purpose,new RetentionIntervalV1(new UtcInstant(1),new UtcInstant(2),RetentionBasisV1.Purpose).Basis);
        Assert.Throws<ArgumentException>(()=>new RetentionIntervalV1(new UtcInstant(2),new UtcInstant(1),RetentionBasisV1.Purpose));
        var regions=new ResidencyRuleV1([new BoundedAscii("eu"),new BoundedAscii("us")],false);
        Assert.Equal(2,regions.AllowedRegions.Count);
        Assert.Throws<ArgumentException>(()=>new ResidencyRuleV1([new BoundedAscii("us"),new BoundedAscii("eu")],false));
    }

    [Fact]
    public void Registered_enums_match_frozen_wire_values()
    {
        Assert.Equal(10,(ushort)TfmId.Net10);Assert.Equal(6,(ushort)RidId.WinX64);
        Assert.Equal(3,(ushort)QualificationDeclarationV1.NotAdvertised);
        Assert.Equal(3,(ushort)EmulationKindV1.OtherRegistered);
        Assert.Equal(5,(ushort)DataClassificationV1.Secret);
    }
}
