using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class SemanticInputAcceptedFactIdV1Tests
{
    [Fact]
    public void Derivation_HasAnExactDomainSeparatedGoldenAndIsRetryStable()
    {
        var payloadHash = Hash256.FromBytes(Convert.FromHexString(
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"));

        var first = SemanticInputAcceptedFactIdV1.Derive(payloadHash);
        var retry = SemanticInputAcceptedFactIdV1.Derive(payloadHash);

        Span<byte> bytes = stackalloc byte[16];
        Assert.True(first.TryWriteBytes(bytes));
        Assert.Equal("950adb5d668800e312cb812f876f5a37", Convert.ToHexString(bytes).ToLowerInvariant());
        Assert.Equal(first, retry);
    }

    [Fact]
    public void Derivation_RejectsTheInvalidDefaultPayloadHash()
    {
        Assert.Throws<ArgumentException>(() => SemanticInputAcceptedFactIdV1.Derive(default));
    }
}
