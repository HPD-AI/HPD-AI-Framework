using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.Tests.Authority;

public sealed class ProviderCatalogV1Tests
{
    [Fact]
    public void Catalog_CanonicalizesInputAndRoundTripsWithStableFingerprint()
    {
        var low = Create("pvd:00041061050R3GG28A1C60T3GF", "pvf:00041061050R3GG28A1C60T3GF", "fac:00041061050R3GG28A1C60T3GF");
        var high = Create("pvd:7ZZZZZZZZZZZZZZZZZZZZZZZZZ", "pvf:7ZZZZZZZZZZZZZZZZZZZZZZZZZ", "fac:7ZZZZZZZZZZZZZZZZZZZZZZZZZ");
        var expected = new ProviderCatalogV1([high, low]);
        var same = new ProviderCatalogV1([low, high]);

        Assert.Equal(low, expected.Contributions[0]);
        Assert.Equal(expected, same);
        Assert.Equal(expected.Fingerprint, same.Fingerprint);
        Assert.True(ProviderCatalogV1Codec.TryDecode(ProviderCatalogV1Codec.Encode(expected), out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(expected.Fingerprint, actual!.Fingerprint);
    }

    [Fact]
    public void Catalog_GoldenFingerprintIsExact()
    {
        var contribution = Create("pvd:00041061050R3GG28A1C60T3GF", "pvf:00041061050R3GG28A1C60T3GF", "fac:00041061050R3GG28A1C60T3GF");
        var catalog = new ProviderCatalogV1([contribution]);

        Assert.Equal("0b5038591d0fd6c43df252f435824e748839f428008b9238c870a8ad5fd81cf8", catalog.Fingerprint.ToString());
    }

    [Fact]
    public void Catalog_RejectsEmptyDuplicateNullAndMaxPlusOne()
    {
        var contribution = Create("pvd:00041061050R3GG28A1C60T3GF", "pvf:00041061050R3GG28A1C60T3GF", "fac:00041061050R3GG28A1C60T3GF");
        Assert.Throws<ArgumentException>(() => new ProviderCatalogV1([]));
        Assert.Throws<ArgumentException>(() => new ProviderCatalogV1([contribution, contribution]));
        Assert.Throws<ArgumentException>(() => new ProviderCatalogV1([null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProviderCatalogV1(Enumerable.Repeat(contribution, 257)));
    }

    [Fact]
    public void Decoder_RejectsNoncanonicalOrderAndOversizedCount()
    {
        var low = Create("pvd:00041061050R3GG28A1C60T3GF", "pvf:00041061050R3GG28A1C60T3GF", "fac:00041061050R3GG28A1C60T3GF");
        var high = Create("pvd:7ZZZZZZZZZZZZZZZZZZZZZZZZZ", "pvf:7ZZZZZZZZZZZZZZZZZZZZZZZZZ", "fac:7ZZZZZZZZZZZZZZZZZZZZZZZZZ");
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(1);
        writer.WriteUInt64(1);
        writer.WriteStartArray(2);
        ProviderContributionV1Codec.Write(writer, high);
        ProviderContributionV1Codec.Write(writer, low);
        writer.WriteEndArray();
        writer.WriteEndMap();
        Assert.False(ProviderCatalogV1Codec.TryDecode(writer.Encode(), out _));

        writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(1);
        writer.WriteUInt64(1);
        writer.WriteStartArray(257);
        for (var index = 0; index < 257; index++) ProviderContributionV1Codec.Write(writer, low);
        writer.WriteEndArray();
        writer.WriteEndMap();
        Assert.False(ProviderCatalogV1Codec.TryDecode(writer.Encode(), out _));
    }

    private static ProviderContributionV1 Create(string providerText, string familyText, string factoryText)
    {
        Assert.True(ProviderId.TryParse(providerText, out var providerId));
        Assert.True(ProviderFamilyId.TryParse(familyText, out var familyId));
        Assert.True(ProviderFactoryId.TryParse(factoryText, out var factoryId));
        Assert.True(Hash256.TryParse(new string('a', 64), out var extensionHash));
        Assert.True(Hash256.TryParse(new string('b', 64), out var supportHash));
        return new ProviderContributionV1(
            providerId,
            familyId,
            new BoundedAscii("HPD-Agent.Providers.Test"),
            [ProviderRoleV1.Chat],
            new ProviderCapabilitySetV1(1, 1, extensionHash),
            [],
            factoryId,
            ProviderLifetimeV1.AgentScoped,
            [],
            supportHash);
    }
}
