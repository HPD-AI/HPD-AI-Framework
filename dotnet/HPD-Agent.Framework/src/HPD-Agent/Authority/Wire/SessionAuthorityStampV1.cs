using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Authority;

/// <summary>Fences authority operations to one live session and S1 runtime generation.</summary>
public readonly record struct SessionAuthorityStampV1
{
    /// <summary>Initializes a validated session authority stamp.</summary>
    /// <param name="runtimeGenerationId">The current S1-owned runtime generation.</param>
    /// <param name="liveSessionId">The logical live session that survives reconnects.</param>
    /// <exception cref="ArgumentException">Either identifier is the invalid default value.</exception>
    public SessionAuthorityStampV1(RuntimeGenerationId runtimeGenerationId, LiveSessionId liveSessionId)
    {
        if (!runtimeGenerationId.IsValid)
            throw new ArgumentException("A runtime generation is required.", nameof(runtimeGenerationId));
        if (!liveSessionId.IsValid)
            throw new ArgumentException("A live session is required.", nameof(liveSessionId));
        RuntimeGenerationId = runtimeGenerationId;
        LiveSessionId = liveSessionId;
    }

    /// <summary>Gets the current S1-owned runtime generation.</summary>
    public RuntimeGenerationId RuntimeGenerationId { get; }

    /// <summary>Gets the logical live session.</summary>
    public LiveSessionId LiveSessionId { get; }

    /// <summary>Gets whether both required identifiers are non-default.</summary>
    public bool IsValid => RuntimeGenerationId.IsValid && LiveSessionId.IsValid;
}

internal static class SessionAuthorityStampV1Codec
{
    internal const string SchemaId = "hpd.session-authority-stamp.v1";
    internal const ushort Major = 1;
    internal const ushort Minor = 0;

    internal static byte[] Encode(SessionAuthorityStampV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The session authority stamp is invalid.", nameof(value));

        Span<byte> runtime = stackalloc byte[16];
        Span<byte> session = stackalloc byte[16];
        if (!value.RuntimeGenerationId.TryWriteBytes(runtime) || !value.LiveSessionId.TryWriteBytes(session))
            throw new ArgumentException("The session authority stamp is invalid.", nameof(value));

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(2);
        writer.WriteUInt64(1);
        writer.WriteByteString(runtime);
        writer.WriteUInt64(2);
        writer.WriteByteString(session);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out SessionAuthorityStampV1 value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, allowMultipleRootLevelValues: false);
            if (reader.ReadStartMap() != 2 || reader.ReadUInt64() != 1)
                return false;
            var runtime = reader.ReadByteString();
            if (runtime.Length != 16 || reader.ReadUInt64() != 2)
                return false;
            var session = reader.ReadByteString();
            if (session.Length != 16)
                return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;
            value = new(
                RuntimeGenerationId.FromValue(StableId128.FromBytes(runtime)),
                LiveSessionId.FromValue(StableId128.FromBytes(session)));
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    internal static Hash256 ComputeIntegrityHash(SessionAuthorityStampV1 value)
    {
        var canonical = Encode(value);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("hpd-authority\0"u8);
        hash.AppendData(Encoding.UTF8.GetBytes(SchemaId));
        hash.AppendData([0]);
        Span<byte> version = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(version, Major);
        BinaryPrimitives.WriteUInt16BigEndian(version[2..], Minor);
        hash.AppendData(version);
        hash.AppendData(canonical);
        return Hash256.FromBytes(hash.GetHashAndReset());
    }
}
