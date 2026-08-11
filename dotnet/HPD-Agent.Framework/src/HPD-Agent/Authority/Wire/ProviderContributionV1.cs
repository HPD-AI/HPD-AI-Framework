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

internal static class ProviderCapabilitySetV1Codec
{
    internal static byte[] Encode(ProviderCapabilitySetV1 value)
    {
        if (!value.IsValid)
            throw new ArgumentException("The provider capability set is invalid.", nameof(value));
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
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
        return writer.Encode();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> encoded, out ProviderCapabilitySetV1 value)
    {
        value = default;
        try
        {
            var reader = new CborReader(encoded, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 3 || reader.ReadUInt64() != 1)
                return false;
            var manifestVersion = reader.ReadUInt64();
            if (manifestVersion is 0 or > ushort.MaxValue || reader.ReadUInt64() != 2)
                return false;
            var bits = reader.ReadUInt64();
            if (reader.ReadUInt64() != 3)
                return false;
            Span<byte> extensionHash = stackalloc byte[32];
            if (!reader.TryReadByteString(extensionHash, out var hashLength) || hashLength != 32)
                return false;
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0)
                return false;
            value = new ProviderCapabilitySetV1((ushort)manifestVersion, bits, Hash256.FromBytes(extensionHash));
            return true;
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException)
        {
            value = default;
            return false;
        }
    }
}
