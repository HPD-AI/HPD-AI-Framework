using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.Tests.Authority;

public sealed class ProviderContributionV1Tests
{
    [Fact]
    public void Constructor_CanonicalizesCollectionsAndCodecRoundTrips()
    {
        var expected = Create(
            [ProviderRoleV1.Vad, ProviderRoleV1.Chat],
            [SchemaId.Create(), SchemaId.Create()],
            [new("z-key"), new("a-key")]);

        Assert.Equal([ProviderRoleV1.Chat, ProviderRoleV1.Vad], expected.Roles);
        Assert.Equal(["a-key", "z-key"], expected.CredentialAliases.Select(static value => value.ToString()));
        Assert.True(ProviderContributionV1Codec.TryDecode(ProviderContributionV1Codec.Encode(expected), out var actual));
        Assert.Equal(expected, actual);
        Assert.True(expected == actual);
        Assert.False(expected != actual);
        Assert.Equal(expected.GetHashCode(), actual!.GetHashCode());
        Assert.True((ProviderContributionV1?)null == null);
        Assert.False(expected == null);
        Assert.True(expected != null);
    }

    [Fact]
    public void CanonicalEncodingAndIntegrityHash_AreInputOrderIndependent()
    {
        var codec1 = SchemaId.Create();
        var codec2 = SchemaId.Create();
        var left = Create(
            [ProviderRoleV1.Chat, ProviderRoleV1.Realtime],
            [codec1, codec2],
            [new("key-a"), new("key-b")]);
        var right = Create(
            [ProviderRoleV1.Realtime, ProviderRoleV1.Chat],
            [codec2, codec1],
            [new("key-b"), new("key-a")],
            left.ProviderId,
            left.FamilyId,
            left.FactoryId);

        Assert.Equal(ProviderContributionV1Codec.Encode(left), ProviderContributionV1Codec.Encode(right));
        Assert.Equal(ProviderContributionV1Codec.ComputeIntegrityHash(left), ProviderContributionV1Codec.ComputeIntegrityHash(right));
        Assert.True(left == right);

        var unequal = Create([], [], [], left.ProviderId, left.FamilyId, left.FactoryId);
        Assert.True(left != unequal);
    }

    [Fact]
    public void Constructor_RejectsDuplicatesInvalidScalarsAndMaxPlusOne()
    {
        var role = ProviderRoleV1.Chat;
        var codec = SchemaId.Create();
        var alias = new BoundedAscii("key");
        Assert.Throws<ArgumentException>(() => Create([role, role], [], []));
        Assert.Throws<ArgumentException>(() => Create([], [codec, codec], []));
        Assert.Throws<ArgumentException>(() => Create([], [], [alias, alias]));
        Assert.Throws<ArgumentException>(() => Create([(ProviderRoleV1)99], [], []));
        Assert.Throws<ArgumentException>(() => Create([], [default], []));
        Assert.Throws<ArgumentException>(() => Create([], [], [default]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(Enumerable.Repeat(role, 257), [], []));
        Assert.Throws<ArgumentNullException>(() => Create(null!, [], []));
    }

    [Fact]
    public void Decoder_RejectsOversizedCollectionBeforeMaterializingItems()
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Span<byte> id = stackalloc byte[16];
        Assert.True(ProviderId.Create().TryWriteBytes(id));
        Assert.True(Hash256.TryParse(new string('e', 64), out var hash));
        Span<byte> hashBytes = stackalloc byte[32];
        Assert.True(hash.TryWriteBytes(hashBytes));
        writer.WriteStartMap(10);
        writer.WriteUInt64(1); writer.WriteByteString(id);
        writer.WriteUInt64(2); writer.WriteByteString(id);
        writer.WriteUInt64(3); writer.WriteTextString("owner");
        writer.WriteUInt64(4); writer.WriteStartArray(257);
        for (var index = 0; index < 257; index++) writer.WriteUInt64(1);
        writer.WriteEndArray();
        writer.WriteUInt64(5); writer.WriteStartMap(3); writer.WriteUInt64(1); writer.WriteUInt64(1); writer.WriteUInt64(2); writer.WriteUInt64(0); writer.WriteUInt64(3); writer.WriteByteString(hashBytes); writer.WriteEndMap();
        writer.WriteUInt64(6); writer.WriteStartArray(0); writer.WriteEndArray();
        writer.WriteUInt64(7); writer.WriteByteString(id);
        writer.WriteUInt64(8); writer.WriteUInt64(1);
        writer.WriteUInt64(9); writer.WriteStartArray(0); writer.WriteEndArray();
        writer.WriteUInt64(10); writer.WriteByteString(hashBytes);
        writer.WriteEndMap();

        Assert.False(ProviderContributionV1Codec.TryDecode(writer.Encode(), out _));
    }

    [Fact]
    public void Decoder_RejectsNoncanonicalSortedArrayOrder()
    {
        var contribution = Create([ProviderRoleV1.Chat, ProviderRoleV1.Vad], [], []);
        var encoded = ProviderContributionV1Codec.Encode(contribution);
        var pattern = Convert.FromHexString("04820108");
        var offset = encoded.AsSpan().IndexOf(pattern);
        Assert.True(offset >= 0);
        encoded[offset + 2] = 8;
        encoded[offset + 3] = 1;

        Assert.False(ProviderContributionV1Codec.TryDecode(encoded, out _));
    }

    private static ProviderContributionV1 Create(
        IEnumerable<ProviderRoleV1> roles,
        IEnumerable<SchemaId> codecs,
        IEnumerable<BoundedAscii> aliases,
        ProviderId providerId = default,
        ProviderFamilyId familyId = default,
        ProviderFactoryId factoryId = default)
    {
        Assert.True(Hash256.TryParse(new string('a', 64), out var extensionHash));
        Assert.True(Hash256.TryParse(new string('b', 64), out var supportHash));
        return new ProviderContributionV1(
            providerId.IsValid ? providerId : ProviderId.Create(),
            familyId.IsValid ? familyId : ProviderFamilyId.Create(),
            new BoundedAscii("HPD-Agent.Providers.Test"),
            roles,
            new ProviderCapabilitySetV1(1, ulong.MaxValue, extensionHash),
            codecs,
            factoryId.IsValid ? factoryId : ProviderFactoryId.Create(),
            ProviderLifetimeV1.AgentScoped,
            aliases,
            supportHash);
    }
}
