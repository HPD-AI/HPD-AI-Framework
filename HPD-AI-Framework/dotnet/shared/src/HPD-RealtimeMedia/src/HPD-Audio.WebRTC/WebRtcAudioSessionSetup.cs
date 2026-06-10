#nullable enable

using HPD.Media.Rtp.Audio;
using HPD.Media.Rtp.Audio.Sdp;
using HPD.Media.Sdp;
using HPD.Media.Transport;
using HPD.Media.WebRTC;

namespace HPD.Audio.WebRTC;

/// <summary>
/// Configures the control-plane setup for one WebRTC audio media section.
/// </summary>
public sealed class WebRtcAudioSessionSetupOptions
{
    /// <summary>Gets the remote WebRTC session description.</summary>
    public required WebRtcSessionDescription RemoteDescription { get; init; }

    /// <summary>Gets the AOT-safe SDP parser.</summary>
    public required ISdpParser SdpParser { get; init; }

    /// <summary>Gets the ICE datagram path factory.</summary>
    public required IIceDatagramPathFactory Ice { get; init; }

    /// <summary>Gets the DTLS-SRTP handshake provider.</summary>
    public required ISecureHandshake SecureHandshake { get; init; }

    /// <summary>Gets the local certificate used by the secure handshake.</summary>
    public required LocalCertificate LocalCertificate { get; init; }

    /// <summary>Gets secure-handshake options.</summary>
    public required SecureHandshakeOptions HandshakeOptions { get; init; }

    /// <summary>Gets the peer identity verifier.</summary>
    public required IPeerIdentityVerifier PeerIdentityVerifier { get; init; }

    /// <summary>Gets the SRTP key schedule.</summary>
    public required ISrtpKeySchedule KeySchedule { get; init; }

    /// <summary>Gets the packet-protector factory provider.</summary>
    public required IWebRtcAudioPacketProtectorFactoryProvider PacketProtectorFactoryProvider { get; init; }

    /// <summary>Gets the preferred audio media identifier, or null to select the first audio section.</summary>
    public string? MediaId { get; init; }

    /// <summary>Gets the RTP payload-map version for this negotiated media generation.</summary>
    public ulong RtpAudioFormatMapVersion { get; init; } = 1;
}

/// <summary>
/// Classifies WebRTC audio session setup results without relying on exception text.
/// </summary>
public enum WebRtcAudioSessionSetupStatus
{
    /// <summary>The session setup completed.</summary>
    Success = 0,

    /// <summary>One or more required options were missing.</summary>
    InvalidOptions = 1,

    /// <summary>The remote SDP could not be parsed as WebRTC audio.</summary>
    InvalidRemoteDescription = 2,

    /// <summary>No matching audio media section was present.</summary>
    AudioMediaNotFound = 3,

    /// <summary>The SDP audio media section could not produce an RTP audio payload map.</summary>
    InvalidRtpAudioFormatMap = 4,

    /// <summary>ICE setup or selected-path creation failed.</summary>
    IceFailed = 5,

    /// <summary>DTLS-SRTP and packet-protection setup failed.</summary>
    SecurityFailed = 6
}

/// <summary>
/// Represents a configured WebRTC audio media session after SDP, ICE, and DTLS-SRTP setup.
/// </summary>
public sealed class WebRtcAudioSession
{
    internal WebRtcAudioSession(
        WebRtcMediaDescription media,
        RtpAudioFormatMap formatMap,
        WebRtcAudioSecurityContext security)
    {
        Media = media;
        FormatMap = formatMap;
        Security = security;
    }

    /// <summary>Gets the selected WebRTC media description.</summary>
    public WebRtcMediaDescription Media { get; }

    /// <summary>Gets the RTP audio payload map for the selected media generation.</summary>
    public RtpAudioFormatMap FormatMap { get; }

    /// <summary>Gets the secure audio media context.</summary>
    public WebRtcAudioSecurityContext Security { get; }
}

/// <summary>
/// Parses WebRTC SDP, configures ICE, creates the selected datagram path, and creates secure audio media state.
/// </summary>
public static class WebRtcAudioSessionSetup
{
    /// <summary>
    /// Attempts to set up one secure WebRTC audio media session.
    /// </summary>
    public static async ValueTask<WebRtcAudioSession?> TryCreateAsync(
        WebRtcAudioSessionSetupOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateOptions(options))
        {
            return null;
        }

        if (!WebRtcSdpNegotiation.TryParse(
                options.RemoteDescription,
                options.SdpParser,
                out WebRtcParsedSessionDescription parsed,
                out SdpStatus sdpStatus) ||
            sdpStatus != SdpStatus.Success)
        {
            return null;
        }

        if (!TrySelectAudioMedia(parsed.MediaDescriptions.Span, options.MediaId, out WebRtcMediaDescription media))
        {
            return null;
        }

        if (!SdpRtpAudioFormatMapBuilder.TryBuild(
                media.SdpMedia,
                options.RtpAudioFormatMapVersion,
                out RtpAudioFormatMap formatMap))
        {
            return null;
        }

        if (media.IceCredentials is not { } remoteCredentials)
        {
            return null;
        }

        IDatagramPath path;
        try
        {
            await options.Ice.SetRemoteCredentialsAsync(remoteCredentials, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < media.IceCandidates.Length; i++)
            {
                IceCandidate candidate = media.IceCandidates.Span[i];
                await options.Ice.AddRemoteCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            }

            await options.Ice.EndRemoteCandidatesAsync(cancellationToken).ConfigureAwait(false);
            path = await options.Ice.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        WebRtcAudioSecurityContext? security = await WebRtcAudioSecurityContext.CreateAsync(new WebRtcAudioSecurityOptions
        {
            Path = path,
            SecureHandshake = options.SecureHandshake,
            LocalCertificate = options.LocalCertificate,
            HandshakeOptions = options.HandshakeOptions,
            KeySchedule = options.KeySchedule,
            PacketProtectorFactoryProvider = options.PacketProtectorFactoryProvider,
            PeerIdentityVerifier = options.PeerIdentityVerifier,
            ExpectedPeerIdentity = media.ExpectedPeerIdentity
        }, cancellationToken).ConfigureAwait(false);
        if (security is null)
        {
            await path.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        return new WebRtcAudioSession(media, formatMap, security);
    }

    private static bool TrySelectAudioMedia(
        ReadOnlySpan<WebRtcMediaDescription> mediaDescriptions,
        string? mediaId,
        out WebRtcMediaDescription media)
    {
        foreach (WebRtcMediaDescription candidate in mediaDescriptions)
        {
            if (candidate.SdpMedia.Kind != SdpMediaKind.Audio)
            {
                continue;
            }

            if (mediaId is not null &&
                !string.Equals(candidate.Mid, mediaId, StringComparison.Ordinal))
            {
                continue;
            }

            media = candidate;
            return true;
        }

        media = default;
        return false;
    }

    private static bool ValidateOptions(WebRtcAudioSessionSetupOptions? options)
    {
        return options is not null &&
            options.SdpParser is not null &&
            options.Ice is not null &&
            options.SecureHandshake is not null &&
            options.LocalCertificate is not null &&
            options.LocalCertificate.Certificate is not null &&
            options.HandshakeOptions is not null &&
            options.PeerIdentityVerifier is not null &&
            options.KeySchedule is not null &&
            options.PacketProtectorFactoryProvider is not null &&
            options.RtpAudioFormatMapVersion > 0 &&
            (options.MediaId is null || options.MediaId.Length > 0);
    }
}
