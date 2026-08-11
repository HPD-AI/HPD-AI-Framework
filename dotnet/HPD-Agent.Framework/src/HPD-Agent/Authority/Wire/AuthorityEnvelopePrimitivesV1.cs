using System.Formats.Cbor;

namespace HPD.Agent.Authority;

/// <summary>Identifies one exact registered authority schema version.</summary>
public readonly record struct SchemaReferenceV1
{
    /// <summary>Initializes a validated schema reference.</summary>
    /// <param name="schemaId">The stable registered schema identity.</param>
    /// <param name="major">The positive compatibility-major version.</param>
    /// <param name="minor">The compatibility-minor version.</param>
    public SchemaReferenceV1(SchemaId schemaId, ushort major, ushort minor)
    {
        if (!schemaId.IsValid)
            throw new ArgumentException("A schema identity is required.", nameof(schemaId));
        if (major == 0)
            throw new ArgumentOutOfRangeException(nameof(major), "A schema major version must be positive.");
        SchemaId = schemaId;
        Major = major;
        Minor = minor;
    }

    /// <summary>Gets the stable registered schema identity.</summary>
    public SchemaId SchemaId { get; }

    /// <summary>Gets the positive compatibility-major version.</summary>
    public ushort Major { get; }

    /// <summary>Gets the compatibility-minor version.</summary>
    public ushort Minor { get; }

    /// <summary>Gets whether the reference contains a schema identity and positive major version.</summary>
    public bool IsValid => SchemaId.IsValid && Major > 0;
}

/// <summary>Contains bounded integrity evidence for one canonical authority envelope.</summary>
public sealed class IntegrityEnvelopeV1
{
    private readonly byte[] _signature;

    /// <summary>Initializes validated integrity evidence.</summary>
    /// <param name="profile">The positive registered integrity profile.</param>
    /// <param name="keyVersion">The positive integrity-key version.</param>
    /// <param name="digest">The canonical 256-bit digest.</param>
    /// <param name="signature">An optional owned signature of at most 4096 bytes.</param>
    public IntegrityEnvelopeV1(ushort profile, uint keyVersion, Hash256 digest, ReadOnlySpan<byte> signature)
    {
        if (profile == 0)
            throw new ArgumentOutOfRangeException(nameof(profile), "An integrity profile must be positive.");
        if (keyVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(keyVersion), "An integrity key version must be positive.");
        Span<byte> digestBytes = stackalloc byte[32];
        if (!digest.TryWriteBytes(digestBytes))
            throw new ArgumentException("A digest is required.", nameof(digest));
        if (signature.Length > 4096)
            throw new ArgumentOutOfRangeException(nameof(signature), "An integrity signature cannot exceed 4096 bytes.");
        Profile = profile;
        KeyVersion = keyVersion;
        Digest = digest;
        _signature = signature.ToArray();
        Signature = Array.AsReadOnly(_signature);
    }

    /// <summary>Gets the positive registered integrity profile.</summary>
    public ushort Profile { get; }

    /// <summary>Gets the positive integrity-key version.</summary>
    public uint KeyVersion { get; }

    /// <summary>Gets the canonical 256-bit digest.</summary>
    public Hash256 Digest { get; }

    /// <summary>Gets a read-only view of the owned signature bytes.</summary>
    public IReadOnlyList<byte> Signature { get; }

    internal ReadOnlySpan<byte> SignatureBytes => _signature;
}

internal static class AuthorityEnvelopePrimitiveCodecsV1
{
    internal static byte[] Encode(SchemaReferenceV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The schema reference is invalid.", nameof(value));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Span<byte> schema = stackalloc byte[16];
        if (!value.SchemaId.TryWriteBytes(schema))
            throw new ArgumentException("The schema reference is invalid.", nameof(value));
        writer.WriteStartMap(3);
        writer.WriteUInt64(1);
        writer.WriteByteString(schema);
        writer.WriteUInt64(2);
        writer.WriteUInt64(value.Major);
        writer.WriteUInt64(3);
        writer.WriteUInt64(value.Minor);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecodeSchemaReference(ReadOnlyMemory<byte> encoded, out SchemaReferenceV1 value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1)
                return false;
            var schema = reader.ReadByteString();
            if (schema.Length != 16 || reader.ReadUInt64() != 2)
                return false;
            var major = reader.ReadUInt64();
            if (major is 0 or > ushort.MaxValue || reader.ReadUInt64() != 3)
                return false;
            var minor = reader.ReadUInt64();
            if (minor > ushort.MaxValue)
                return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;
            value = new SchemaReferenceV1(SchemaId.FromValue(StableId128.FromBytes(schema)), (ushort)major, (ushort)minor);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = default;
            return false;
        }
    }

    internal static byte[] Encode(IntegrityEnvelopeV1 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        Span<byte> digest = stackalloc byte[32];
        if (!value.Digest.TryWriteBytes(digest) || value.Profile == 0 || value.KeyVersion == 0 || value.Signature.Count > 4096)
            throw new ArgumentException("The integrity envelope is invalid.", nameof(value));
        writer.WriteStartMap(4);
        writer.WriteUInt64(1);
        writer.WriteUInt64(value.Profile);
        writer.WriteUInt64(2);
        writer.WriteUInt64(value.KeyVersion);
        writer.WriteUInt64(3);
        writer.WriteByteString(digest);
        writer.WriteUInt64(4);
        writer.WriteByteString(value.SignatureBytes);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecodeIntegrityEnvelope(ReadOnlyMemory<byte> encoded, out IntegrityEnvelopeV1? value)
    {
        value = null;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1)
                return false;
            var profile = reader.ReadUInt64();
            if (profile is 0 or > ushort.MaxValue || reader.ReadUInt64() != 2)
                return false;
            var keyVersion = reader.ReadUInt64();
            if (keyVersion is 0 or > uint.MaxValue || reader.ReadUInt64() != 3)
                return false;
            var digest = reader.ReadByteString();
            if (digest.Length != 32 || reader.ReadUInt64() != 4)
                return false;
            var signature = reader.ReadByteString();
            if (signature.Length > 4096)
                return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;
            value = new IntegrityEnvelopeV1((ushort)profile, (uint)keyVersion, Hash256.FromBytes(digest), signature);
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = null;
            return false;
        }
    }
}
