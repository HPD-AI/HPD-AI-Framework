using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Identifies a provider role in the closed authority catalog.</summary>
public enum ProviderRoleV1 : ushort
{
    /// <summary>Chat completion or response generation.</summary>
    Chat = 1,
    /// <summary>Vector embedding generation.</summary>
    Embeddings = 2,
    /// <summary>Provider-hosted file storage or retrieval.</summary>
    HostedFiles = 3,
    /// <summary>Image generation.</summary>
    ImageGeneration = 4,
    /// <summary>Stateful realtime model protocol.</summary>
    Realtime = 5,
    /// <summary>Speech-to-text recognition.</summary>
    SpeechToText = 6,
    /// <summary>Text-to-speech synthesis.</summary>
    TextToSpeech = 7,
    /// <summary>Voice activity detection.</summary>
    Vad = 8,
}

/// <summary>Identifies the lifetime policy declared by a provider catalog contribution.</summary>
public enum ProviderLifetimeV1 : ushort
{
    /// <summary>A new instance is created for each bounded acquisition.</summary>
    Transient = 1,
    /// <summary>An instance may be reused within one Agent runtime composite.</summary>
    AgentScoped = 2,
    /// <summary>An instance may be reused only within one live session.</summary>
    SessionScoped = 3,
    /// <summary>The provider factory returns an externally owned singleton.</summary>
    SingletonExternal = 4,
}

/// <summary>Contains a versioned, bounded capability bitset and extension-manifest hash.</summary>
public readonly record struct ProviderCapabilitySetV1
{
    /// <summary>Initializes a validated provider capability set.</summary>
    /// <param name="manifestVersion">The positive capability-manifest version.</param>
    /// <param name="bits">The closed capability bits defined by that version.</param>
    /// <param name="extensionHash">The hash of the canonical extension capability manifest.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="manifestVersion"/> is zero.</exception>
    /// <exception cref="ArgumentException"><paramref name="extensionHash"/> is the invalid default value.</exception>
    public ProviderCapabilitySetV1(ushort manifestVersion, ulong bits, Hash256 extensionHash)
    {
        if (manifestVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(manifestVersion), "A capability manifest version must be positive.");
        Span<byte> hash = stackalloc byte[32];
        if (!extensionHash.TryWriteBytes(hash))
            throw new ArgumentException("An extension manifest hash is required.", nameof(extensionHash));
        ManifestVersion = manifestVersion;
        Bits = bits;
        ExtensionHash = extensionHash;
    }

    /// <summary>Gets the positive capability-manifest version.</summary>
    public ushort ManifestVersion { get; }

    /// <summary>Gets the closed capability bits defined by the manifest version.</summary>
    public ulong Bits { get; }

    /// <summary>Gets the hash of the canonical extension capability manifest.</summary>
    public Hash256 ExtensionHash { get; }

    /// <summary>Gets whether the manifest version and extension hash are valid.</summary>
    public bool IsValid
    {
        get
        {
            Span<byte> hash = stackalloc byte[32];
            return ManifestVersion > 0 && ExtensionHash.TryWriteBytes(hash);
        }
    }
}

/// <summary>Describes one immutable provider-family contribution in the authority catalog.</summary>
public sealed class ProviderContributionV1 : IEquatable<ProviderContributionV1>
{
    /// <summary>The maximum number of roles, codec identities, or credential aliases in one contribution.</summary>
    public const int MaximumItemsPerCollection = 256;

    /// <summary>Initializes and canonically sorts one provider contribution.</summary>
    /// <param name="providerId">The stable provider identity.</param>
    /// <param name="familyId">The stable provider-family identity.</param>
    /// <param name="ownerAssembly">The bounded owning assembly identity.</param>
    /// <param name="roles">The closed roles contributed by this family.</param>
    /// <param name="capabilities">The versioned capability evidence.</param>
    /// <param name="codecIds">The registered request and response codec schema identities.</param>
    /// <param name="factoryId">The stable generated factory identity.</param>
    /// <param name="lifetime">The declared runtime lifetime policy.</param>
    /// <param name="credentialAliases">The canonical credential alias keys, without secret values.</param>
    /// <param name="supportManifest">The hash of declared TFM, RID, environment, and typed-negative support cells.</param>
    /// <exception cref="ArgumentNullException"><paramref name="roles"/>, <paramref name="codecIds"/>, or <paramref name="credentialAliases"/> is null.</exception>
    /// <exception cref="ArgumentException">A required scalar is invalid, an enum is outside its closed set, or a collection contains a duplicate.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A collection exceeds 256 items.</exception>
    public ProviderContributionV1(
        ProviderId providerId,
        ProviderFamilyId familyId,
        BoundedAscii ownerAssembly,
        IEnumerable<ProviderRoleV1> roles,
        ProviderCapabilitySetV1 capabilities,
        IEnumerable<SchemaId> codecIds,
        ProviderFactoryId factoryId,
        ProviderLifetimeV1 lifetime,
        IEnumerable<BoundedAscii> credentialAliases,
        Hash256 supportManifest)
    {
        if (!providerId.IsValid)
            throw new ArgumentException("A provider identity is required.", nameof(providerId));
        if (!familyId.IsValid)
            throw new ArgumentException("A provider-family identity is required.", nameof(familyId));
        if (!ownerAssembly.IsValid)
            throw new ArgumentException("An owner assembly is required.", nameof(ownerAssembly));
        if (!capabilities.IsValid)
            throw new ArgumentException("A capability set is required.", nameof(capabilities));
        if (!factoryId.IsValid)
            throw new ArgumentException("A provider factory identity is required.", nameof(factoryId));
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentException("The provider lifetime is outside the closed registry.", nameof(lifetime));
        Span<byte> supportHash = stackalloc byte[32];
        if (!supportManifest.TryWriteBytes(supportHash))
            throw new ArgumentException("A support manifest hash is required.", nameof(supportManifest));

        ProviderId = providerId;
        FamilyId = familyId;
        OwnerAssembly = ownerAssembly;
        Roles = Array.AsReadOnly(CanonicalizeRoles(roles));
        Capabilities = capabilities;
        CodecIds = Array.AsReadOnly(CanonicalizeCodecIds(codecIds));
        FactoryId = factoryId;
        Lifetime = lifetime;
        CredentialAliases = Array.AsReadOnly(CanonicalizeAliases(credentialAliases));
        SupportManifest = supportManifest;
    }

    /// <summary>Gets the stable provider identity.</summary>
    public ProviderId ProviderId { get; }
    /// <summary>Gets the stable provider-family identity.</summary>
    public ProviderFamilyId FamilyId { get; }
    /// <summary>Gets the bounded owning assembly identity.</summary>
    public BoundedAscii OwnerAssembly { get; }
    /// <summary>Gets the canonically ordered closed provider roles.</summary>
    public IReadOnlyList<ProviderRoleV1> Roles { get; }
    /// <summary>Gets the versioned capability evidence.</summary>
    public ProviderCapabilitySetV1 Capabilities { get; }
    /// <summary>Gets the canonically ordered registered codec schema identities.</summary>
    public IReadOnlyList<SchemaId> CodecIds { get; }
    /// <summary>Gets the stable generated factory identity.</summary>
    public ProviderFactoryId FactoryId { get; }
    /// <summary>Gets the declared runtime lifetime policy.</summary>
    public ProviderLifetimeV1 Lifetime { get; }
    /// <summary>Gets canonically ordered credential alias keys without secret values.</summary>
    public IReadOnlyList<BoundedAscii> CredentialAliases { get; }
    /// <summary>Gets the hash of the declared support-cell manifest.</summary>
    public Hash256 SupportManifest { get; }

    /// <inheritdoc />
    public bool Equals(ProviderContributionV1? other) =>
        other is not null &&
        ProviderId == other.ProviderId &&
        FamilyId == other.FamilyId &&
        OwnerAssembly == other.OwnerAssembly &&
        Roles.SequenceEqual(other.Roles) &&
        Capabilities == other.Capabilities &&
        CodecIds.SequenceEqual(other.CodecIds) &&
        FactoryId == other.FactoryId &&
        Lifetime == other.Lifetime &&
        CredentialAliases.SequenceEqual(other.CredentialAliases) &&
        SupportManifest == other.SupportManifest;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ProviderContributionV1 other && Equals(other);

    /// <summary>Returns whether two provider contributions contain the same canonical value.</summary>
    public static bool operator ==(ProviderContributionV1? left, ProviderContributionV1? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    /// <summary>Returns whether two provider contributions contain different canonical values.</summary>
    public static bool operator !=(ProviderContributionV1? left, ProviderContributionV1? right) => !(left == right);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProviderId);
        hash.Add(FamilyId);
        hash.Add(OwnerAssembly);
        foreach (var role in Roles) hash.Add(role);
        hash.Add(Capabilities);
        foreach (var codecId in CodecIds) hash.Add(codecId);
        hash.Add(FactoryId);
        hash.Add(Lifetime);
        foreach (var alias in CredentialAliases) hash.Add(alias);
        hash.Add(SupportManifest);
        return hash.ToHashCode();
    }

    private static ProviderRoleV1[] CanonicalizeRoles(IEnumerable<ProviderRoleV1> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = CollectBounded(source, nameof(source));
        if (values.Any(static value => !Enum.IsDefined(value)))
            throw new ArgumentException("A provider role is outside the closed registry.", nameof(source));
        Array.Sort(values);
        RejectAdjacentDuplicates(values, nameof(source));
        return values;
    }

    private static SchemaId[] CanonicalizeCodecIds(IEnumerable<SchemaId> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = CollectBounded(source, nameof(source));
        if (values.Any(static value => !value.IsValid))
            throw new ArgumentException("A codec schema identity is invalid.", nameof(source));
        Array.Sort(values, static (left, right) => StringComparer.Ordinal.Compare(left.ToString(), right.ToString()));
        RejectAdjacentDuplicates(values, nameof(source));
        return values;
    }

    private static BoundedAscii[] CanonicalizeAliases(IEnumerable<BoundedAscii> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var values = CollectBounded(source, nameof(source));
        if (values.Any(static value => !value.IsValid))
            throw new ArgumentException("A credential alias is invalid.", nameof(source));
        Array.Sort(values);
        RejectAdjacentDuplicates(values, nameof(source));
        return values;
    }

    private static T[] CollectBounded<T>(IEnumerable<T> source, string parameterName)
    {
        var values = new List<T>();
        foreach (var value in source)
        {
            if (values.Count == MaximumItemsPerCollection)
                throw new ArgumentOutOfRangeException(parameterName, "A provider contribution collection cannot exceed 256 items.");
            values.Add(value);
        }
        return values.ToArray();
    }

    private static void RejectAdjacentDuplicates<T>(T[] values, string parameterName)
    {
        for (var index = 1; index < values.Length; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index - 1], values[index]))
                throw new ArgumentException("A provider contribution collection contains a duplicate.", parameterName);
        }
    }
}

internal static class ProviderContributionV1Codec
{
    internal const string SchemaIdentifier = "hpd.provider-contribution.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;

    internal static byte[] Encode(ProviderContributionV1 value)
    {
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Write(writer, value);
        return writer.Encode();
    }

    internal static void Write(CborWriter writer, ProviderContributionV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Span<byte> providerId = stackalloc byte[16];
        Span<byte> familyId = stackalloc byte[16];
        Span<byte> factoryId = stackalloc byte[16];
        Span<byte> supportManifest = stackalloc byte[32];
        if (!value.ProviderId.TryWriteBytes(providerId) || !value.FamilyId.TryWriteBytes(familyId) ||
            !value.FactoryId.TryWriteBytes(factoryId) || !value.SupportManifest.TryWriteBytes(supportManifest))
            throw new ArgumentException("The provider contribution is invalid.", nameof(value));

        writer.WriteStartMap(10);
        writer.WriteUInt64(1);
        writer.WriteByteString(providerId);
        writer.WriteUInt64(2);
        writer.WriteByteString(familyId);
        writer.WriteUInt64(3);
        BoundedAsciiCodec.Write(writer, value.OwnerAssembly);
        writer.WriteUInt64(4);
        writer.WriteStartArray(value.Roles.Count);
        foreach (var role in value.Roles) writer.WriteUInt64((ushort)role);
        writer.WriteEndArray();
        writer.WriteUInt64(5);
        ProviderCapabilitySetV1Codec.Write(writer, value.Capabilities);
        writer.WriteUInt64(6);
        writer.WriteStartArray(value.CodecIds.Count);
        Span<byte> codec = stackalloc byte[16];
        foreach (var codecId in value.CodecIds)
        {
            if (!codecId.TryWriteBytes(codec))
                throw new ArgumentException("A provider codec identity is invalid.", nameof(value));
            writer.WriteByteString(codec);
        }
        writer.WriteEndArray();
        writer.WriteUInt64(7);
        writer.WriteByteString(factoryId);
        writer.WriteUInt64(8);
        writer.WriteUInt64((ushort)value.Lifetime);
        writer.WriteUInt64(9);
        writer.WriteStartArray(value.CredentialAliases.Count);
        foreach (var alias in value.CredentialAliases) BoundedAsciiCodec.Write(writer, alias);
        writer.WriteEndArray();
        writer.WriteUInt64(10);
        writer.WriteByteString(supportManifest);
        writer.WriteEndMap();
    }

    internal static Hash256 ComputeIntegrityHash(ProviderContributionV1 value) =>
        AuthorityIntegrityHashV1.Compute(SchemaIdentifier, Major, Minor, Encode(value));

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out ProviderContributionV1? value)
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            value = Read(reader);
            if (reader.BytesRemaining != 0)
                return false;
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = null;
            return false;
        }
    }

    internal static ProviderContributionV1 Read(CborReader reader)
    {
        if (reader.ReadStartMap() != 10 || reader.ReadUInt64() != 1)
            throw new CborContentException("A provider contribution must contain exactly tags 1 through 10.");
        var providerId = ReadStableId(reader);
        if (reader.ReadUInt64() != 2)
            throw new CborContentException("Provider contribution tag 2 is missing.");
        var familyId = ReadStableId(reader);
        if (reader.ReadUInt64() != 3)
            throw new CborContentException("Provider contribution tag 3 is missing.");
        var ownerAssembly = BoundedAsciiCodec.Read(reader);
        if (reader.ReadUInt64() != 4)
            throw new CborContentException("Provider contribution tag 4 is missing.");
        var roles = ReadRoles(reader);
        if (reader.ReadUInt64() != 5)
            throw new CborContentException("Provider contribution tag 5 is missing.");
        var capabilities = ProviderCapabilitySetV1Codec.Read(reader);
        if (reader.ReadUInt64() != 6)
            throw new CborContentException("Provider contribution tag 6 is missing.");
        var codecIds = ReadSchemaIds(reader);
        if (reader.ReadUInt64() != 7)
            throw new CborContentException("Provider contribution tag 7 is missing.");
        var factoryId = ReadStableId(reader);
        if (reader.ReadUInt64() != 8)
            throw new CborContentException("Provider contribution tag 8 is missing.");
        var lifetime = reader.ReadUInt64();
        if (lifetime is < 1 or > 4 || reader.ReadUInt64() != 9)
            throw new CborContentException("The provider lifetime is invalid or tag 9 is missing.");
        var aliases = ReadAliases(reader);
        if (reader.ReadUInt64() != 10)
            throw new CborContentException("Provider contribution tag 10 is missing.");
        Span<byte> supportManifest = stackalloc byte[32];
        if (!reader.TryReadByteString(supportManifest, out var supportLength) || supportLength != 32)
            throw new CborContentException("The provider support manifest must be exactly 32 bytes.");
        reader.ReadEndMap();
        return new ProviderContributionV1(
            ProviderId.FromValue(providerId),
            ProviderFamilyId.FromValue(familyId),
            ownerAssembly,
            roles,
            capabilities,
            codecIds,
            ProviderFactoryId.FromValue(factoryId),
            (ProviderLifetimeV1)lifetime,
            aliases,
            Hash256.FromBytes(supportManifest));
    }

    private static StableId128 ReadStableId(CborReader reader)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!reader.TryReadByteString(bytes, out var length) || length != 16)
            throw new CborContentException("A provider stable identity must be exactly 16 bytes.");
        return StableId128.FromBytes(bytes);
    }

    private static ProviderRoleV1[] ReadRoles(CborReader reader)
    {
        var count = ReadBoundedArrayCount(reader);
        var values = new ProviderRoleV1[count];
        for (var index = 0; index < count; index++)
        {
            var role = reader.ReadUInt64();
            if (role is < 1 or > 8)
                throw new CborContentException("A provider role is outside the closed registry.");
            values[index] = (ProviderRoleV1)role;
            if (index > 0 && values[index - 1] >= values[index])
                throw new CborContentException("Provider roles must be strictly increasing.");
        }
        reader.ReadEndArray();
        return values;
    }

    private static SchemaId[] ReadSchemaIds(CborReader reader)
    {
        var count = ReadBoundedArrayCount(reader);
        var values = new SchemaId[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = SchemaId.FromValue(ReadStableId(reader));
            if (index > 0 && StringComparer.Ordinal.Compare(values[index - 1].ToString(), values[index].ToString()) >= 0)
                throw new CborContentException("Provider codec identities must be strictly increasing.");
        }
        reader.ReadEndArray();
        return values;
    }

    private static BoundedAscii[] ReadAliases(CborReader reader)
    {
        var count = ReadBoundedArrayCount(reader);
        var values = new BoundedAscii[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = BoundedAsciiCodec.Read(reader);
            if (index > 0 && values[index - 1].CompareTo(values[index]) >= 0)
                throw new CborContentException("Credential aliases must be strictly increasing.");
        }
        reader.ReadEndArray();
        return values;
    }

    private static int ReadBoundedArrayCount(CborReader reader)
    {
        var count = reader.ReadStartArray();
        if (count is null or < 0 or > ProviderContributionV1.MaximumItemsPerCollection)
            throw new CborContentException("A provider contribution collection must contain at most 256 items.");
        return count.Value;
    }
}

internal static class ProviderCapabilitySetV1Codec
{
    internal static byte[] Encode(ProviderCapabilitySetV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The provider capability set is invalid.", nameof(value));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Write(writer, value);
        return writer.Encode();
    }

    internal static void Write(CborWriter writer, ProviderCapabilitySetV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The provider capability set is invalid.", nameof(value));
        Span<byte> extensionHash = stackalloc byte[32];
        if (!value.ExtensionHash.TryWriteBytes(extensionHash))
            throw new ArgumentException("The provider capability set is invalid.", nameof(value));
        writer.WriteStartMap(3);
        writer.WriteUInt64(1);
        writer.WriteUInt64(value.ManifestVersion);
        writer.WriteUInt64(2);
        writer.WriteUInt64(value.Bits);
        writer.WriteUInt64(3);
        writer.WriteByteString(extensionHash);
        writer.WriteEndMap();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out ProviderCapabilitySetV1 value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            value = Read(reader);
            if (reader.BytesRemaining != 0)
                return false;
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = default;
            return false;
        }
    }

    internal static ProviderCapabilitySetV1 Read(CborReader reader)
    {
        if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1)
            throw new CborContentException("A provider capability set must contain exactly tags 1 through 3.");
        var manifestVersion = reader.ReadUInt64();
        if (manifestVersion is 0 or > ushort.MaxValue || reader.ReadUInt64() != 2)
            throw new CborContentException("The capability manifest version is invalid or tag 2 is missing.");
        var bits = reader.ReadUInt64();
        if (reader.ReadUInt64() != 3)
            throw new CborContentException("Capability tag 3 is missing.");
        Span<byte> extensionHash = stackalloc byte[32];
        if (!reader.TryReadByteString(extensionHash, out var hashLength) || hashLength != 32)
            throw new CborContentException("The capability extension hash must be exactly 32 bytes.");
        reader.ReadEndMap();
        return new ProviderCapabilitySetV1((ushort)manifestVersion, bits, Hash256.FromBytes(extensionHash));
    }
}
