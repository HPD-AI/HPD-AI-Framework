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

    [Fact]
    public void StableId_UsesTheCheckedInNetworkOrderGoldenVector()
    {
        var bytes = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        Assert.Equal("fct:00041061050R3GG28A1C60T3GF", StableId128.FromBytes(bytes).Format("fct"));
    }

    [Theory]
    [InlineData("liv:00000000000000000000000000")]
    [InlineData("liv:80000000000000000000000000")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAI")]
    [InlineData("run:01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    public void LiveSessionId_RejectsInvalidOrWrongFamily(string text) =>
        Assert.False(LiveSessionId.TryParse(text, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("liv:0000000000000000000000000")]
    [InlineData("liv:000000000000000000000000000")]
    [InlineData(" liv:01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("liv:01arz3ndektsv4rrffq69g5fav")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAL")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAO")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAU")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAV=")]
    [InlineData("liv:01ARZ3NDEKTSV4RRFFQ69G5FAé")]
    public void StableId_RejectsNoncanonicalText(string? text) =>
        Assert.False(LiveSessionId.TryParse(text, out _));

    [Fact]
    public void StableId_RejectsEveryNoncanonicalLeadingDigit()
    {
        const string tail = "0000000000000000000000000";
        foreach (var first in "89ABCDEFGHJKMNPQRSTVWXYZ")
            Assert.False(LiveSessionId.TryParse($"liv:{first}{tail}", out _));
    }

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

    [Fact]
    public void DefaultHash_RemainsValueEqualButHasNoBoundaryText()
    {
        Assert.Equal(default(Hash256), default(Hash256));
        Assert.Equal(string.Empty, default(Hash256).ToString());
    }

    [Fact]
    public void DefaultIds_HaveNoCanonicalBoundaryText()
    {
        Assert.Equal(string.Empty, default(TenantId).ToString());
        Assert.Equal(string.Empty, default(SessionId).ToString());
        Assert.Equal(string.Empty, default(ThreadId).ToString());
        Assert.Equal(string.Empty, default(LiveSessionId).ToString());
        Assert.Equal(string.Empty, default(RuntimeGenerationId).ToString());
    }
}
