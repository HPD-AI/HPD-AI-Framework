using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.Tests.Authority;

public sealed class ProviderCapabilitySetV1Tests
{
    [Fact]
    public void ClosedEnums_HaveExactRegisteredNumericValues()
    {
        Assert.Equal(new ushort[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Enum.GetValues<ProviderRoleV1>().Select(static value => (ushort)value));
        Assert.Equal(new ushort[] { 1, 2, 3, 4 }, Enum.GetValues<ProviderLifetimeV1>().Select(static value => (ushort)value));
    }

    [Fact]
    public void Codec_RoundTripsFullBitRangeAndExactTags()
    {
        Assert.True(Hash256.TryParse(new string('c', 64), out var hash));
        var expected = new ProviderCapabilitySetV1(ushort.MaxValue, ulong.MaxValue, hash);
        var encoded = ProviderCapabilitySetV1Codec.Encode(expected);

        Assert.Equal("a30119ffff021bffffffffffffffff035820", Convert.ToHexString(encoded.AsSpan(0, 18)).ToLowerInvariant());
        Assert.True(ProviderCapabilitySetV1Codec.TryDecode(encoded, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConstructorAndCodec_RejectInvalidDefaultsAndBounds()
    {
        Assert.True(Hash256.TryParse(new string('d', 64), out var hash));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderCapabilitySetV1(0, 0, hash));
        Assert.Throws<ArgumentException>(() => new ProviderCapabilitySetV1(1, 0, default));
        Assert.Throws<ArgumentException>(() => ProviderCapabilitySetV1Codec.Encode(default));
        Assert.False(ProviderCapabilitySetV1Codec.TryDecode(Convert.FromHexString("a301000200035820dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"), out _));

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteUInt64(1);
        writer.WriteUInt64(1);
        writer.WriteUInt64(2);
        writer.WriteUInt64(0);
        writer.WriteUInt64(3);
        writer.WriteByteString(new byte[8192]);
        writer.WriteEndMap();
        Assert.False(ProviderCapabilitySetV1Codec.TryDecode(writer.Encode(), out _));
    }
}
