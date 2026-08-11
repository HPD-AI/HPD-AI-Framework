using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class CapacityGrantIdDerivationV1Tests
{
    [Fact]
    public void Derive_IsDeterministicAndUsesTheFrozenDomain()
    {
        var operation = OperationId.FromValue(StableId128.FromBytes(
            Convert.FromHexString("000102030405060708090a0b0c0d0e0f")));

        var first = CapacityGrantIdDerivationV1.Derive(operation);
        var second = CapacityGrantIdDerivationV1.Derive(operation);

        Assert.Equal(first, second);
        Assert.Equal("grt:7C2J4Q6RZSABAD69M7MSXV7BTH", first.ToString());
    }

    [Fact]
    public void Derive_RejectsTheInvalidDefaultOperation()
    {
        Assert.Throws<ArgumentException>(() => CapacityGrantIdDerivationV1.Derive(default));
    }

    [Fact]
    public void GrantAndOperationRemainDistinctPublicTypes()
    {
        Assert.NotEqual(typeof(OperationId), typeof(CapacityGrantId));
        Assert.Null(typeof(CapacityGrantId).GetMethod("op_Implicit"));
    }
}
