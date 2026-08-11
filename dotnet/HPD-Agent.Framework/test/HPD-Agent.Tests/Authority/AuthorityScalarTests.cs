using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class AuthorityScalarTests
{
    [Fact]
    public void StableIds_RoundTripCanonicalText()
    {
        var id = LiveSessionId.Create();
        var text = id.ToString();

        Assert.StartsWith("liv:", text);
        Assert.Equal(30, text.Length);
        Assert.True(LiveSessionId.TryParse(text, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("liv:00000000000000000000000000")]
    [InlineData("liv:80000000000000000000000000")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAI")]
    [InlineData("run:01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    public void LiveSessionId_RejectsInvalidOrWrongFamily(string text) =>
        Assert.False(LiveSessionId.TryParse(text, out _));

    [Fact]
    public void SemanticWrappers_AreNotInterchangeable()
    {
        var liveText = LiveSessionId.Create().ToString();
        Assert.False(RuntimeGenerationId.TryParse(liveText, out _));
    }

    [Fact]
    public void Hash256_UsesLowercaseCanonicalText()
    {
        var hash = Hash256.Compute("abc"u8);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash.ToString());
        Assert.True(Hash256.TryParse(hash.ToString(), out var parsed));
        Assert.Equal(hash, parsed);
        Assert.False(Hash256.TryParse(hash.ToString().ToUpperInvariant(), out _));
    }
}
