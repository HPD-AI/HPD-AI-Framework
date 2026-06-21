#nullable enable

using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using HPD.Media.Diagnostics;
using HPD.Media.Transport;

namespace HPD.Media.Srtp;

/// <summary>
/// Configures SRTP and SRTCP packet protection behavior.
/// </summary>
public sealed class SrtpPacketProtectionOptions
{
    /// <summary>Gets the replay window size in packets.</summary>
    public int ReplayWindowSize { get; init; } = 64;

    /// <summary>Gets a value indicating whether MKI values are accepted when present.</summary>
    public bool AllowMki { get; init; }
}

/// <summary>
/// Creates SRTP packet protector factories from role-resolved protection material.
/// </summary>
public interface ISrtpPacketProtectorFactoryBuilder
{
    /// <summary>Creates a packet protector factory for a material set.</summary>
    IPacketProtectorFactory Create(SrtpProtectionMaterial material, SrtpPacketProtectionOptions options);
}

/// <summary>
/// Builds SRTP packet protectors for the supported AES-CM/HMAC-SHA1 and AES-GCM profiles.
/// </summary>
public sealed class AesCmSha1SrtpPacketProtectorFactoryBuilder : ISrtpPacketProtectorFactoryBuilder
{
    /// <inheritdoc />
    public IPacketProtectorFactory Create(SrtpProtectionMaterial material, SrtpPacketProtectionOptions options)
    {
        return CreateCore(material, options, default, hasTelemetry: false);
    }

    /// <summary>Creates a packet protector factory with cached SRTP reject telemetry emitters.</summary>
    public IPacketProtectorFactory Create(
        SrtpProtectionMaterial material,
        SrtpPacketProtectionOptions options,
        RealtimeMediaTelemetryEmitters telemetry)
    {
        return CreateCore(material, options, telemetry, hasTelemetry: true);
    }

    private static IPacketProtectorFactory CreateCore(
        SrtpProtectionMaterial material,
        SrtpPacketProtectionOptions options,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (material.Mki.Length > 0 && !options.AllowMki)
        {
            return new UnsupportedPacketProtectorFactory();
        }

        if (options.ReplayWindowSize is < 1 or > 64)
        {
            return new UnsupportedPacketProtectorFactory();
        }

        return material.Profile switch
        {
            SrtpProtectionProfile.Aes128CmHmacSha1_80 when HasValidAesCmMaterial(material) =>
                new AesCmSha1PacketProtectorFactory(material, options, authenticationTagBytes: 10, telemetry, hasTelemetry),
            SrtpProtectionProfile.Aes128CmHmacSha1_32 when HasValidAesCmMaterial(material) =>
                new AesCmSha1PacketProtectorFactory(material, options, authenticationTagBytes: 4, telemetry, hasTelemetry),
            SrtpProtectionProfile.AeadAes128Gcm when HasValidAeadAes128GcmMaterial(material) =>
                new AeadAes128GcmPacketProtectorFactory(material, options, telemetry, hasTelemetry),
            _ => new UnsupportedPacketProtectorFactory()
        };
    }

    private static bool HasValidAesCmMaterial(SrtpProtectionMaterial material)
    {
        return material.OutboundMasterKey.Length == AesCmSha1KeyDerivation.MasterKeyBytes &&
            material.InboundMasterKey.Length == AesCmSha1KeyDerivation.MasterKeyBytes &&
            material.OutboundMasterSalt.Length == AesCmSha1KeyDerivation.MasterSaltBytes &&
            material.InboundMasterSalt.Length == AesCmSha1KeyDerivation.MasterSaltBytes;
    }

    private static bool HasValidAeadAes128GcmMaterial(SrtpProtectionMaterial material)
    {
        return material.OutboundMasterKey.Length == AeadAes128GcmKeyDerivation.MasterKeyBytes &&
            material.InboundMasterKey.Length == AeadAes128GcmKeyDerivation.MasterKeyBytes &&
            material.OutboundMasterSalt.Length == AeadAes128GcmKeyDerivation.MasterSaltBytes &&
            material.InboundMasterSalt.Length == AeadAes128GcmKeyDerivation.MasterSaltBytes;
    }
}

internal static class AesCmSha1KeyDerivation
{
    internal const int MasterKeyBytes = 16;
    internal const int MasterSaltBytes = 14;
    internal const int SessionEncryptionKeyBytes = 16;
    internal const int SessionSaltBytes = 14;
    internal const int SessionAuthenticationKeyBytes = 94;

    internal static AesCmSha1SessionKeys DeriveRtp(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> masterSalt)
    {
        return new AesCmSha1SessionKeys(
            Derive(masterKey, masterSalt, 0x00, SessionEncryptionKeyBytes),
            Derive(masterKey, masterSalt, 0x01, SessionAuthenticationKeyBytes),
            Derive(masterKey, masterSalt, 0x02, SessionSaltBytes));
    }

    internal static AesCmSha1SessionKeys DeriveRtcp(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> masterSalt)
    {
        return new AesCmSha1SessionKeys(
            Derive(masterKey, masterSalt, 0x03, SessionEncryptionKeyBytes),
            Derive(masterKey, masterSalt, 0x04, SessionAuthenticationKeyBytes),
            Derive(masterKey, masterSalt, 0x05, SessionSaltBytes));
    }

    internal static byte[] Derive(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> masterSalt, byte label, int outputLength)
    {
        if (masterKey.Length != MasterKeyBytes || masterSalt.Length != MasterSaltBytes)
        {
            throw new ArgumentException("AES-CM SRTP requires a 16-byte master key and 14-byte master salt.");
        }

        byte[] output = new byte[outputLength];
        Span<byte> counter = stackalloc byte[16];
        masterSalt.CopyTo(counter);
        counter[7] ^= label;

        using Aes aes = Aes.Create();
        aes.Key = masterKey.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        Span<byte> block = stackalloc byte[16];
        int written = 0;
        ushort blockCounter = 0;
        while (written < outputLength)
        {
            BinaryPrimitives.WriteUInt16BigEndian(counter[14..], blockCounter++);
            aes.EncryptEcb(counter, block, PaddingMode.None);
            int copy = Math.Min(block.Length, outputLength - written);
            block[..copy].CopyTo(output.AsSpan(written, copy));
            written += copy;
        }

        return output;
    }
}

internal sealed class AesCmSha1PacketProtectorFactory : IPacketProtectorFactory
{
    private readonly SrtpProtectionMaterial material;
    private readonly SrtpPacketProtectionOptions options;
    private readonly int authenticationTagBytes;
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;
    private readonly byte[] mki;

    internal AesCmSha1PacketProtectorFactory(
        SrtpProtectionMaterial material,
        SrtpPacketProtectionOptions options,
        int authenticationTagBytes,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
    {
        this.material = material;
        this.options = options;
        this.authenticationTagBytes = authenticationTagBytes;
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        mki = material.Mki.Length == 0 ? [] : material.Mki.ToArray();
    }

    public IPacketProtector Create(PacketProtectionPurpose purpose, PacketDirection direction, uint ssrc)
    {
        if (purpose is not (PacketProtectionPurpose.Rtp or PacketProtectionPurpose.Rtcp) ||
            direction is not (PacketDirection.Inbound or PacketDirection.Outbound))
        {
            return new UnsupportedPacketProtector();
        }

        ReadOnlyMemory<byte> masterKey = direction == PacketDirection.Outbound
            ? material.OutboundMasterKey
            : material.InboundMasterKey;
        ReadOnlyMemory<byte> masterSalt = direction == PacketDirection.Outbound
            ? material.OutboundMasterSalt
            : material.InboundMasterSalt;

        AesCmSha1SessionKeys keys = purpose == PacketProtectionPurpose.Rtp
            ? AesCmSha1KeyDerivation.DeriveRtp(masterKey.Span, masterSalt.Span)
            : AesCmSha1KeyDerivation.DeriveRtcp(masterKey.Span, masterSalt.Span);

        return purpose == PacketProtectionPurpose.Rtp
            ? new AesCmSha1RtpPacketProtector(keys, direction, ssrc, authenticationTagBytes, options.ReplayWindowSize, mki, telemetry, hasTelemetry)
            : new AesCmSha1RtcpPacketProtector(keys, direction, ssrc, authenticationTagBytes, options.ReplayWindowSize, mki, telemetry, hasTelemetry);
    }
}

internal readonly struct AesCmSha1SessionKeys
{
    internal AesCmSha1SessionKeys(byte[] encryptionKey, byte[] authenticationKey, byte[] salt)
    {
        EncryptionKey = encryptionKey;
        AuthenticationKey = authenticationKey;
        Salt = salt;
    }

    internal byte[] EncryptionKey { get; }

    internal byte[] AuthenticationKey { get; }

    internal byte[] Salt { get; }
}

internal static class AeadAes128GcmKeyDerivation
{
    internal const int MasterKeyBytes = 16;
    internal const int MasterSaltBytes = 12;
    internal const int SessionKeyBytes = 16;
    internal const int SessionSaltBytes = 12;
    internal const int AuthenticationTagBytes = 16;

    internal static AeadAes128GcmSessionKeys DeriveRtp(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> masterSalt)
    {
        return new AeadAes128GcmSessionKeys(
            Derive(masterKey, masterSalt, 0x00, SessionKeyBytes),
            Derive(masterKey, masterSalt, 0x02, SessionSaltBytes));
    }

    internal static AeadAes128GcmSessionKeys DeriveRtcp(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> masterSalt)
    {
        return new AeadAes128GcmSessionKeys(
            Derive(masterKey, masterSalt, 0x03, SessionKeyBytes),
            Derive(masterKey, masterSalt, 0x05, SessionSaltBytes));
    }

    internal static byte[] Derive(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> masterSalt, byte label, int outputLength)
    {
        if (masterKey.Length != MasterKeyBytes || masterSalt.Length != MasterSaltBytes)
        {
            throw new ArgumentException("AEAD AES-128-GCM SRTP requires a 16-byte master key and 12-byte master salt.");
        }

        byte[] output = new byte[outputLength];
        Span<byte> counter = stackalloc byte[16];
        masterSalt.CopyTo(counter);
        counter[7] ^= label;

        using Aes aes = Aes.Create();
        aes.Key = masterKey.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        Span<byte> block = stackalloc byte[16];
        int written = 0;
        ushort blockCounter = 0;
        while (written < outputLength)
        {
            BinaryPrimitives.WriteUInt16BigEndian(counter[14..], blockCounter++);
            aes.EncryptEcb(counter, block, PaddingMode.None);
            int copy = Math.Min(block.Length, outputLength - written);
            block[..copy].CopyTo(output.AsSpan(written, copy));
            written += copy;
        }

        return output;
    }
}

internal readonly struct AeadAes128GcmSessionKeys
{
    internal AeadAes128GcmSessionKeys(byte[] encryptionKey, byte[] salt)
    {
        EncryptionKey = encryptionKey;
        Salt = salt;
    }

    internal byte[] EncryptionKey { get; }

    internal byte[] Salt { get; }
}

internal sealed class AeadAes128GcmPacketProtectorFactory : IPacketProtectorFactory
{
    private readonly SrtpProtectionMaterial material;
    private readonly SrtpPacketProtectionOptions options;
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;
    private readonly byte[] mki;

    internal AeadAes128GcmPacketProtectorFactory(
        SrtpProtectionMaterial material,
        SrtpPacketProtectionOptions options,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
    {
        this.material = material;
        this.options = options;
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        mki = material.Mki.Length == 0 ? [] : material.Mki.ToArray();
    }

    public IPacketProtector Create(PacketProtectionPurpose purpose, PacketDirection direction, uint ssrc)
    {
        if (purpose is not (PacketProtectionPurpose.Rtp or PacketProtectionPurpose.Rtcp) ||
            direction is not (PacketDirection.Inbound or PacketDirection.Outbound))
        {
            return new UnsupportedPacketProtector();
        }

        ReadOnlyMemory<byte> masterKey = direction == PacketDirection.Outbound
            ? material.OutboundMasterKey
            : material.InboundMasterKey;
        ReadOnlyMemory<byte> masterSalt = direction == PacketDirection.Outbound
            ? material.OutboundMasterSalt
            : material.InboundMasterSalt;

        AeadAes128GcmSessionKeys keys = purpose == PacketProtectionPurpose.Rtp
            ? AeadAes128GcmKeyDerivation.DeriveRtp(masterKey.Span, masterSalt.Span)
            : AeadAes128GcmKeyDerivation.DeriveRtcp(masterKey.Span, masterSalt.Span);

        return purpose == PacketProtectionPurpose.Rtp
            ? new AeadAes128GcmRtpPacketProtector(keys, direction, ssrc, options.ReplayWindowSize, mki, telemetry, hasTelemetry)
            : new AeadAes128GcmRtcpPacketProtector(keys, direction, ssrc, options.ReplayWindowSize, mki, telemetry, hasTelemetry);
    }
}

internal abstract class AeadAes128GcmPacketProtector : IPacketProtector, IDisposable
{
    private const int MaximumPacketBytes = 65_535;
    private readonly AesGcm gcm;
    private readonly byte[] nonceScratch;
    private readonly byte[] packetScratch;
    private readonly byte[] salt;
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;

    protected AeadAes128GcmPacketProtector(
        AeadAes128GcmSessionKeys keys,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
    {
        gcm = new AesGcm(keys.EncryptionKey, AeadAes128GcmKeyDerivation.AuthenticationTagBytes);
        salt = keys.Salt;
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        nonceScratch = new byte[AeadAes128GcmKeyDerivation.SessionSaltBytes];
        packetScratch = new byte[MaximumPacketBytes];
    }

    public abstract int MaximumExpansionBytes { get; }

    public abstract PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength);

    public abstract PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength);

    public void Dispose()
    {
        gcm.Dispose();
    }

    protected void EmitReject(PacketProtectionStatus status, uint ssrc, bool isRtcp)
    {
        if (!hasTelemetry || status == PacketProtectionStatus.Success)
        {
            return;
        }

        _ = telemetry.SrtpReject.Emit(new SrtpRejectSample
        {
            Ssrc = ssrc,
            RejectKind = ToRejectKind(status),
            IsRtcp = isRtcp
        });
    }

    protected static bool HasRtpFamilyVersion(ReadOnlySpan<byte> packet)
    {
        return packet.Length > 0 && (packet[0] & 0xC0) == 0x80;
    }

    protected bool TryEncrypt(
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        ReadOnlySpan<byte> associatedData)
    {
        if (plaintext.Length > packetScratch.Length ||
            ciphertext.Length < plaintext.Length ||
            tag.Length < AeadAes128GcmKeyDerivation.AuthenticationTagBytes)
        {
            return false;
        }

        plaintext.CopyTo(packetScratch);
        gcm.Encrypt(nonce, packetScratch.AsSpan(0, plaintext.Length), ciphertext[..plaintext.Length], tag[..AeadAes128GcmKeyDerivation.AuthenticationTagBytes], associatedData);
        return true;
    }

    protected bool TryDecrypt(
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        ReadOnlySpan<byte> associatedData)
    {
        if (ciphertext.Length > packetScratch.Length ||
            plaintext.Length < ciphertext.Length ||
            tag.Length < AeadAes128GcmKeyDerivation.AuthenticationTagBytes)
        {
            return false;
        }

        try
        {
            gcm.Decrypt(nonce, ciphertext, tag[..AeadAes128GcmKeyDerivation.AuthenticationTagBytes], packetScratch.AsSpan(0, ciphertext.Length), associatedData);
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }

        packetScratch.AsSpan(0, ciphertext.Length).CopyTo(plaintext);
        return true;
    }

    protected ReadOnlySpan<byte> WriteRtpNonce(uint ssrc, uint rolloverCounter, ushort sequenceNumber)
    {
        Span<byte> nonce = nonceScratch;
        nonce.Clear();
        salt.CopyTo(nonce);
        nonce[2] ^= (byte)(ssrc >> 24);
        nonce[3] ^= (byte)(ssrc >> 16);
        nonce[4] ^= (byte)(ssrc >> 8);
        nonce[5] ^= (byte)ssrc;
        nonce[6] ^= (byte)(rolloverCounter >> 24);
        nonce[7] ^= (byte)(rolloverCounter >> 16);
        nonce[8] ^= (byte)(rolloverCounter >> 8);
        nonce[9] ^= (byte)rolloverCounter;
        nonce[10] ^= (byte)(sequenceNumber >> 8);
        nonce[11] ^= (byte)sequenceNumber;
        return nonce;
    }

    protected ReadOnlySpan<byte> WriteRtcpNonce(uint ssrc, uint index)
    {
        Span<byte> nonce = nonceScratch;
        nonce.Clear();
        salt.CopyTo(nonce);
        nonce[2] ^= (byte)(ssrc >> 24);
        nonce[3] ^= (byte)(ssrc >> 16);
        nonce[4] ^= (byte)(ssrc >> 8);
        nonce[5] ^= (byte)ssrc;
        nonce[8] ^= (byte)(index >> 24);
        nonce[9] ^= (byte)(index >> 16);
        nonce[10] ^= (byte)(index >> 8);
        nonce[11] ^= (byte)index;
        return nonce;
    }

    protected bool TryCopyAssociatedData(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, out ReadOnlySpan<byte> associatedData)
    {
        associatedData = default;
        int length = first.Length + second.Length;
        if (length > packetScratch.Length)
        {
            return false;
        }

        Span<byte> destination = packetScratch.AsSpan(0, length);
        first.CopyTo(destination);
        second.CopyTo(destination[first.Length..]);
        associatedData = destination;
        return true;
    }

    protected static bool TryGetRtpPayloadOffset(ReadOnlySpan<byte> packet, out int payloadOffset)
    {
        payloadOffset = 0;
        if (packet.Length < 12)
        {
            return false;
        }

        int csrcCount = packet[0] & 0x0F;
        int headerLength = 12 + csrcCount * 4;
        if (packet.Length < headerLength)
        {
            return false;
        }

        if ((packet[0] & 0x10) != 0)
        {
            if (packet.Length < headerLength + 4)
            {
                return false;
            }

            int extensionWords = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(headerLength + 2, 2));
            headerLength += 4 + extensionWords * 4;
            if (packet.Length < headerLength)
            {
                return false;
            }
        }

        payloadOffset = headerLength;
        return true;
    }

    private static SrtpRejectKind ToRejectKind(PacketProtectionStatus status)
    {
        return status switch
        {
            PacketProtectionStatus.AuthenticationFailed => SrtpRejectKind.AuthenticationFailed,
            PacketProtectionStatus.ReplayRejected => SrtpRejectKind.ReplayRejected,
            PacketProtectionStatus.WrongSsrc => SrtpRejectKind.WrongSsrc,
            PacketProtectionStatus.UnsupportedProfile => SrtpRejectKind.UnsupportedProfile,
            _ => SrtpRejectKind.InvalidPacket
        };
    }
}

internal sealed class AeadAes128GcmRtpPacketProtector : AeadAes128GcmPacketProtector
{
    private readonly PacketDirection direction;
    private readonly byte[] mki;
    private readonly ReplayWindow replayWindow;
    private readonly uint ssrc;
    private bool hasReceivedPacket;
    private ushort highestSequenceNumber;
    private uint rolloverCounter;

    internal AeadAes128GcmRtpPacketProtector(
        AeadAes128GcmSessionKeys keys,
        PacketDirection direction,
        uint ssrc,
        int replayWindowSize,
        byte[] mki,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
        : base(keys, telemetry, hasTelemetry)
    {
        this.direction = direction;
        this.ssrc = ssrc;
        this.mki = mki;
        replayWindow = new ReplayWindow(replayWindowSize);
    }

    public override int MaximumExpansionBytes => AeadAes128GcmKeyDerivation.AuthenticationTagBytes + mki.Length;

    public override PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Outbound || inputLength < 12 || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        if (inputLength > packet.Length - MaximumExpansionBytes)
        {
            EmitReject(PacketProtectionStatus.DestinationTooSmall, ssrc, isRtcp: false);
            return PacketProtectionStatus.DestinationTooSmall;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[8..12]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.WrongSsrc;
        }

        if (!TryGetRtpPayloadOffset(packet[..inputLength], out int payloadOffset))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        ushort sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        uint roc = UpdateOutboundRolloverCounter(sequenceNumber);
        ReadOnlySpan<byte> nonce = WriteRtpNonce(ssrc, roc, sequenceNumber);
        Span<byte> payload = packet[payloadOffset..inputLength];
        Span<byte> tag = packet.Slice(inputLength, AeadAes128GcmKeyDerivation.AuthenticationTagBytes);
        if (!TryEncrypt(nonce, payload, payload, tag, packet[..payloadOffset]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        mki.CopyTo(packet.Slice(inputLength + AeadAes128GcmKeyDerivation.AuthenticationTagBytes, mki.Length));
        outputLength = inputLength + MaximumExpansionBytes;
        return PacketProtectionStatus.Success;
    }

    public override PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Inbound || inputLength < 12 + MaximumExpansionBytes || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        int protectedLength = inputLength - MaximumExpansionBytes;
        if (protectedLength < 12)
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[8..12]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.WrongSsrc;
        }

        if (mki.Length > 0 &&
            !CryptographicOperations.FixedTimeEquals(packet.Slice(protectedLength + AeadAes128GcmKeyDerivation.AuthenticationTagBytes, mki.Length), mki))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        if (!TryGetRtpPayloadOffset(packet[..protectedLength], out int payloadOffset))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        ushort sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        uint guessedRoc = GuessInboundRolloverCounter(sequenceNumber);
        ulong packetIndex = ((ulong)guessedRoc << 16) | sequenceNumber;
        if (replayWindow.WouldReplay(packetIndex))
        {
            EmitReject(PacketProtectionStatus.ReplayRejected, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.ReplayRejected;
        }

        ReadOnlySpan<byte> nonce = WriteRtpNonce(ssrc, guessedRoc, sequenceNumber);
        Span<byte> payload = packet[payloadOffset..protectedLength];
        ReadOnlySpan<byte> tag = packet.Slice(protectedLength, AeadAes128GcmKeyDerivation.AuthenticationTagBytes);
        if (!TryDecrypt(nonce, payload, tag, payload, packet[..payloadOffset]))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        replayWindow.Mark(packetIndex);
        UpdateInboundRolloverCounter(sequenceNumber, guessedRoc);
        outputLength = protectedLength;
        return PacketProtectionStatus.Success;
    }

    private uint UpdateOutboundRolloverCounter(ushort sequenceNumber)
    {
        if (hasReceivedPacket && highestSequenceNumber > 0xF000 && sequenceNumber < 0x1000)
        {
            rolloverCounter++;
        }

        highestSequenceNumber = sequenceNumber;
        hasReceivedPacket = true;
        return rolloverCounter;
    }

    private uint GuessInboundRolloverCounter(ushort sequenceNumber)
    {
        if (!hasReceivedPacket)
        {
            return 0;
        }

        if (highestSequenceNumber < 0x8000)
        {
            return sequenceNumber - highestSequenceNumber > 0x8000 && rolloverCounter > 0
                ? rolloverCounter - 1
                : rolloverCounter;
        }

        return highestSequenceNumber - 0x8000 > sequenceNumber
            ? rolloverCounter + 1
            : rolloverCounter;
    }

    private void UpdateInboundRolloverCounter(ushort sequenceNumber, uint guessedRoc)
    {
        ulong currentHighestIndex = ((ulong)rolloverCounter << 16) | highestSequenceNumber;
        ulong guessedIndex = ((ulong)guessedRoc << 16) | sequenceNumber;

        if (!hasReceivedPacket || guessedIndex > currentHighestIndex)
        {
            rolloverCounter = guessedRoc;
            highestSequenceNumber = sequenceNumber;
            hasReceivedPacket = true;
        }
    }
}

internal sealed class AeadAes128GcmRtcpPacketProtector : AeadAes128GcmPacketProtector
{
    private readonly PacketDirection direction;
    private readonly byte[] mki;
    private readonly ReplayWindow replayWindow;
    private readonly uint ssrc;
    private uint outboundIndex;

    internal AeadAes128GcmRtcpPacketProtector(
        AeadAes128GcmSessionKeys keys,
        PacketDirection direction,
        uint ssrc,
        int replayWindowSize,
        byte[] mki,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
        : base(keys, telemetry, hasTelemetry)
    {
        this.direction = direction;
        this.ssrc = ssrc;
        this.mki = mki;
        replayWindow = new ReplayWindow(replayWindowSize);
    }

    public override int MaximumExpansionBytes => AeadAes128GcmKeyDerivation.AuthenticationTagBytes + 4 + mki.Length;

    public override PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Outbound || inputLength < 8 || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        if (inputLength > packet.Length - MaximumExpansionBytes)
        {
            EmitReject(PacketProtectionStatus.DestinationTooSmall, ssrc, isRtcp: true);
            return PacketProtectionStatus.DestinationTooSmall;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[4..8]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.WrongSsrc;
        }

        uint index = outboundIndex++ & 0x7FFFFFFF;
        uint encryptedIndex = index | 0x80000000u;
        Span<byte> indexBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(indexBytes, encryptedIndex);
        if (!TryCopyAssociatedData(packet[..8], indexBytes, out ReadOnlySpan<byte> associatedData))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        ReadOnlySpan<byte> nonce = WriteRtcpNonce(ssrc, index);
        Span<byte> plaintext = packet[8..inputLength];
        Span<byte> tag = packet.Slice(inputLength, AeadAes128GcmKeyDerivation.AuthenticationTagBytes);
        if (!TryEncrypt(nonce, plaintext, plaintext, tag, associatedData))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        indexBytes.CopyTo(packet.Slice(inputLength + AeadAes128GcmKeyDerivation.AuthenticationTagBytes, 4));
        mki.CopyTo(packet.Slice(inputLength + AeadAes128GcmKeyDerivation.AuthenticationTagBytes + 4, mki.Length));
        outputLength = inputLength + MaximumExpansionBytes;
        return PacketProtectionStatus.Success;
    }

    public override PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Inbound || inputLength < 8 + MaximumExpansionBytes || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        int protectedLength = inputLength - MaximumExpansionBytes;
        if (protectedLength < 8)
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[4..8]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.WrongSsrc;
        }

        int indexOffset = protectedLength + AeadAes128GcmKeyDerivation.AuthenticationTagBytes;
        if (mki.Length > 0 &&
            !CryptographicOperations.FixedTimeEquals(packet.Slice(indexOffset + 4, mki.Length), mki))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        uint encryptedIndex = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(indexOffset, 4));
        bool encrypted = (encryptedIndex & 0x80000000u) != 0;
        uint index = encryptedIndex & 0x7FFFFFFFu;
        if (replayWindow.WouldReplay(index))
        {
            EmitReject(PacketProtectionStatus.ReplayRejected, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.ReplayRejected;
        }

        if (!encrypted)
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        ReadOnlySpan<byte> indexBytes = packet.Slice(indexOffset, 4);
        if (!TryCopyAssociatedData(packet[..8], indexBytes, out ReadOnlySpan<byte> associatedData))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        ReadOnlySpan<byte> nonce = WriteRtcpNonce(ssrc, index);
        Span<byte> ciphertext = packet[8..protectedLength];
        ReadOnlySpan<byte> tag = packet.Slice(protectedLength, AeadAes128GcmKeyDerivation.AuthenticationTagBytes);
        if (!TryDecrypt(nonce, ciphertext, tag, ciphertext, associatedData))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        replayWindow.Mark(index);
        outputLength = protectedLength;
        return PacketProtectionStatus.Success;
    }
}

internal abstract class AesCmSha1PacketProtector : IPacketProtector, IDisposable
{
    private readonly Aes aes;
    private readonly byte[] authenticationScratch;
    private readonly byte[] counterScratch;
    private readonly ICryptoTransform encryptor;
    private readonly NoAllocHmacSha1 authenticator;
    private readonly byte[] keystreamScratch;
    private readonly byte[] salt;
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;
    private const int MaximumAuthenticationInputBytes = 65539;

    protected AesCmSha1PacketProtector(
        AesCmSha1SessionKeys keys,
        int authenticationTagBytes,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
    {
        AuthenticationTagBytes = authenticationTagBytes;
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        salt = keys.Salt;
        authenticator = new NoAllocHmacSha1(keys.AuthenticationKey, MaximumAuthenticationInputBytes);
        authenticationScratch = new byte[MaximumAuthenticationInputBytes];
        aes = Aes.Create();
        aes.Key = keys.EncryptionKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        encryptor = aes.CreateEncryptor();
        counterScratch = new byte[16];
        keystreamScratch = new byte[16];
    }

    public abstract int MaximumExpansionBytes { get; }

    protected int AuthenticationTagBytes { get; }

    public abstract PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength);

    public abstract PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength);

    protected void EmitReject(PacketProtectionStatus status, uint ssrc, bool isRtcp)
    {
        if (!hasTelemetry || status == PacketProtectionStatus.Success)
        {
            return;
        }

        _ = telemetry.SrtpReject.Emit(new SrtpRejectSample
        {
            Ssrc = ssrc,
            RejectKind = ToRejectKind(status),
            IsRtcp = isRtcp
        });
    }

    protected static bool HasRtpFamilyVersion(ReadOnlySpan<byte> packet)
    {
        return packet.Length > 0 && (packet[0] & 0xC0) == 0x80;
    }

    public void Dispose()
    {
        encryptor.Dispose();
        aes.Dispose();
    }

    protected void ApplyCipher(ReadOnlySpan<byte> counterBase, Span<byte> payload)
    {
        counterBase.CopyTo(counterScratch);

        ushort counterValue = 0;
        int offset = 0;
        while (offset < payload.Length)
        {
            BinaryPrimitives.WriteUInt16BigEndian(counterScratch.AsSpan(14, 2), counterValue++);
            _ = encryptor.TransformBlock(counterScratch, 0, counterScratch.Length, keystreamScratch, 0);
            int blockLength = Math.Min(keystreamScratch.Length, payload.Length - offset);
            Xor(payload.Slice(offset, blockLength), keystreamScratch.AsSpan(0, blockLength));
            offset += blockLength;
        }
    }

    protected bool TryWriteAuthenticationTag(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> suffix, Span<byte> destination)
    {
        if (!TryCopyAuthenticationInput(packet, suffix, out Span<byte> input))
        {
            return false;
        }

        Span<byte> hash = stackalloc byte[20];
        if (!authenticator.TryComputeHash(input, hash))
        {
            return false;
        }

        hash[..AuthenticationTagBytes].CopyTo(destination);
        return true;
    }

    protected bool VerifyAuthenticationTag(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> suffix, ReadOnlySpan<byte> expectedTag)
    {
        if (!TryCopyAuthenticationInput(packet, suffix, out Span<byte> input))
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[20];
        if (!authenticator.TryComputeHash(input, actual))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(actual[..expectedTag.Length], expectedTag);
    }

    protected bool CanAuthenticate(int packetLength, int suffixLength)
    {
        return packetLength >= 0 &&
            suffixLength >= 0 &&
            packetLength <= authenticationScratch.Length - suffixLength;
    }

    private bool TryCopyAuthenticationInput(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> suffix, out Span<byte> input)
    {
        input = default;
        int inputLength = packet.Length + suffix.Length;
        if (inputLength > authenticationScratch.Length)
        {
            return false;
        }

        input = authenticationScratch.AsSpan(0, inputLength);
        packet.CopyTo(input);
        suffix.CopyTo(input[packet.Length..]);
        return true;
    }

    protected void WriteRtpCounter(uint ssrc, uint rolloverCounter, ushort sequenceNumber, Span<byte> destination)
    {
        destination.Clear();
        salt.CopyTo(destination);
        destination[4] ^= (byte)(ssrc >> 24);
        destination[5] ^= (byte)(ssrc >> 16);
        destination[6] ^= (byte)(ssrc >> 8);
        destination[7] ^= (byte)ssrc;
        destination[8] ^= (byte)(rolloverCounter >> 24);
        destination[9] ^= (byte)(rolloverCounter >> 16);
        destination[10] ^= (byte)(rolloverCounter >> 8);
        destination[11] ^= (byte)rolloverCounter;
        destination[12] ^= (byte)(sequenceNumber >> 8);
        destination[13] ^= (byte)sequenceNumber;
    }

    protected void WriteRtcpCounter(uint ssrc, uint index, Span<byte> destination)
    {
        destination.Clear();
        salt.CopyTo(destination);
        destination[4] ^= (byte)(ssrc >> 24);
        destination[5] ^= (byte)(ssrc >> 16);
        destination[6] ^= (byte)(ssrc >> 8);
        destination[7] ^= (byte)ssrc;
        destination[10] ^= (byte)(index >> 24);
        destination[11] ^= (byte)(index >> 16);
        destination[12] ^= (byte)(index >> 8);
        destination[13] ^= (byte)index;
    }

    private static void Xor(Span<byte> destination, ReadOnlySpan<byte> keystream)
    {
        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] ^= keystream[i];
        }
    }

    private static SrtpRejectKind ToRejectKind(PacketProtectionStatus status)
    {
        return status switch
        {
            PacketProtectionStatus.AuthenticationFailed => SrtpRejectKind.AuthenticationFailed,
            PacketProtectionStatus.ReplayRejected => SrtpRejectKind.ReplayRejected,
            PacketProtectionStatus.WrongSsrc => SrtpRejectKind.WrongSsrc,
            PacketProtectionStatus.UnsupportedProfile => SrtpRejectKind.UnsupportedProfile,
            _ => SrtpRejectKind.InvalidPacket
        };
    }
}

internal sealed class AesCmSha1RtpPacketProtector : AesCmSha1PacketProtector
{
    private readonly PacketDirection direction;
    private readonly byte[] mki;
    private readonly uint ssrc;
    private readonly ReplayWindow replayWindow;
    private ushort highestSequenceNumber;
    private uint rolloverCounter;
    private bool hasReceivedPacket;

    internal AesCmSha1RtpPacketProtector(
        AesCmSha1SessionKeys keys,
        PacketDirection direction,
        uint ssrc,
        int authenticationTagBytes,
        int replayWindowSize,
        byte[] mki,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
        : base(keys, authenticationTagBytes, telemetry, hasTelemetry)
    {
        this.direction = direction;
        this.ssrc = ssrc;
        this.mki = mki;
        replayWindow = new ReplayWindow(replayWindowSize);
    }

    public override int MaximumExpansionBytes => mki.Length + AuthenticationTagBytes;

    public override PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Outbound || inputLength < 12 || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        if (inputLength > packet.Length - MaximumExpansionBytes)
        {
            EmitReject(PacketProtectionStatus.DestinationTooSmall, ssrc, isRtcp: false);
            return PacketProtectionStatus.DestinationTooSmall;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[8..12]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.WrongSsrc;
        }

        int authenticatedLength = inputLength + mki.Length;
        if (!CanAuthenticate(authenticatedLength, suffixLength: 4))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        if (!TryGetRtpPayload(packet[..inputLength], out Span<byte> payload))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        ushort sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        uint roc = UpdateOutboundRolloverCounter(sequenceNumber);
        Span<byte> counter = stackalloc byte[16];
        WriteRtpCounter(ssrc, roc, sequenceNumber, counter);
        ApplyCipher(counter, payload);

        Span<byte> rocBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(rocBytes, roc);
        mki.CopyTo(packet.Slice(inputLength, mki.Length));
        if (!TryWriteAuthenticationTag(packet[..authenticatedLength], rocBytes, packet.Slice(authenticatedLength, AuthenticationTagBytes)))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        outputLength = authenticatedLength + AuthenticationTagBytes;
        return PacketProtectionStatus.Success;
    }

    public override PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Inbound || inputLength < 12 + MaximumExpansionBytes || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        int authenticatedLength = inputLength - AuthenticationTagBytes;
        int protectedLength = authenticatedLength - mki.Length;
        if (protectedLength < 12)
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[8..12]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.WrongSsrc;
        }

        if (mki.Length > 0 && !CryptographicOperations.FixedTimeEquals(packet.Slice(protectedLength, mki.Length), mki))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        ushort sequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        uint guessedRoc = GuessInboundRolloverCounter(sequenceNumber);
        ulong packetIndex = ((ulong)guessedRoc << 16) | sequenceNumber;
        if (replayWindow.WouldReplay(packetIndex))
        {
            EmitReject(PacketProtectionStatus.ReplayRejected, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.ReplayRejected;
        }

        Span<byte> rocBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(rocBytes, guessedRoc);
        if (!VerifyAuthenticationTag(packet[..authenticatedLength], rocBytes, packet.Slice(authenticatedLength, AuthenticationTagBytes)))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        if (!TryGetRtpPayload(packet[..protectedLength], out Span<byte> payload))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: false);
            return PacketProtectionStatus.InvalidPacket;
        }

        Span<byte> counter = stackalloc byte[16];
        WriteRtpCounter(ssrc, guessedRoc, sequenceNumber, counter);
        ApplyCipher(counter, payload);

        replayWindow.Mark(packetIndex);
        UpdateInboundRolloverCounter(sequenceNumber, guessedRoc);
        outputLength = protectedLength;
        return PacketProtectionStatus.Success;
    }

    private static bool TryGetRtpPayload(Span<byte> packet, out Span<byte> payload)
    {
        payload = default;
        if (packet.Length < 12)
        {
            return false;
        }

        int csrcCount = packet[0] & 0x0F;
        int headerLength = 12 + csrcCount * 4;
        if (packet.Length < headerLength)
        {
            return false;
        }

        if ((packet[0] & 0x10) != 0)
        {
            if (packet.Length < headerLength + 4)
            {
                return false;
            }

            int extensionWords = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(headerLength + 2, 2));
            headerLength += 4 + extensionWords * 4;
            if (packet.Length < headerLength)
            {
                return false;
            }
        }

        payload = packet[headerLength..];
        return true;
    }

    private uint UpdateOutboundRolloverCounter(ushort sequenceNumber)
    {
        if (hasReceivedPacket && highestSequenceNumber > 0xF000 && sequenceNumber < 0x1000)
        {
            rolloverCounter++;
        }

        highestSequenceNumber = sequenceNumber;
        hasReceivedPacket = true;
        return rolloverCounter;
    }

    private uint GuessInboundRolloverCounter(ushort sequenceNumber)
    {
        if (!hasReceivedPacket)
        {
            return 0;
        }

        if (highestSequenceNumber < 0x8000)
        {
            return sequenceNumber - highestSequenceNumber > 0x8000 && rolloverCounter > 0
                ? rolloverCounter - 1
                : rolloverCounter;
        }

        return highestSequenceNumber - 0x8000 > sequenceNumber
            ? rolloverCounter + 1
            : rolloverCounter;
    }

    private void UpdateInboundRolloverCounter(ushort sequenceNumber, uint guessedRoc)
    {
        ulong currentHighestIndex = ((ulong)rolloverCounter << 16) | highestSequenceNumber;
        ulong guessedIndex = ((ulong)guessedRoc << 16) | sequenceNumber;

        if (!hasReceivedPacket || guessedIndex > currentHighestIndex)
        {
            rolloverCounter = guessedRoc;
            highestSequenceNumber = sequenceNumber;
            hasReceivedPacket = true;
        }
    }
}

internal sealed class AesCmSha1RtcpPacketProtector : AesCmSha1PacketProtector
{
    private readonly PacketDirection direction;
    private readonly byte[] mki;
    private readonly uint ssrc;
    private readonly ReplayWindow replayWindow;
    private uint outboundIndex;

    internal AesCmSha1RtcpPacketProtector(
        AesCmSha1SessionKeys keys,
        PacketDirection direction,
        uint ssrc,
        int authenticationTagBytes,
        int replayWindowSize,
        byte[] mki,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry)
        : base(keys, authenticationTagBytes, telemetry, hasTelemetry)
    {
        this.direction = direction;
        this.ssrc = ssrc;
        this.mki = mki;
        replayWindow = new ReplayWindow(replayWindowSize);
    }

    public override int MaximumExpansionBytes => 4 + mki.Length + AuthenticationTagBytes;

    public override PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Outbound || inputLength < 8 || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        if (inputLength > packet.Length - MaximumExpansionBytes)
        {
            EmitReject(PacketProtectionStatus.DestinationTooSmall, ssrc, isRtcp: true);
            return PacketProtectionStatus.DestinationTooSmall;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[4..8]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.WrongSsrc;
        }

        int authenticatedLength = inputLength + 4 + mki.Length;
        if (!CanAuthenticate(authenticatedLength, suffixLength: 0))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        uint index = outboundIndex++ & 0x7FFFFFFF;
        Span<byte> counter = stackalloc byte[16];
        WriteRtcpCounter(ssrc, index, counter);
        ApplyCipher(counter, packet[8..inputLength]);

        uint encryptedIndex = index | 0x80000000u;
        BinaryPrimitives.WriteUInt32BigEndian(packet.Slice(inputLength, 4), encryptedIndex);
        mki.CopyTo(packet.Slice(inputLength + 4, mki.Length));
        if (!TryWriteAuthenticationTag(packet[..authenticatedLength], ReadOnlySpan<byte>.Empty, packet.Slice(authenticatedLength, AuthenticationTagBytes)))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        outputLength = authenticatedLength + AuthenticationTagBytes;
        return PacketProtectionStatus.Success;
    }

    public override PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        if (direction != PacketDirection.Inbound || inputLength < 8 + MaximumExpansionBytes || inputLength > packet.Length ||
            !HasRtpFamilyVersion(packet[..inputLength]))
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        int authenticatedLength = inputLength - AuthenticationTagBytes;
        int plainLength = authenticatedLength - 4 - mki.Length;
        if (plainLength < 8)
        {
            EmitReject(PacketProtectionStatus.InvalidPacket, ssrc, isRtcp: true);
            return PacketProtectionStatus.InvalidPacket;
        }

        uint packetSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet[4..8]);
        if (packetSsrc != ssrc)
        {
            EmitReject(PacketProtectionStatus.WrongSsrc, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.WrongSsrc;
        }

        if (mki.Length > 0 && !CryptographicOperations.FixedTimeEquals(packet.Slice(plainLength + 4, mki.Length), mki))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        uint encryptedIndex = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(plainLength, 4));
        bool encrypted = (encryptedIndex & 0x80000000u) != 0;
        uint index = encryptedIndex & 0x7FFFFFFFu;
        if (replayWindow.WouldReplay(index))
        {
            EmitReject(PacketProtectionStatus.ReplayRejected, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.ReplayRejected;
        }

        if (!VerifyAuthenticationTag(packet[..authenticatedLength], ReadOnlySpan<byte>.Empty, packet.Slice(authenticatedLength, AuthenticationTagBytes)))
        {
            EmitReject(PacketProtectionStatus.AuthenticationFailed, packetSsrc, isRtcp: true);
            return PacketProtectionStatus.AuthenticationFailed;
        }

        if (encrypted)
        {
            Span<byte> counter = stackalloc byte[16];
            WriteRtcpCounter(ssrc, index, counter);
            ApplyCipher(counter, packet[8..plainLength]);
        }

        replayWindow.Mark(index);
        outputLength = plainLength;
        return PacketProtectionStatus.Success;
    }
}

internal sealed class NoAllocHmacSha1
{
    private const int BlockBytes = 64;
    private const int HashBytes = 20;
    private readonly byte[] innerInput;
    private readonly byte[] outerInput;

    internal NoAllocHmacSha1(ReadOnlySpan<byte> key, int maximumInputBytes)
    {
        innerInput = new byte[BlockBytes + maximumInputBytes];
        outerInput = new byte[BlockBytes + HashBytes];

        byte[]? normalizedKeyArray = null;
        ReadOnlySpan<byte> keyBlock = key;
        if (key.Length > BlockBytes)
        {
            normalizedKeyArray = new byte[HashBytes];
            Sha1NoAlloc.Hash(key, normalizedKeyArray);
            keyBlock = normalizedKeyArray;
        }

        for (int i = 0; i < BlockBytes; i++)
        {
            byte keyByte = i < keyBlock.Length ? keyBlock[i] : (byte)0;
            innerInput[i] = (byte)(keyByte ^ 0x36);
            outerInput[i] = (byte)(keyByte ^ 0x5C);
        }
    }

    internal bool TryComputeHash(ReadOnlySpan<byte> input, Span<byte> destination)
    {
        if (destination.Length < HashBytes || input.Length > innerInput.Length - BlockBytes)
        {
            return false;
        }

        input.CopyTo(innerInput.AsSpan(BlockBytes));
        Span<byte> innerHash = stackalloc byte[HashBytes];
        Sha1NoAlloc.Hash(innerInput.AsSpan(0, BlockBytes + input.Length), innerHash);
        innerHash.CopyTo(outerInput.AsSpan(BlockBytes));
        Sha1NoAlloc.Hash(outerInput, destination);
        return true;
    }
}

internal static class Sha1NoAlloc
{
    private const int BlockBytes = 64;

    internal static void Hash(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        uint h0 = 0x67452301;
        uint h1 = 0xEFCDAB89;
        uint h2 = 0x98BADCFE;
        uint h3 = 0x10325476;
        uint h4 = 0xC3D2E1F0;

        int offset = 0;
        while (data.Length - offset >= BlockBytes)
        {
            ProcessBlock(data.Slice(offset, BlockBytes), ref h0, ref h1, ref h2, ref h3, ref h4);
            offset += BlockBytes;
        }

        Span<byte> finalBlocks = stackalloc byte[BlockBytes * 2];
        int remaining = data.Length - offset;
        data[offset..].CopyTo(finalBlocks);
        finalBlocks[remaining] = 0x80;
        ulong bitLength = (ulong)data.Length * 8;
        int finalLength = remaining + 1 + 8 <= BlockBytes ? BlockBytes : BlockBytes * 2;
        BinaryPrimitives.WriteUInt64BigEndian(finalBlocks.Slice(finalLength - 8, 8), bitLength);

        ProcessBlock(finalBlocks[..BlockBytes], ref h0, ref h1, ref h2, ref h3, ref h4);
        if (finalLength == BlockBytes * 2)
        {
            ProcessBlock(finalBlocks.Slice(BlockBytes, BlockBytes), ref h0, ref h1, ref h2, ref h3, ref h4);
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination[..4], h0);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), h1);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), h2);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(12, 4), h3);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16, 4), h4);
    }

    private static void ProcessBlock(
        ReadOnlySpan<byte> block,
        ref uint h0,
        ref uint h1,
        ref uint h2,
        ref uint h3,
        ref uint h4)
    {
        Span<uint> w = stackalloc uint[80];
        for (int i = 0; i < 16; i++)
        {
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(i * 4, 4));
        }

        for (int i = 16; i < 80; i++)
        {
            w[i] = BitOperations.RotateLeft(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
        }

        uint a = h0;
        uint b = h1;
        uint c = h2;
        uint d = h3;
        uint e = h4;

        for (int i = 0; i < 80; i++)
        {
            uint f;
            uint k;
            if (i < 20)
            {
                f = (b & c) | (~b & d);
                k = 0x5A827999;
            }
            else if (i < 40)
            {
                f = b ^ c ^ d;
                k = 0x6ED9EBA1;
            }
            else if (i < 60)
            {
                f = (b & c) | (b & d) | (c & d);
                k = 0x8F1BBCDC;
            }
            else
            {
                f = b ^ c ^ d;
                k = 0xCA62C1D6;
            }

            uint temp = BitOperations.RotateLeft(a, 5) + f + e + k + w[i];
            e = d;
            d = c;
            c = BitOperations.RotateLeft(b, 30);
            b = a;
            a = temp;
        }

        h0 += a;
        h1 += b;
        h2 += c;
        h3 += d;
        h4 += e;
    }
}

internal sealed class ReplayWindow
{
    private readonly int windowSize;
    private ulong highestIndex;
    private ulong bitmask;
    private bool hasPacket;

    internal ReplayWindow(int windowSize)
    {
        this.windowSize = Math.Clamp(windowSize, 1, 64);
    }

    internal bool WouldReplay(ulong packetIndex)
    {
        if (!hasPacket || packetIndex > highestIndex)
        {
            return false;
        }

        ulong delta = highestIndex - packetIndex;
        if (delta >= (ulong)windowSize)
        {
            return true;
        }

        return (bitmask & (1UL << (int)delta)) != 0;
    }

    internal void Mark(ulong packetIndex)
    {
        if (!hasPacket)
        {
            highestIndex = packetIndex;
            bitmask = 1;
            hasPacket = true;
            return;
        }

        if (packetIndex > highestIndex)
        {
            ulong shift = packetIndex - highestIndex;
            bitmask = shift >= 64 ? 1 : (bitmask << (int)shift) | 1;
            highestIndex = packetIndex;
            return;
        }

        ulong delta = highestIndex - packetIndex;
        if (delta < 64)
        {
            bitmask |= 1UL << (int)delta;
        }
    }
}

internal sealed class UnsupportedPacketProtectorFactory : IPacketProtectorFactory
{
    public IPacketProtector Create(PacketProtectionPurpose purpose, PacketDirection direction, uint ssrc)
    {
        return new UnsupportedPacketProtector();
    }
}

internal sealed class UnsupportedPacketProtector : IPacketProtector
{
    public int MaximumExpansionBytes => 0;

    public PacketProtectionStatus Protect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        return PacketProtectionStatus.UnsupportedProfile;
    }

    public PacketProtectionStatus Unprotect(Span<byte> packet, int inputLength, out int outputLength)
    {
        outputLength = 0;
        return PacketProtectionStatus.UnsupportedProfile;
    }
}
