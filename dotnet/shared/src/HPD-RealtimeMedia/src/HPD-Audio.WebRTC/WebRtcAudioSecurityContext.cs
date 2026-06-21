#nullable enable

using HPD.Audio.Codecs;
using HPD.Audio.Primitives;
using HPD.Media.Rtp.Audio;
using HPD.Media.Transport;

namespace HPD.Audio.WebRTC;

/// <summary>
/// Creates packet-protector factories from role-resolved SRTP material for WebRTC audio.
/// </summary>
public interface IWebRtcAudioPacketProtectorFactoryProvider
{
    /// <summary>Creates a packet-protector factory for one negotiated SRTP material set.</summary>
    IPacketProtectorFactory Create(SrtpProtectionMaterial material);
}

/// <summary>
/// Configures the control-plane setup for a secure WebRTC audio media context.
/// </summary>
public sealed class WebRtcAudioSecurityOptions
{
    /// <summary>Gets the selected datagram path used by the secure handshake.</summary>
    public required IDatagramPath Path { get; init; }

    /// <summary>Gets the DTLS-SRTP handshake provider.</summary>
    public required ISecureHandshake SecureHandshake { get; init; }

    /// <summary>Gets the local certificate used by the secure handshake.</summary>
    public required LocalCertificate LocalCertificate { get; init; }

    /// <summary>Gets secure-handshake options.</summary>
    public required SecureHandshakeOptions HandshakeOptions { get; init; }

    /// <summary>Gets the SRTP key schedule.</summary>
    public required ISrtpKeySchedule KeySchedule { get; init; }

    /// <summary>Gets the packet-protector factory provider.</summary>
    public required IWebRtcAudioPacketProtectorFactoryProvider PacketProtectorFactoryProvider { get; init; }

    /// <summary>Gets an optional peer identity verifier.</summary>
    public IPeerIdentityVerifier? PeerIdentityVerifier { get; init; }

    /// <summary>Gets optional expected peer identity material from signaling.</summary>
    public ExpectedPeerIdentity? ExpectedPeerIdentity { get; init; }
}

/// <summary>
/// Classifies secure WebRTC audio setup results without relying on exception text.
/// </summary>
public enum WebRtcAudioSecurityStatus
{
    /// <summary>The secure context was created.</summary>
    Success = 0,

    /// <summary>One or more required options were missing.</summary>
    InvalidOptions = 1,

    /// <summary>The secure handshake failed.</summary>
    HandshakeFailed = 2,

    /// <summary>The peer proof did not match the expected identity.</summary>
    PeerIdentityFailed = 3,

    /// <summary>SRTP material or packet-protector creation failed.</summary>
    PacketProtectionFailed = 4
}

/// <summary>
/// Represents a secure WebRTC audio media context after DTLS-SRTP material has been derived.
/// </summary>
public sealed class WebRtcAudioSecurityContext
{
    private readonly IPacketProtectorFactory packetProtectorFactory;

    private WebRtcAudioSecurityContext(
        IDatagramPath path,
        SecureHandshakeResult handshake,
        SrtpProtectionMaterial protectionMaterial,
        IPacketProtectorFactory packetProtectorFactory)
    {
        Path = path;
        Handshake = handshake;
        ProtectionMaterial = protectionMaterial;
        this.packetProtectorFactory = packetProtectorFactory;
    }

    /// <summary>Gets the selected datagram path used by this media context.</summary>
    public IDatagramPath Path { get; }

    /// <summary>Gets the completed secure-handshake result.</summary>
    public SecureHandshakeResult Handshake { get; }

    /// <summary>Gets the role-resolved SRTP protection material.</summary>
    public SrtpProtectionMaterial ProtectionMaterial { get; }

    /// <summary>
    /// Runs the secure handshake, verifies peer identity when requested, derives SRTP material, and creates packet protectors.
    /// </summary>
    public static async ValueTask<WebRtcAudioSecurityContext?> CreateAsync(
        WebRtcAudioSecurityOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateOptions(options))
        {
            return null;
        }

        SecureHandshakeResult handshake;
        try
        {
            handshake = await options.SecureHandshake
                .HandshakeAsync(options.Path, options.LocalCertificate, options.HandshakeOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (options.ExpectedPeerIdentity is { } expectedIdentity)
        {
            if (options.PeerIdentityVerifier is null)
            {
                return null;
            }

            PeerIdentityVerificationResult verification = options.PeerIdentityVerifier.Verify(handshake.PeerProof, expectedIdentity);
            if (!verification.IsVerified)
            {
                return null;
            }
        }

        SrtpProtectionMaterial material;
        IPacketProtectorFactory packetProtectorFactory;
        try
        {
            material = options.KeySchedule.Derive(handshake);
            packetProtectorFactory = options.PacketProtectorFactoryProvider.Create(material);
        }
        catch
        {
            return null;
        }

        return new WebRtcAudioSecurityContext(options.Path, handshake, material, packetProtectorFactory);
    }

    /// <summary>Creates an inbound RTP audio pump for one remote RTP source.</summary>
    public WebRtcAudioInboundPump CreateInboundAudioPump(
        uint remoteSsrc,
        IRtpAudioFormatMap formatMap,
        IRealtimeAudioDecoder decoder,
        IAudioFrameViewSink sink)
    {
        IPacketProtector protector = packetProtectorFactory.Create(
            PacketProtectionPurpose.Rtp,
            PacketDirection.Inbound,
            remoteSsrc);
        return new WebRtcAudioInboundPump(protector, formatMap, decoder, sink);
    }

    /// <summary>Creates an outbound RTP audio pump for one local RTP source.</summary>
    public WebRtcAudioOutboundPump CreateOutboundAudioPump(
        uint localSsrc,
        byte payloadType,
        IRtpAudioFormatMap formatMap,
        IRealtimeAudioEncoder encoder,
        IWebRtcProtectedPacketSink sink,
        ushort initialSequenceNumber = 0,
        uint initialTimestamp = 0)
    {
        IPacketProtector protector = packetProtectorFactory.Create(
            PacketProtectionPurpose.Rtp,
            PacketDirection.Outbound,
            localSsrc);
        return new WebRtcAudioOutboundPump(
            encoder,
            formatMap,
            protector,
            sink,
            localSsrc,
            payloadType,
            initialSequenceNumber,
            initialTimestamp);
    }

    private static bool ValidateOptions(WebRtcAudioSecurityOptions? options)
    {
        return options is not null &&
            options.Path is not null &&
            options.SecureHandshake is not null &&
            options.LocalCertificate is not null &&
            options.LocalCertificate.Certificate is not null &&
            options.HandshakeOptions is not null &&
            options.KeySchedule is not null &&
            options.PacketProtectorFactoryProvider is not null;
    }
}
