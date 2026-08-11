using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.Tests.Authority;

public sealed class BoundedAsciiTests
{
    [Fact]
    public void Constructor_EnforcesPrintableAsciiAndExactBounds()
    {
        Assert.False(default(BoundedAscii).IsValid);
        Assert.Equal(string.Empty, default(BoundedAscii).ToString());
        Assert.Throws<ArgumentNullException>(() => new BoundedAscii(null!));
        Assert.Throws<ArgumentException>(() => new BoundedAscii(string.Empty));
        Assert.Throws<ArgumentException>(() => new BoundedAscii("line\nbreak"));
        Assert.Throws<ArgumentException>(() => new BoundedAscii("café"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedAscii(new string('a', 257)));
        Assert.True(new BoundedAscii(new string('~', 256)).IsValid);
    }

    [Fact]
    public void Codec_UsesCanonicalTextAndRoundTripsMaximum()
    {
        var expected = new BoundedAscii(new string('A', 256));
        var encoded = BoundedAsciiCodec.Encode(expected);

        Assert.Equal("790100", Convert.ToHexString(encoded.AsSpan(0, 3)).ToLowerInvariant());
        Assert.True(BoundedAsciiCodec.TryDecode(encoded, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Codec_RejectsEmptyUnicodeControlTrailingAndOversizedBeforeAllocation()
    {
        Assert.False(BoundedAsciiCodec.TryDecode(Convert.FromHexString("60"), out _));
        Assert.False(BoundedAsciiCodec.TryDecode(Convert.FromHexString("62c3a9"), out _));
        Assert.False(BoundedAsciiCodec.TryDecode(Convert.FromHexString("610a"), out _));
        Assert.False(BoundedAsciiCodec.TryDecode(Convert.FromHexString("616100"), out _));

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteTextString(new string('x', 8192));
        Assert.False(BoundedAsciiCodec.TryDecode(writer.Encode(), out _));
    }

    [Fact]
    public void EqualityAndOrdering_AreStructuralAndOrdinal()
    {
        var lower = new BoundedAscii("alpha");
        var same = new BoundedAscii("alpha");
        var upper = new BoundedAscii("Alpha");

        Assert.Equal(lower, same);
        Assert.True(lower == same);
        Assert.True(lower != upper);
        Assert.True(upper.CompareTo(lower) < 0);
        Assert.Equal(default(BoundedAscii), default(BoundedAscii));
    }
}
