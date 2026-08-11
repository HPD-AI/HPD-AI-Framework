using HPD.Agent.Authority;

namespace HPD.Agent.Tests.Authority;

public sealed class MonotonicStampV1CodecTests
{
    [Fact]
    public void Encode_UsesExactCanonicalTagsAndUInt64Range()
    {
        Assert.True(ClockDomainId.TryParse("clk:00041061050R3GG28A1C60T3GF", out var domain));
        Assert.True(BootId.TryParse("boo:00041061050R3GG28A1C60T3GF", out var boot));
        var encoded = MonotonicStampV1Codec.Encode(new(domain, boot, ulong.MaxValue));

        Assert.Equal(
            "a30150000102030405060708090a0b0c0d0e0f0250000102030405060708090a0b0c0d0e0f031bffffffffffffffff",
            Convert.ToHexString(encoded).ToLowerInvariant());
    }

    [Fact]
    public void TryDecode_RoundTripsZeroAndMaximumNanoseconds()
    {
        var domain = ClockDomainId.Create();
        var boot = BootId.Create();

        foreach (var nanoseconds in new[] { 0UL, ulong.MaxValue })
        {
            var expected = new MonotonicStampV1(domain, boot, nanoseconds);
            Assert.True(MonotonicStampV1Codec.TryDecode(MonotonicStampV1Codec.Encode(expected), out var actual));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void TryDecode_RejectsNoncanonicalMalformedOrTrailingData()
    {
        Assert.False(MonotonicStampV1Codec.TryDecode(Array.Empty<byte>(), out _));
        Assert.False(MonotonicStampV1Codec.TryDecode(Convert.FromHexString("a30150000102030405060708090a0b0c0d0e0f0250404040404040404040404040404040400400"), out _));
        Assert.False(MonotonicStampV1Codec.TryDecode(Convert.FromHexString("bf0150000102030405060708090a0b0c0d0e0f0250404040404040404040404040404040400300ff"), out _));
        Assert.False(MonotonicStampV1Codec.TryDecode(Convert.FromHexString("a30150000102030405060708090a0b0c0d0e0f025040404040404040404040404040404040030000"), out _));
    }

    [Fact]
    public void Encode_RejectsDefaultStamp()
    {
        Assert.Throws<ArgumentException>(() => MonotonicStampV1Codec.Encode(default));
    }
}
