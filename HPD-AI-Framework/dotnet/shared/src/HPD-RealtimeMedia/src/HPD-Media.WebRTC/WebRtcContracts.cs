#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Media.Sdp;
using HPD.Media.Transport;

namespace HPD.Media.WebRTC;

/// <summary>
/// Identifies a WebRTC session description type.
/// </summary>
public enum WebRtcSessionDescriptionType
{
    Offer = 0,
    Answer = 1,
    Rollback = 2
}

/// <summary>
/// Represents a WebRTC session description.
/// </summary>
public readonly struct WebRtcSessionDescription
{
    /// <summary>Gets the description type.</summary>
    public required WebRtcSessionDescriptionType Type { get; init; }

    /// <summary>Gets the SDP text.</summary>
    public required string Sdp { get; init; }
}

/// <summary>
/// Represents a WebRTC ICE candidate message.
/// </summary>
public readonly struct WebRtcIceCandidate
{
    /// <summary>Gets the candidate attribute value.</summary>
    public required string Candidate { get; init; }

    /// <summary>Gets the SDP media identifier when present.</summary>
    public string? SdpMid { get; init; }

    /// <summary>Gets the SDP m-line index when present.</summary>
    public int? SdpMLineIndex { get; init; }
}

/// <summary>
/// Identifies a WebRTC signaling event kind without requiring subclass allocation.
/// </summary>
public enum WebRtcSignalEventKind
{
    RemoteDescriptionReceived = 0,
    RemoteIceCandidateReceived = 1,
    RemoteEndOfCandidatesReceived = 2,
    SignalingDisconnected = 3,
    SignalingProtocolError = 4
}

/// <summary>
/// Represents a WebRTC signaling event.
/// </summary>
public readonly struct WebRtcSignalEvent
{
    /// <summary>Gets the event kind.</summary>
    public required WebRtcSignalEventKind Kind { get; init; }

    /// <summary>Gets the remote description for description events.</summary>
    public WebRtcSessionDescription Description { get; init; }

    /// <summary>Gets the remote candidate for candidate events.</summary>
    public WebRtcIceCandidate Candidate { get; init; }

    /// <summary>Gets the negotiation identifier when present.</summary>
    public string? NegotiationId { get; init; }

    /// <summary>Gets a reason or protocol error message when present.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Exchanges WebRTC session descriptions and ICE candidates.
/// </summary>
public interface IWebRtcSignalingChannel
{
    /// <summary>Reads one signaling event, or null when signaling completes.</summary>
    ValueTask<WebRtcSignalEvent?> ReadEventAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a local session description.</summary>
    ValueTask SendSessionDescriptionAsync(
        WebRtcSessionDescription description,
        string negotiationId,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a local ICE candidate.</summary>
    ValueTask SendIceCandidateAsync(WebRtcIceCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>Sends an end-of-candidates signal.</summary>
    ValueTask SendEndOfCandidatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and writes browser-facing WebRTC signaling payloads without reflection-based serializers.
/// </summary>
public static class WebRtcSignalingJson
{
    /// <summary>
    /// Writes a WebRTC session description JSON object.
    /// </summary>
    public static void WriteSessionDescription(in WebRtcSessionDescription description, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateSessionDescription(description);
        using var writer = new Utf8JsonWriter(destination);
        WriteSessionDescriptionObject(writer, description);
    }

    private static void WriteSessionDescriptionObject(Utf8JsonWriter writer, in WebRtcSessionDescription description)
    {
        writer.WriteStartObject();
        writer.WriteString("type", FormatDescriptionType(description.Type));
        writer.WriteString("sdp", description.Sdp);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Attempts to parse a WebRTC session description JSON object.
    /// </summary>
    public static bool TryParseSessionDescription(ReadOnlySpan<byte> utf8Json, out WebRtcSessionDescription description)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            if (!reader.Read())
            {
                description = default;
                return false;
            }

            return TryReadSessionDescriptionObject(ref reader, out description) &&
                HasNoTrailingTokens(ref reader);
        }
        catch (JsonException)
        {
            description = default;
            return false;
        }
    }

    private static bool TryReadSessionDescriptionObject(
        ref Utf8JsonReader reader,
        out WebRtcSessionDescription description)
    {
        description = default;
        string? type = null;
        string? sdp = null;

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                return false;
            }

            string? propertyName = reader.GetString();
            if (!reader.Read())
            {
                return false;
            }

            switch (propertyName)
            {
                case "type" when reader.TokenType == JsonTokenType.String:
                    type = reader.GetString();
                    break;
                case "sdp" when reader.TokenType == JsonTokenType.String:
                    sdp = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (type is null ||
            string.IsNullOrWhiteSpace(sdp) ||
            !TryParseDescriptionType(type, out WebRtcSessionDescriptionType descriptionType))
        {
            return false;
        }

        description = new WebRtcSessionDescription
        {
            Type = descriptionType,
            Sdp = sdp
        };
        return true;
    }

    /// <summary>
    /// Writes a WebRTC ICE candidate JSON object.
    /// </summary>
    public static void WriteIceCandidate(in WebRtcIceCandidate candidate, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateIceCandidate(candidate);
        using var writer = new Utf8JsonWriter(destination);
        WriteIceCandidateObject(writer, candidate);
    }

    private static void WriteIceCandidateObject(Utf8JsonWriter writer, in WebRtcIceCandidate candidate)
    {
        writer.WriteStartObject();
        writer.WriteString("candidate", candidate.Candidate);
        if (candidate.SdpMid is not null)
        {
            writer.WriteString("sdpMid", candidate.SdpMid);
        }

        if (candidate.SdpMLineIndex is not null)
        {
            writer.WriteNumber("sdpMLineIndex", candidate.SdpMLineIndex.Value);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Attempts to parse a WebRTC ICE candidate JSON object.
    /// </summary>
    public static bool TryParseIceCandidate(ReadOnlySpan<byte> utf8Json, out WebRtcIceCandidate candidate)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            if (!reader.Read())
            {
                candidate = default;
                return false;
            }

            return TryReadIceCandidateObject(ref reader, out candidate) &&
                HasNoTrailingTokens(ref reader);
        }
        catch (JsonException)
        {
            candidate = default;
            return false;
        }
    }

    private static bool TryReadIceCandidateObject(ref Utf8JsonReader reader, out WebRtcIceCandidate candidate)
    {
        candidate = default;
        string? candidateText = null;
        string? sdpMid = null;
        int? sdpMLineIndex = null;

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                return false;
            }

            string? propertyName = reader.GetString();
            if (!reader.Read())
            {
                return false;
            }

            switch (propertyName)
            {
                case "candidate" when reader.TokenType == JsonTokenType.String:
                    candidateText = reader.GetString();
                    break;
                case "sdpMid" when reader.TokenType == JsonTokenType.String:
                    sdpMid = reader.GetString();
                    break;
                case "sdpMid" when reader.TokenType == JsonTokenType.Null:
                    sdpMid = null;
                    break;
                case "sdpMLineIndex" when reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value):
                    sdpMLineIndex = value;
                    break;
                case "sdpMLineIndex" when reader.TokenType == JsonTokenType.Null:
                    sdpMLineIndex = null;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (candidateText is null ||
            string.IsNullOrWhiteSpace(candidateText) ||
            (sdpMid is not null && string.IsNullOrWhiteSpace(sdpMid)) ||
            sdpMLineIndex is < 0)
        {
            return false;
        }

        candidate = new WebRtcIceCandidate
        {
            Candidate = candidateText,
            SdpMid = sdpMid,
            SdpMLineIndex = sdpMLineIndex
        };
        return true;
    }

    /// <summary>
    /// Writes one WebRTC signaling event envelope.
    /// </summary>
    public static void WriteSignalEvent(in WebRtcSignalEvent signalEvent, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateSignalEvent(signalEvent);
        using var writer = new Utf8JsonWriter(destination);
        writer.WriteStartObject();
        writer.WriteString("kind", FormatSignalEventKind(signalEvent.Kind));
        if (signalEvent.NegotiationId is not null)
        {
            writer.WriteString("negotiationId", signalEvent.NegotiationId);
        }

        if (signalEvent.Message is not null)
        {
            writer.WriteString("message", signalEvent.Message);
        }

        switch (signalEvent.Kind)
        {
            case WebRtcSignalEventKind.RemoteDescriptionReceived:
                writer.WritePropertyName("description");
                WriteSessionDescriptionObject(writer, signalEvent.Description);
                break;
            case WebRtcSignalEventKind.RemoteIceCandidateReceived:
                writer.WritePropertyName("candidate");
                WriteIceCandidateObject(writer, signalEvent.Candidate);
                break;
        }

        writer.WriteEndObject();
    }

    private static void ValidateSignalEvent(in WebRtcSignalEvent signalEvent)
    {
        _ = FormatSignalEventKind(signalEvent.Kind);
        switch (signalEvent.Kind)
        {
            case WebRtcSignalEventKind.RemoteDescriptionReceived:
                ValidateSessionDescription(signalEvent.Description);
                break;
            case WebRtcSignalEventKind.RemoteIceCandidateReceived:
                ValidateIceCandidate(signalEvent.Candidate);
                break;
        }
    }

    private static void ValidateSessionDescription(in WebRtcSessionDescription description)
    {
        _ = FormatDescriptionType(description.Type);
        if (string.IsNullOrWhiteSpace(description.Sdp))
        {
            throw new ArgumentException("The WebRTC session description SDP must not be empty.", nameof(description));
        }
    }

    private static void ValidateIceCandidate(in WebRtcIceCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Candidate))
        {
            throw new ArgumentException("The WebRTC ICE candidate value must not be empty.", nameof(candidate));
        }

        if (candidate.SdpMid is not null && string.IsNullOrWhiteSpace(candidate.SdpMid))
        {
            throw new ArgumentException("The WebRTC ICE candidate SDP mid must not be empty when provided.", nameof(candidate));
        }

        if (candidate.SdpMLineIndex is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidate), candidate.SdpMLineIndex, "The WebRTC ICE candidate SDP m-line index must not be negative.");
        }
    }

    /// <summary>
    /// Attempts to parse one WebRTC signaling event envelope.
    /// </summary>
    public static bool TryParseSignalEvent(ReadOnlySpan<byte> utf8Json, out WebRtcSignalEvent signalEvent)
    {
        try
        {
            signalEvent = default;
            var reader = new Utf8JsonReader(utf8Json);
            string? kind = null;
            string? negotiationId = null;
            string? message = null;
            WebRtcSessionDescription description = default;
            WebRtcIceCandidate candidate = default;
            bool hasDescription = false;
            bool hasCandidate = false;

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    return false;
                }

                string? propertyName = reader.GetString();
                if (!reader.Read())
                {
                    return false;
                }

                switch (propertyName)
                {
                    case "kind" when reader.TokenType == JsonTokenType.String:
                        kind = reader.GetString();
                        break;
                    case "negotiationId" when reader.TokenType == JsonTokenType.String:
                        negotiationId = reader.GetString();
                        break;
                    case "negotiationId" when reader.TokenType == JsonTokenType.Null:
                        negotiationId = null;
                        break;
                    case "message" when reader.TokenType == JsonTokenType.String:
                        message = reader.GetString();
                        break;
                    case "message" when reader.TokenType == JsonTokenType.Null:
                        message = null;
                        break;
                    case "description":
                        if (!TryReadSessionDescriptionObject(ref reader, out description))
                        {
                            return false;
                        }

                        hasDescription = true;
                        break;
                    case "candidate":
                        if (!TryReadIceCandidateObject(ref reader, out candidate))
                        {
                            return false;
                        }

                        hasCandidate = true;
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (kind is null || !TryParseSignalEventKind(kind, out WebRtcSignalEventKind eventKind))
            {
                return false;
            }

            if (eventKind == WebRtcSignalEventKind.RemoteDescriptionReceived && !hasDescription)
            {
                return false;
            }

            if (eventKind == WebRtcSignalEventKind.RemoteIceCandidateReceived && !hasCandidate)
            {
                return false;
            }

            signalEvent = new WebRtcSignalEvent
            {
                Kind = eventKind,
                Description = description,
                Candidate = candidate,
                NegotiationId = negotiationId,
                Message = message
            };
            return HasNoTrailingTokens(ref reader);
        }
        catch (JsonException)
        {
            signalEvent = default;
            return false;
        }
    }

    private static bool HasNoTrailingTokens(ref Utf8JsonReader reader)
    {
        return !reader.Read();
    }

    private static string FormatDescriptionType(WebRtcSessionDescriptionType type)
    {
        return type switch
        {
            WebRtcSessionDescriptionType.Offer => "offer",
            WebRtcSessionDescriptionType.Answer => "answer",
            WebRtcSessionDescriptionType.Rollback => "rollback",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported WebRTC session description type.")
        };
    }

    private static bool TryParseDescriptionType(string value, out WebRtcSessionDescriptionType type)
    {
        type = value.ToLowerInvariant() switch
        {
            "offer" => WebRtcSessionDescriptionType.Offer,
            "answer" => WebRtcSessionDescriptionType.Answer,
            "rollback" => WebRtcSessionDescriptionType.Rollback,
            _ => (WebRtcSessionDescriptionType)(-1)
        };
        return type != (WebRtcSessionDescriptionType)(-1);
    }

    private static string FormatSignalEventKind(WebRtcSignalEventKind kind)
    {
        return kind switch
        {
            WebRtcSignalEventKind.RemoteDescriptionReceived => "remoteDescriptionReceived",
            WebRtcSignalEventKind.RemoteIceCandidateReceived => "remoteIceCandidateReceived",
            WebRtcSignalEventKind.RemoteEndOfCandidatesReceived => "remoteEndOfCandidatesReceived",
            WebRtcSignalEventKind.SignalingDisconnected => "signalingDisconnected",
            WebRtcSignalEventKind.SignalingProtocolError => "signalingProtocolError",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported WebRTC signal event kind.")
        };
    }

    private static bool TryParseSignalEventKind(string value, out WebRtcSignalEventKind kind)
    {
        kind = value switch
        {
            "remoteDescriptionReceived" => WebRtcSignalEventKind.RemoteDescriptionReceived,
            "remoteIceCandidateReceived" => WebRtcSignalEventKind.RemoteIceCandidateReceived,
            "remoteEndOfCandidatesReceived" => WebRtcSignalEventKind.RemoteEndOfCandidatesReceived,
            "signalingDisconnected" => WebRtcSignalEventKind.SignalingDisconnected,
            "signalingProtocolError" => WebRtcSignalEventKind.SignalingProtocolError,
            _ => (WebRtcSignalEventKind)(-1)
        };
        return kind != (WebRtcSignalEventKind)(-1);
    }
}

/// <summary>
/// Identifies the remote DTLS setup role signaled by SDP.
/// </summary>
public enum WebRtcDtlsSetup
{
    Unknown = 0,
    Active = 1,
    Passive = 2,
    ActPass = 3,
    HoldConn = 4
}

/// <summary>
/// Represents one WebRTC media section after SDP has been parsed into typed control-plane values.
/// </summary>
public readonly struct WebRtcMediaDescription
{
    /// <summary>Gets the SDP media section.</summary>
    public required SdpMediaSection SdpMedia { get; init; }

    /// <summary>Gets the media identifier.</summary>
    public string? Mid { get; init; }

    /// <summary>Gets the resolved ICE credentials for this media section.</summary>
    public IceCredentials? IceCredentials { get; init; }

    /// <summary>Gets the expected peer identity from the resolved SDP fingerprint.</summary>
    public ExpectedPeerIdentity? ExpectedPeerIdentity { get; init; }

    /// <summary>Gets the remote DTLS setup role.</summary>
    public WebRtcDtlsSetup Setup { get; init; }

    /// <summary>Gets parsed remote ICE candidates for this media section.</summary>
    public ReadOnlyMemory<IceCandidate> IceCandidates { get; init; }
}

/// <summary>
/// Represents a WebRTC session description parsed into typed negotiation data.
/// </summary>
public readonly struct WebRtcParsedSessionDescription
{
    /// <summary>Gets the original browser-facing description.</summary>
    public required WebRtcSessionDescription Description { get; init; }

    /// <summary>Gets the parsed SDP session.</summary>
    public required SdpSessionDescription Sdp { get; init; }

    /// <summary>Gets the parsed WebRTC media descriptions.</summary>
    public required ReadOnlyMemory<WebRtcMediaDescription> MediaDescriptions { get; init; }
}

/// <summary>
/// Parses browser-facing WebRTC SDP into typed negotiation values for WebRTC internals.
/// </summary>
public static class WebRtcSdpNegotiation
{
    /// <summary>
    /// Attempts to parse a WebRTC session description and derive typed media negotiation values.
    /// </summary>
    public static bool TryParse(
        in WebRtcSessionDescription description,
        ISdpParser parser,
        out WebRtcParsedSessionDescription parsedDescription,
        out SdpStatus status)
    {
        ArgumentNullException.ThrowIfNull(parser);
        parsedDescription = default;

        if (!IsValidSessionDescription(description))
        {
            status = SdpStatus.InvalidSyntax;
            return false;
        }

        status = parser.TryParse(description.Sdp, out SdpSessionDescription sdp);
        if (status != SdpStatus.Success)
        {
            return false;
        }

        var mediaDescriptions = new WebRtcMediaDescription[sdp.MediaSections.Length];
        for (int i = 0; i < sdp.MediaSections.Length; i++)
        {
            SdpMediaSection media = sdp.MediaSections.Span[i];
            if (!TryCreateMediaDescription(sdp, media, out mediaDescriptions[i]))
            {
                status = SdpStatus.InvalidSyntax;
                return false;
            }
        }

        parsedDescription = new WebRtcParsedSessionDescription
        {
            Description = description,
            Sdp = sdp,
            MediaDescriptions = mediaDescriptions
        };
        status = SdpStatus.Success;
        return true;
    }

    private static bool IsValidSessionDescription(in WebRtcSessionDescription description)
    {
        return description.Type is WebRtcSessionDescriptionType.Offer or WebRtcSessionDescriptionType.Answer &&
            !string.IsNullOrWhiteSpace(description.Sdp);
    }

    private static bool TryCreateMediaDescription(
        in SdpSessionDescription session,
        in SdpMediaSection media,
        out WebRtcMediaDescription description)
    {
        description = default;
        if (!TryGetIceCredentials(session, media, out IceCredentials credentials) ||
            !TryGetExpectedPeerIdentity(session, media, out ExpectedPeerIdentity identity))
        {
            return false;
        }

        var candidates = new IceCandidate[media.IceCandidates.Length];
        for (int i = 0; i < media.IceCandidates.Length; i++)
        {
            string candidateText = media.IceCandidates.Span[i];
            if (!IceCandidateParser.TryParse(
                    candidateText,
                    media.Mid,
                    out candidates[i],
                    out IceCandidateRejectReason rejectReason) ||
                rejectReason != IceCandidateRejectReason.None)
            {
                return false;
            }
        }

        if (!TryParseSetup(media.Setup, out WebRtcDtlsSetup setup) ||
            setup == WebRtcDtlsSetup.Unknown)
        {
            return false;
        }

        description = new WebRtcMediaDescription
        {
            SdpMedia = media,
            Mid = media.Mid,
            IceCredentials = credentials,
            ExpectedPeerIdentity = identity,
            Setup = setup,
            IceCandidates = candidates
        };
        return true;
    }

    private static bool TryGetIceCredentials(
        in SdpSessionDescription session,
        in SdpMediaSection media,
        out IceCredentials credentials)
    {
        string? usernameFragment = media.IceUsernameFragment ?? session.IceUsernameFragment;
        string? password = media.IcePassword ?? session.IcePassword;
        if (string.IsNullOrWhiteSpace(usernameFragment) || string.IsNullOrWhiteSpace(password))
        {
            credentials = default;
            return false;
        }

        credentials = new IceCredentials
        {
            UsernameFragment = usernameFragment,
            Password = password
        };
        return true;
    }

    private static bool TryGetExpectedPeerIdentity(
        in SdpSessionDescription session,
        in SdpMediaSection media,
        out ExpectedPeerIdentity identity)
    {
        ReadOnlyMemory<SdpFingerprint> fingerprints = media.Fingerprints.IsEmpty
            ? session.Fingerprints
            : media.Fingerprints;
        if (fingerprints.IsEmpty)
        {
            identity = default;
            return false;
        }

        SdpFingerprint fingerprint = fingerprints.Span[0];
        CertificateFingerprintAlgorithm algorithm = ParseFingerprintAlgorithm(fingerprint.Algorithm);
        if (algorithm == CertificateFingerprintAlgorithm.Unknown || fingerprint.Fingerprint.IsEmpty)
        {
            identity = default;
            return false;
        }

        identity = new ExpectedPeerIdentity
        {
            FingerprintAlgorithm = algorithm,
            Fingerprint = fingerprint.Fingerprint
        };
        return true;
    }

    private static CertificateFingerprintAlgorithm ParseFingerprintAlgorithm(string algorithm)
    {
        return algorithm.ToLowerInvariant() switch
        {
            "sha-256" or "sha256" => CertificateFingerprintAlgorithm.Sha256,
            "sha-384" or "sha384" => CertificateFingerprintAlgorithm.Sha384,
            "sha-512" or "sha512" => CertificateFingerprintAlgorithm.Sha512,
            _ => CertificateFingerprintAlgorithm.Unknown
        };
    }

    private static bool TryParseSetup(string? setup, out WebRtcDtlsSetup parsedSetup)
    {
        parsedSetup = setup?.ToLowerInvariant() switch
        {
            "active" => WebRtcDtlsSetup.Active,
            "passive" => WebRtcDtlsSetup.Passive,
            "actpass" => WebRtcDtlsSetup.ActPass,
            "holdconn" => WebRtcDtlsSetup.HoldConn,
            _ => WebRtcDtlsSetup.Unknown
        };

        return setup is null || parsedSetup != WebRtcDtlsSetup.Unknown;
    }
}

/// <summary>
/// Configures one local WebRTC audio media description for SDP offer/answer generation.
/// </summary>
public sealed class WebRtcAudioSessionDescriptionOptions
{
    /// <summary>Gets the SDP origin value.</summary>
    public string Origin { get; init; } = "- 0 0 IN IP4 127.0.0.1";

    /// <summary>Gets the SDP session name.</summary>
    public string SessionName { get; init; } = "-";

    /// <summary>Gets the audio media identifier.</summary>
    public string Mid { get; init; } = "0";

    /// <summary>Gets the media port used in the m-line.</summary>
    public int Port { get; init; } = 9;

    /// <summary>Gets the RTP transport profile.</summary>
    public string Protocol { get; init; } = "UDP/TLS/RTP/SAVPF";

    /// <summary>Gets the preferred media direction.</summary>
    public SdpMediaDirection Direction { get; init; } = SdpMediaDirection.SendRecv;

    /// <summary>Gets the local ICE credentials.</summary>
    public required IceCredentials LocalIceCredentials { get; init; }

    /// <summary>Gets the local DTLS certificate used to publish the SDP fingerprint.</summary>
    public required LocalCertificate LocalCertificate { get; init; }

    /// <summary>Gets the certificate fingerprint algorithm to publish.</summary>
    public CertificateFingerprintAlgorithm FingerprintAlgorithm { get; init; } = CertificateFingerprintAlgorithm.Sha256;

    /// <summary>Gets the local DTLS setup attribute.</summary>
    public WebRtcDtlsSetup Setup { get; init; } = WebRtcDtlsSetup.ActPass;

    /// <summary>Gets the payload types offered by the audio media section.</summary>
    public required ReadOnlyMemory<byte> PayloadTypes { get; init; }

    /// <summary>Gets RTP map declarations for local payload types.</summary>
    public required ReadOnlyMemory<SdpRtpMap> RtpMaps { get; init; }

    /// <summary>Gets fmtp declarations for local payload types.</summary>
    public ReadOnlyMemory<SdpFmtp> Fmtps { get; init; }

    /// <summary>Gets RTCP feedback declarations for local payload types.</summary>
    public ReadOnlyMemory<SdpRtcpFeedback> RtcpFeedback { get; init; }

    /// <summary>Gets RTP header extension mappings.</summary>
    public ReadOnlyMemory<SdpExtMap> ExtMaps { get; init; }

    /// <summary>Gets local ICE candidates.</summary>
    public ReadOnlyMemory<IceCandidate> LocalCandidates { get; init; }

    /// <summary>Gets a value indicating whether end-of-candidates should be written.</summary>
    public bool EndOfCandidates { get; init; }

    /// <summary>Gets a value indicating whether RTP and RTCP are multiplexed.</summary>
    public bool RtcpMux { get; init; } = true;

    /// <summary>Gets a value indicating whether reduced-size RTCP is requested.</summary>
    public bool RtcpReducedSize { get; init; } = true;

    /// <summary>Gets local SSRC attributes.</summary>
    public ReadOnlyMemory<SdpSsrcAttribute> SsrcAttributes { get; init; }

    /// <summary>Gets local MSID declarations.</summary>
    public ReadOnlyMemory<SdpMsid> Msids { get; init; }

    /// <summary>Gets additional media-level attributes.</summary>
    public ReadOnlyMemory<SdpAttribute> Attributes { get; init; }
}

/// <summary>
/// Writes local WebRTC SDP offers and answers from typed, AOT-safe control-plane values.
/// </summary>
public static class WebRtcSessionDescriptionBuilder
{
    /// <summary>Attempts to create a local WebRTC offer.</summary>
    public static bool TryCreateOffer(
        WebRtcAudioSessionDescriptionOptions options,
        ISdpWriter writer,
        out WebRtcSessionDescription description,
        out SdpStatus status)
    {
        return TryCreateDescription(
            WebRtcSessionDescriptionType.Offer,
            options,
            writer,
            options.Mid,
            options.Direction,
            options.Setup,
            options.PayloadTypes,
            options.RtpMaps,
            options.Fmtps,
            options.RtcpFeedback,
            options.ExtMaps,
            out description,
            out status);
    }

    /// <summary>Attempts to create a local WebRTC answer for a parsed remote offer.</summary>
    public static bool TryCreateAnswer(
        in WebRtcParsedSessionDescription remoteOffer,
        WebRtcAudioSessionDescriptionOptions options,
        ISdpWriter writer,
        out WebRtcSessionDescription description,
        out SdpStatus status)
    {
        description = default;
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(writer);

        if (remoteOffer.Description.Type != WebRtcSessionDescriptionType.Offer ||
            remoteOffer.MediaDescriptions.IsEmpty)
        {
            status = SdpStatus.InvalidSyntax;
            return false;
        }

        WebRtcMediaDescription remoteMedia = remoteOffer.MediaDescriptions.Span[0];
        if (remoteMedia.SdpMedia.Kind != SdpMediaKind.Audio ||
            !TryResolveAnswerSetup(remoteMedia.Setup, options.Setup, out WebRtcDtlsSetup answerSetup) ||
            !TrySelectPayloads(remoteMedia.SdpMedia, options, out byte[] selectedPayloadTypes) ||
            selectedPayloadTypes.Length == 0)
        {
            status = SdpStatus.UnsupportedMediaProfile;
            return false;
        }

        SdpMediaDirection answerDirection = ResolveAnswerDirection(remoteMedia.SdpMedia.Direction, options.Direction);
        string mid = remoteMedia.Mid ?? options.Mid;
        SdpRtpMap[] rtpMaps = FilterRtpMaps(options.RtpMaps.Span, selectedPayloadTypes);
        SdpFmtp[] fmtps = FilterFmtps(options.Fmtps.Span, selectedPayloadTypes);
        SdpRtcpFeedback[] feedback = FilterRtcpFeedback(options.RtcpFeedback.Span, selectedPayloadTypes);
        SdpExtMap[] extMaps = FilterExtMaps(options.ExtMaps.Span, remoteMedia.SdpMedia.ExtMaps.Span);

        return TryCreateDescription(
            WebRtcSessionDescriptionType.Answer,
            options,
            writer,
            mid,
            answerDirection,
            answerSetup,
            selectedPayloadTypes,
            rtpMaps,
            fmtps,
            feedback,
            extMaps,
            out description,
            out status);
    }

    private static bool TryCreateDescription(
        WebRtcSessionDescriptionType type,
        WebRtcAudioSessionDescriptionOptions options,
        ISdpWriter writer,
        string mid,
        SdpMediaDirection direction,
        WebRtcDtlsSetup setup,
        ReadOnlyMemory<byte> payloadTypes,
        ReadOnlyMemory<SdpRtpMap> rtpMaps,
        ReadOnlyMemory<SdpFmtp> fmtps,
        ReadOnlyMemory<SdpRtcpFeedback> rtcpFeedback,
        ReadOnlyMemory<SdpExtMap> extMaps,
        out WebRtcSessionDescription description,
        out SdpStatus status)
    {
        description = default;
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryCreateFingerprint(options.LocalCertificate, options.FingerprintAlgorithm, out SdpFingerprint fingerprint) ||
            !TryFormatSetup(setup, out string setupText) ||
            !TryFormatLocalCandidates(options.LocalCandidates.Span, out string[] candidates))
        {
            status = SdpStatus.InvalidSyntax;
            return false;
        }

        var session = new SdpSessionDescription
        {
            Origin = options.Origin,
            SessionName = options.SessionName,
            BundleMids = new[] { mid },
            Fingerprints = new[] { fingerprint },
            IceUsernameFragment = options.LocalIceCredentials.UsernameFragment,
            IcePassword = options.LocalIceCredentials.Password,
            MediaSections = new[]
            {
                new SdpMediaSection
                {
                    Kind = SdpMediaKind.Audio,
                    Mid = mid,
                    Protocol = options.Protocol,
                    Port = options.Port,
                    Direction = direction,
                    PayloadTypes = payloadTypes,
                    RtpMaps = rtpMaps,
                    Fmtps = fmtps,
                    RtcpFeedback = rtcpFeedback,
                    ExtMaps = extMaps,
                    Setup = setupText,
                    RtcpMux = options.RtcpMux,
                    RtcpReducedSize = options.RtcpReducedSize,
                    IceCandidates = candidates,
                    EndOfCandidates = options.EndOfCandidates,
                    SsrcAttributes = options.SsrcAttributes,
                    Msids = options.Msids,
                    Attributes = options.Attributes
                }
            }
        };

        var buffer = new ArrayBufferWriter<char>();
        status = writer.TryWrite(session, buffer);
        if (status != SdpStatus.Success)
        {
            return false;
        }

        description = new WebRtcSessionDescription
        {
            Type = type,
            Sdp = new string(buffer.WrittenSpan)
        };
        return true;
    }

    private static bool TryCreateFingerprint(
        LocalCertificate certificate,
        CertificateFingerprintAlgorithm algorithm,
        out SdpFingerprint fingerprint)
    {
        fingerprint = default;
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(certificate.Certificate);
        byte[] rawData = certificate.Certificate.RawData;
        byte[] hash = algorithm switch
        {
            CertificateFingerprintAlgorithm.Sha256 => SHA256.HashData(rawData),
            CertificateFingerprintAlgorithm.Sha384 => SHA384.HashData(rawData),
            CertificateFingerprintAlgorithm.Sha512 => SHA512.HashData(rawData),
            _ => []
        };

        if (hash.Length == 0)
        {
            return false;
        }

        fingerprint = new SdpFingerprint
        {
            Algorithm = FormatFingerprintAlgorithm(algorithm),
            Fingerprint = hash
        };
        return true;
    }

    private static string FormatFingerprintAlgorithm(CertificateFingerprintAlgorithm algorithm)
    {
        return algorithm switch
        {
            CertificateFingerprintAlgorithm.Sha256 => "sha-256",
            CertificateFingerprintAlgorithm.Sha384 => "sha-384",
            CertificateFingerprintAlgorithm.Sha512 => "sha-512",
            _ => string.Empty
        };
    }

    private static bool TryFormatSetup(WebRtcDtlsSetup setup, out string value)
    {
        value = setup switch
        {
            WebRtcDtlsSetup.Active => "active",
            WebRtcDtlsSetup.Passive => "passive",
            WebRtcDtlsSetup.ActPass => "actpass",
            WebRtcDtlsSetup.HoldConn => "holdconn",
            _ => string.Empty
        };
        return value.Length != 0;
    }

    private static bool TryResolveAnswerSetup(
        WebRtcDtlsSetup remoteSetup,
        WebRtcDtlsSetup preferredLocalSetup,
        out WebRtcDtlsSetup answerSetup)
    {
        answerSetup = remoteSetup switch
        {
            WebRtcDtlsSetup.Active => WebRtcDtlsSetup.Passive,
            WebRtcDtlsSetup.Passive => WebRtcDtlsSetup.Active,
            WebRtcDtlsSetup.ActPass => preferredLocalSetup is WebRtcDtlsSetup.Passive
                ? WebRtcDtlsSetup.Passive
                : WebRtcDtlsSetup.Active,
            _ => WebRtcDtlsSetup.Unknown
        };
        return answerSetup != WebRtcDtlsSetup.Unknown;
    }

    private static SdpMediaDirection ResolveAnswerDirection(
        SdpMediaDirection remoteDirection,
        SdpMediaDirection preferredLocalDirection)
    {
        return remoteDirection switch
        {
            SdpMediaDirection.SendRecv => preferredLocalDirection,
            SdpMediaDirection.SendOnly => CanReceive(preferredLocalDirection) ? SdpMediaDirection.RecvOnly : SdpMediaDirection.Inactive,
            SdpMediaDirection.RecvOnly => CanSend(preferredLocalDirection) ? SdpMediaDirection.SendOnly : SdpMediaDirection.Inactive,
            _ => SdpMediaDirection.Inactive
        };
    }

    private static bool CanSend(SdpMediaDirection direction)
    {
        return direction is SdpMediaDirection.SendRecv or SdpMediaDirection.SendOnly;
    }

    private static bool CanReceive(SdpMediaDirection direction)
    {
        return direction is SdpMediaDirection.SendRecv or SdpMediaDirection.RecvOnly;
    }

    private static bool TrySelectPayloads(
        in SdpMediaSection remoteMedia,
        WebRtcAudioSessionDescriptionOptions options,
        out byte[] selectedPayloadTypes)
    {
        var selected = new List<byte>();
        foreach (byte payloadType in options.PayloadTypes.Span)
        {
            if (PayloadTypeIsListed(remoteMedia.PayloadTypes.Span, payloadType) &&
                HasMatchingRtpMap(payloadType, options.RtpMaps.Span, remoteMedia.RtpMaps.Span))
            {
                selected.Add(payloadType);
            }
        }

        selectedPayloadTypes = selected.ToArray();
        return true;
    }

    private static bool HasMatchingRtpMap(
        byte payloadType,
        ReadOnlySpan<SdpRtpMap> localMaps,
        ReadOnlySpan<SdpRtpMap> remoteMaps)
    {
        if (!TryFindRtpMap(localMaps, payloadType, out SdpRtpMap localMap))
        {
            return false;
        }

        return TryFindRtpMap(remoteMaps, payloadType, out SdpRtpMap remoteMap) &&
            localMap.EncodingName.Equals(remoteMap.EncodingName, StringComparison.OrdinalIgnoreCase) &&
            localMap.ClockRate == remoteMap.ClockRate &&
            localMap.ChannelCount == remoteMap.ChannelCount;
    }

    private static bool TryFindRtpMap(ReadOnlySpan<SdpRtpMap> maps, byte payloadType, out SdpRtpMap map)
    {
        foreach (SdpRtpMap candidate in maps)
        {
            if (candidate.PayloadType == payloadType)
            {
                map = candidate;
                return true;
            }
        }

        map = default;
        return false;
    }

    private static bool PayloadTypeIsListed(ReadOnlySpan<byte> payloadTypes, byte payloadType)
    {
        foreach (byte candidate in payloadTypes)
        {
            if (candidate == payloadType)
            {
                return true;
            }
        }

        return false;
    }

    private static SdpRtpMap[] FilterRtpMaps(ReadOnlySpan<SdpRtpMap> maps, ReadOnlySpan<byte> payloadTypes)
    {
        var filtered = new List<SdpRtpMap>();
        foreach (SdpRtpMap map in maps)
        {
            if (PayloadTypeIsListed(payloadTypes, map.PayloadType))
            {
                filtered.Add(map);
            }
        }

        return filtered.ToArray();
    }

    private static SdpFmtp[] FilterFmtps(ReadOnlySpan<SdpFmtp> fmtps, ReadOnlySpan<byte> payloadTypes)
    {
        var filtered = new List<SdpFmtp>();
        foreach (SdpFmtp fmtp in fmtps)
        {
            if (PayloadTypeIsListed(payloadTypes, fmtp.PayloadType))
            {
                filtered.Add(fmtp);
            }
        }

        return filtered.ToArray();
    }

    private static SdpRtcpFeedback[] FilterRtcpFeedback(
        ReadOnlySpan<SdpRtcpFeedback> feedback,
        ReadOnlySpan<byte> payloadTypes)
    {
        var filtered = new List<SdpRtcpFeedback>();
        foreach (SdpRtcpFeedback item in feedback)
        {
            if (item.PayloadType is null || PayloadTypeIsListed(payloadTypes, item.PayloadType.Value))
            {
                filtered.Add(item);
            }
        }

        return filtered.ToArray();
    }

    private static SdpExtMap[] FilterExtMaps(ReadOnlySpan<SdpExtMap> localExtMaps, ReadOnlySpan<SdpExtMap> remoteExtMaps)
    {
        if (localExtMaps.IsEmpty || remoteExtMaps.IsEmpty)
        {
            return [];
        }

        var filtered = new List<SdpExtMap>();
        foreach (SdpExtMap local in localExtMaps)
        {
            foreach (SdpExtMap remote in remoteExtMaps)
            {
                if (local.Uri.Equals(remote.Uri, StringComparison.Ordinal))
                {
                    filtered.Add(local);
                    break;
                }
            }
        }

        return filtered.ToArray();
    }

    private static bool TryFormatLocalCandidates(ReadOnlySpan<IceCandidate> candidates, out string[] formattedCandidates)
    {
        formattedCandidates = new string[candidates.Length];
        for (int i = 0; i < candidates.Length; i++)
        {
            var writer = new ArrayBufferWriter<char>();
            if (!IceCandidateParser.TryWrite(candidates[i], writer))
            {
                formattedCandidates = [];
                return false;
            }

            ReadOnlySpan<char> candidate = writer.WrittenSpan;
            const string Prefix = "candidate:";
            formattedCandidates[i] = candidate.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                ? candidate[Prefix.Length..].ToString()
                : candidate.ToString();
        }

        return true;
    }
}

/// <summary>
/// Registers WebRTC providers explicitly without reflection, assembly scanning, or convention discovery.
/// </summary>
public interface IWebRtcProviderRegistry
{
    /// <summary>Registers the SDP parser used by WebRTC offer/answer processing.</summary>
    void UseSdpParser(ISdpParser parser);

    /// <summary>Registers the SDP writer used by WebRTC offer/answer generation.</summary>
    void UseSdpWriter(ISdpWriter writer);

    /// <summary>Registers the DTLS-SRTP handshake provider.</summary>
    void UseSecureHandshake(ISecureHandshake handshake);

    /// <summary>Registers the peer identity verifier.</summary>
    void UsePeerIdentityVerifier(IPeerIdentityVerifier verifier);

    /// <summary>Registers the DTLS-SRTP key schedule.</summary>
    void UseSrtpKeySchedule(ISrtpKeySchedule keySchedule);

    /// <summary>Registers the packet protector factory for role-resolved SRTP material.</summary>
    void UsePacketProtectorFactory(IWebRtcPacketProtectorFactoryProvider packetProtectorFactoryProvider);
}

/// <summary>
/// Stores explicitly registered WebRTC providers without reflection or assembly scanning.
/// </summary>
public sealed class WebRtcProviderRegistry : IWebRtcProviderRegistry
{
    /// <summary>Gets the registered SDP parser.</summary>
    public ISdpParser? SdpParser { get; private set; }

    /// <summary>Gets the registered SDP writer.</summary>
    public ISdpWriter? SdpWriter { get; private set; }

    /// <summary>Gets the registered secure handshake provider.</summary>
    public ISecureHandshake? SecureHandshake { get; private set; }

    /// <summary>Gets the registered peer identity verifier.</summary>
    public IPeerIdentityVerifier? PeerIdentityVerifier { get; private set; }

    /// <summary>Gets the registered SRTP key schedule.</summary>
    public ISrtpKeySchedule? SrtpKeySchedule { get; private set; }

    /// <summary>Gets the registered packet protector factory provider.</summary>
    public IWebRtcPacketProtectorFactoryProvider? PacketProtectorFactoryProvider { get; private set; }

    /// <inheritdoc />
    public void UseSdpParser(ISdpParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        SdpParser = parser;
    }

    /// <inheritdoc />
    public void UseSdpWriter(ISdpWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        SdpWriter = writer;
    }

    /// <inheritdoc />
    public void UseSecureHandshake(ISecureHandshake handshake)
    {
        ArgumentNullException.ThrowIfNull(handshake);
        SecureHandshake = handshake;
    }

    /// <inheritdoc />
    public void UsePeerIdentityVerifier(IPeerIdentityVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        PeerIdentityVerifier = verifier;
    }

    /// <inheritdoc />
    public void UseSrtpKeySchedule(ISrtpKeySchedule keySchedule)
    {
        ArgumentNullException.ThrowIfNull(keySchedule);
        SrtpKeySchedule = keySchedule;
    }

    /// <inheritdoc />
    public void UsePacketProtectorFactory(IWebRtcPacketProtectorFactoryProvider packetProtectorFactoryProvider)
    {
        ArgumentNullException.ThrowIfNull(packetProtectorFactoryProvider);
        PacketProtectorFactoryProvider = packetProtectorFactoryProvider;
    }
}

/// <summary>
/// Creates packet protector factories for role-resolved WebRTC SRTP material.
/// </summary>
public interface IWebRtcPacketProtectorFactoryProvider
{
    /// <summary>Creates a packet protector factory for one negotiated SRTP material set.</summary>
    IPacketProtectorFactory Create(SrtpProtectionMaterial material);
}

/// <summary>
/// Verifies SDP-signaled certificate fingerprints against DTLS peer proof material.
/// </summary>
public sealed class CertificateFingerprintPeerIdentityVerifier : IPeerIdentityVerifier
{
    /// <inheritdoc />
    public PeerIdentityVerificationResult Verify(PeerProofMaterial proof, ExpectedPeerIdentity expected)
    {
        if (proof.CertificateDer.IsEmpty)
        {
            return Failure("Peer certificate proof is empty.");
        }

        int expectedLength = GetHashLength(expected.FingerprintAlgorithm);
        if (expectedLength == 0)
        {
            return Failure("Unsupported certificate fingerprint algorithm.");
        }

        if (expected.Fingerprint.Length != expectedLength)
        {
            return Failure("Certificate fingerprint length does not match the selected algorithm.");
        }

        Span<byte> actual = stackalloc byte[64];
        if (!TryHash(expected.FingerprintAlgorithm, proof.CertificateDer.Span, actual, out int bytesWritten) ||
            bytesWritten != expectedLength)
        {
            return Failure("Unable to hash peer certificate proof.");
        }

        bool verified = CryptographicOperations.FixedTimeEquals(
            actual[..expectedLength],
            expected.Fingerprint.Span);

        return verified
            ? new PeerIdentityVerificationResult { IsVerified = true }
            : Failure("Certificate fingerprint did not match.");
    }

    private static int GetHashLength(CertificateFingerprintAlgorithm algorithm)
    {
        return algorithm switch
        {
            CertificateFingerprintAlgorithm.Sha256 => SHA256.HashSizeInBytes,
            CertificateFingerprintAlgorithm.Sha384 => SHA384.HashSizeInBytes,
            CertificateFingerprintAlgorithm.Sha512 => SHA512.HashSizeInBytes,
            _ => 0
        };
    }

    private static bool TryHash(
        CertificateFingerprintAlgorithm algorithm,
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesWritten)
    {
        return algorithm switch
        {
            CertificateFingerprintAlgorithm.Sha256 => SHA256.TryHashData(source, destination, out bytesWritten),
            CertificateFingerprintAlgorithm.Sha384 => SHA384.TryHashData(source, destination, out bytesWritten),
            CertificateFingerprintAlgorithm.Sha512 => SHA512.TryHashData(source, destination, out bytesWritten),
            _ => Unsupported(out bytesWritten)
        };
    }

    private static bool Unsupported(out int bytesWritten)
    {
        bytesWritten = 0;
        return false;
    }

    private static PeerIdentityVerificationResult Failure(string reason)
    {
        return new PeerIdentityVerificationResult
        {
            IsVerified = false,
            FailureReason = reason
        };
    }
}

/// <summary>
/// Derives WebRTC DTLS-SRTP packet protection material from RFC 5764 exporter output.
/// </summary>
public sealed class WebRtcSrtpKeySchedule : ISrtpKeySchedule
{
    private const string ExporterLabel = "EXTRACTOR-dtls_srtp";

    /// <inheritdoc />
    public SrtpProtectionMaterial Derive(SecureHandshakeResult handshake)
    {
        GetProfileLengths(handshake.NegotiatedSrtpProfile, out int keyLength, out int saltLength);
        int exportLength = (keyLength * 2) + (saltLength * 2);
        Span<byte> keyBlock = stackalloc byte[exportLength];

        if (!handshake.KeyExporter.TryExport(ExporterLabel, ReadOnlySpan<byte>.Empty, keyBlock))
        {
            throw new InvalidOperationException("DTLS key exporter did not produce SRTP keying material.");
        }

        ReadOnlySpan<byte> clientKey = keyBlock[..keyLength];
        ReadOnlySpan<byte> serverKey = keyBlock[keyLength..(keyLength * 2)];
        ReadOnlySpan<byte> clientSalt = keyBlock[(keyLength * 2)..((keyLength * 2) + saltLength)];
        ReadOnlySpan<byte> serverSalt = keyBlock[((keyLength * 2) + saltLength)..exportLength];

        bool localIsClient = handshake.LocalRole == DtlsRole.Client;
        return new SrtpProtectionMaterial
        {
            Profile = handshake.NegotiatedSrtpProfile,
            OutboundMasterKey = Copy(localIsClient ? clientKey : serverKey),
            OutboundMasterSalt = Copy(localIsClient ? clientSalt : serverSalt),
            InboundMasterKey = Copy(localIsClient ? serverKey : clientKey),
            InboundMasterSalt = Copy(localIsClient ? serverSalt : clientSalt)
        };
    }

    private static void GetProfileLengths(SrtpProtectionProfile profile, out int keyLength, out int saltLength)
    {
        switch (profile)
        {
            case SrtpProtectionProfile.Aes128CmHmacSha1_80:
            case SrtpProtectionProfile.Aes128CmHmacSha1_32:
                keyLength = 16;
                saltLength = 14;
                break;
            case SrtpProtectionProfile.AeadAes128Gcm:
                keyLength = 16;
                saltLength = 12;
                break;
            default:
                throw new NotSupportedException($"Unsupported SRTP protection profile: {profile}.");
        }
    }

    private static byte[] Copy(ReadOnlySpan<byte> source)
    {
        byte[] copy = new byte[source.Length];
        source.CopyTo(copy);
        return copy;
    }
}

/// <summary>
/// Selects the ICE behavior used to create a datagram path.
/// </summary>
public enum IceMode
{
    IceLite = 0,
    PublicHostFull = 1,
    Full = 2
}

/// <summary>
/// Selects which local ICE candidate types may be gathered.
/// </summary>
public enum IceGatheringPolicy
{
    All = 0,
    HostOnly = 1,
    RelayOnly = 2
}

/// <summary>
/// Identifies an ICE candidate type.
/// </summary>
public enum IceCandidateType
{
    Host = 0,
    ServerReflexive = 1,
    PeerReflexive = 2,
    Relay = 3
}

/// <summary>
/// Classifies ICE candidate rejection without requiring string matching.
/// </summary>
public enum IceCandidateRejectReason
{
    None = 0,
    InvalidSyntax = 1,
    UnsupportedTransport = 2,
    PolicyRejected = 3,
    Duplicate = 4,
    WrongGeneration = 5,
    MdnsResolutionFailed = 6,
    MissingCredentials = 7
}

/// <summary>
/// Carries ICE username fragment and password values.
/// </summary>
public readonly struct IceCredentials
{
    /// <summary>Gets the ICE username fragment.</summary>
    public required string UsernameFragment { get; init; }

    /// <summary>Gets the ICE password.</summary>
    public required string Password { get; init; }
}

/// <summary>
/// Describes an ICE server used for STUN srflx gathering or TURN relay allocation.
/// </summary>
public sealed class IceServerOptions
{
    /// <summary>Gets the ICE server URI.</summary>
    public required Uri Uri { get; init; }

    /// <summary>Gets the username when the server requires credentials.</summary>
    public string? Username { get; init; }

    /// <summary>Gets the credential when the server requires credentials.</summary>
    public string? Credential { get; init; }

    /// <summary>Gets the TURN long-term credential realm when already known.</summary>
    public string? Realm { get; init; }

    /// <summary>Gets the TURN long-term credential nonce when already known.</summary>
    public string? Nonce { get; init; }
}

/// <summary>
/// Resolves mDNS ICE host candidates through an explicit provider.
/// </summary>
public interface IIceMdnsResolver
{
    /// <summary>Resolves an mDNS host name to an IP address.</summary>
    ValueTask<IPAddress?> ResolveAsync(string hostName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Gathers server-reflexive ICE candidates through an explicit STUN provider.
/// </summary>
public interface IIceServerReflexiveCandidateGatherer
{
    /// <summary>Attempts to gather a server-reflexive candidate using one configured ICE server.</summary>
    ValueTask<IceCandidate?> GatherAsync(
        IceServerOptions server,
        IPEndPoint localEndPoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Allocates relay ICE candidates through an explicit TURN provider.
/// </summary>
public interface IIceRelayCandidateAllocator
{
    /// <summary>Attempts to allocate a relay candidate using one configured ICE server.</summary>
    ValueTask<IceCandidate?> AllocateAsync(
        IceServerOptions server,
        IPEndPoint localEndPoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an active TURN relay allocation with its control-plane lifecycle.
/// </summary>
public interface ITurnRelayAllocation : IAsyncDisposable
{
    /// <summary>Gets the local relay ICE candidate.</summary>
    IceCandidate Candidate { get; }

    /// <summary>Gets the relayed endpoint assigned by the TURN server.</summary>
    IPEndPoint RelayedEndPoint { get; }

    /// <summary>Gets the most recently confirmed allocation lifetime.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>Creates or refreshes permission for one peer endpoint.</summary>
    ValueTask<bool> CreatePermissionAsync(IPEndPoint peerEndPoint, CancellationToken cancellationToken = default);

    /// <summary>Binds a ChannelData channel number to one peer endpoint.</summary>
    ValueTask<bool> BindChannelAsync(
        ushort channelNumber,
        IPEndPoint peerEndPoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates permission, binds a ChannelData channel, and returns a media path through the relay.
    /// </summary>
    ValueTask<IDatagramPath?> OpenChannelDataPathAsync(
        ushort channelNumber,
        IPEndPoint peerEndPoint,
        CancellationToken cancellationToken = default);

    /// <summary>Refreshes the allocation lifetime.</summary>
    ValueTask<bool> RefreshAsync(TimeSpan lifetime, CancellationToken cancellationToken = default);
}

/// <summary>
/// Performs ICE connectivity checks for a candidate pair through an explicit provider.
/// </summary>
public interface IIceConnectivityChecker
{
    /// <summary>Attempts to validate one local and remote candidate pair.</summary>
    ValueTask<bool> CheckAsync(
        Socket socket,
        IceCredentials localCredentials,
        IceCredentials remoteCredentials,
        IceCandidate localCandidate,
        IceCandidate remoteCandidate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Classifies TURN ChannelData parse and write results without exceptions for normal packet flow.
/// </summary>
public enum TurnChannelDataStatus
{
    Success = 0,
    InvalidPacket = 1,
    DestinationTooSmall = 2,
    InvalidChannelNumber = 3,
    PayloadTooLarge = 4
}

/// <summary>
/// Represents a parsed TURN ChannelData message over caller-owned bytes.
/// </summary>
public readonly ref struct TurnChannelDataView
{
    /// <summary>Initializes a new instance of the <see cref="TurnChannelDataView"/> struct.</summary>
    public TurnChannelDataView(ushort channelNumber, ReadOnlySpan<byte> payload, int encodedLength)
    {
        ChannelNumber = channelNumber;
        Payload = payload;
        EncodedLength = encodedLength;
    }

    /// <summary>Gets the TURN channel number.</summary>
    public ushort ChannelNumber { get; }

    /// <summary>Gets the ChannelData payload bytes.</summary>
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>Gets the encoded message length, including padding.</summary>
    public int EncodedLength { get; }
}

/// <summary>
/// Reads and writes TURN ChannelData messages used to carry relayed media packets.
/// </summary>
public static class TurnChannelDataMessage
{
    /// <summary>Gets the ChannelData header length in bytes.</summary>
    public const int HeaderLength = 4;

    /// <summary>Gets the minimum channel number allowed by TURN.</summary>
    public const ushort MinimumChannelNumber = 0x4000;

    /// <summary>Gets the maximum channel number allowed by TURN.</summary>
    public const ushort MaximumChannelNumber = 0x7FFF;

    /// <summary>Attempts to parse one TURN ChannelData message from caller-owned bytes.</summary>
    public static TurnChannelDataStatus TryParse(ReadOnlySpan<byte> packet, out TurnChannelDataView view)
    {
        view = default;
        if (packet.Length < HeaderLength)
        {
            return TurnChannelDataStatus.InvalidPacket;
        }

        ushort channelNumber = BinaryPrimitives.ReadUInt16BigEndian(packet);
        if (!IsValidChannelNumber(channelNumber))
        {
            return TurnChannelDataStatus.InvalidChannelNumber;
        }

        int payloadLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        int encodedLength = GetEncodedLength(payloadLength);
        if (packet.Length < encodedLength)
        {
            return TurnChannelDataStatus.InvalidPacket;
        }

        view = new TurnChannelDataView(
            channelNumber,
            packet.Slice(HeaderLength, payloadLength),
            encodedLength);
        return TurnChannelDataStatus.Success;
    }

    /// <summary>Attempts to write one TURN ChannelData message into caller-provided storage.</summary>
    public static TurnChannelDataStatus TryWrite(
        ushort channelNumber,
        ReadOnlySpan<byte> payload,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (!IsValidChannelNumber(channelNumber))
        {
            return TurnChannelDataStatus.InvalidChannelNumber;
        }

        if (payload.Length > ushort.MaxValue)
        {
            return TurnChannelDataStatus.PayloadTooLarge;
        }

        int encodedLength = GetEncodedLength(payload.Length);
        if (destination.Length < encodedLength)
        {
            return TurnChannelDataStatus.DestinationTooSmall;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, channelNumber);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)payload.Length);
        payload.CopyTo(destination[HeaderLength..]);
        destination.Slice(HeaderLength + payload.Length, encodedLength - HeaderLength - payload.Length).Clear();
        bytesWritten = encodedLength;
        return TurnChannelDataStatus.Success;
    }

    /// <summary>Gets the encoded length for a ChannelData payload length, including padding.</summary>
    public static int GetEncodedLength(int payloadLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payloadLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payloadLength, ushort.MaxValue);
        return HeaderLength + payloadLength + GetPaddingLength(payloadLength);
    }

    /// <summary>Gets a value indicating whether the channel number is in the TURN ChannelData range.</summary>
    public static bool IsValidChannelNumber(ushort channelNumber)
    {
        return channelNumber is >= MinimumChannelNumber and <= MaximumChannelNumber;
    }

    private static int GetPaddingLength(int payloadLength)
    {
        return (4 - (payloadLength & 3)) & 3;
    }
}

/// <summary>
/// Adapts a TURN allocation datagram path by wrapping outbound media in ChannelData and unwrapping inbound ChannelData.
/// </summary>
public sealed class TurnChannelDataDatagramPath : IDatagramPath
{
    private readonly IDatagramPath inner;
    private readonly ushort channelNumber;
    private readonly byte[] receiveScratch;
    private readonly byte[] sendScratch;

    /// <summary>
    /// Initializes a new instance of the <see cref="TurnChannelDataDatagramPath"/> class.
    /// </summary>
    public TurnChannelDataDatagramPath(IDatagramPath inner, ushort channelNumber, int maximumPayloadLength = 1500)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPayloadLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPayloadLength, ushort.MaxValue);
        if (!TurnChannelDataMessage.IsValidChannelNumber(channelNumber))
        {
            throw new ArgumentOutOfRangeException(nameof(channelNumber), "TURN channel number must be in the ChannelData range.");
        }

        this.inner = inner;
        this.channelNumber = channelNumber;
        int encodedLength = TurnChannelDataMessage.GetEncodedLength(maximumPayloadLength);
        receiveScratch = new byte[encodedLength];
        sendScratch = new byte[encodedLength];
    }

    /// <inheritdoc />
    public IPEndPoint LocalEndPoint => inner.LocalEndPoint;

    /// <inheritdoc />
    public IPEndPoint RemoteEndPoint => inner.RemoteEndPoint;

    /// <inheritdoc />
    public PathState State => inner.State;

    /// <inheritdoc />
    public ValueTask<PathStateChange?> ReadStateChangeAsync(CancellationToken cancellationToken = default)
    {
        return inner.ReadStateChangeAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<DatagramReceiveResult> ReceiveAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        if (destination.IsEmpty)
        {
            throw new ArgumentException("The datagram receive destination must not be empty.", nameof(destination));
        }

        while (true)
        {
            DatagramReceiveResult result = await inner.ReceiveAsync(receiveScratch, cancellationToken).ConfigureAwait(false);
            if (!result.HasDatagram)
            {
                return result;
            }

            if (TurnChannelDataMessage.TryParse(receiveScratch.AsSpan(0, result.BytesWritten), out TurnChannelDataView view) !=
                    TurnChannelDataStatus.Success ||
                view.ChannelNumber != channelNumber)
            {
                continue;
            }

            if (view.Payload.Length > destination.Length)
            {
                throw new ArgumentException("The datagram receive destination is too small for the TURN ChannelData payload.", nameof(destination));
            }

            view.Payload.CopyTo(destination.Span);
            return new DatagramReceiveResult
            {
                HasDatagram = true,
                BytesWritten = view.Payload.Length,
                LocalEndPoint = result.LocalEndPoint,
                RemoteEndPoint = result.RemoteEndPoint,
                ReceivedAt = result.ReceivedAt,
                Hint = ClassifyPayload(view.Payload)
            };
        }
    }

    /// <inheritdoc />
    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        TurnChannelDataStatus status = TurnChannelDataMessage.TryWrite(
            channelNumber,
            payload.Span,
            sendScratch,
            out int bytesWritten);
        return status == TurnChannelDataStatus.Success
            ? inner.SendAsync(sendScratch.AsMemory(0, bytesWritten), cancellationToken)
            : throw new ArgumentException($"TURN ChannelData send failed with status {status}.", nameof(payload));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return inner.DisposeAsync();
    }

    private static DatagramProtocolHint ClassifyPayload(ReadOnlySpan<byte> payload)
    {
        if (IsStunDatagram(payload))
        {
            return DatagramProtocolHint.Stun;
        }

        if (!payload.IsEmpty && payload[0] is >= 20 and <= 63)
        {
            return DatagramProtocolHint.Dtls;
        }

        if (!payload.IsEmpty && payload[0] is >= 128 and <= 191)
        {
            return DatagramProtocolHint.SrtpOrSrtcp;
        }

        return DatagramProtocolHint.Unknown;
    }

    private static bool IsStunDatagram(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < StunBindingMessage.HeaderLength ||
            (payload[0] & 0xC0) != 0 ||
            payload[4] != 0x21 ||
            payload[5] != 0x12 ||
            payload[6] != 0xA4 ||
            payload[7] != 0x42)
        {
            return false;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(payload[2..]);
        return (messageLength & 3) == 0 &&
            messageLength == payload.Length - StunBindingMessage.HeaderLength;
    }
}

/// <summary>
/// Reads and writes the STUN binding messages needed for ICE server-reflexive gathering.
/// </summary>
public static class StunBindingMessage
{
    /// <summary>Gets the STUN transaction identifier length in bytes.</summary>
    public const int TransactionIdLength = 12;

    /// <summary>Gets the length of a STUN message header in bytes.</summary>
    public const int HeaderLength = 20;

    private const ushort BindingRequest = 0x0001;
    private const ushort BindingSuccessResponse = 0x0101;
    private const ushort XorMappedAddress = 0x0020;
    private const ushort MappedAddress = 0x0001;
    private const uint MagicCookie = 0x2112A442;

    /// <summary>Creates a random STUN transaction identifier.</summary>
    public static void CreateTransactionId(Span<byte> transactionId)
    {
        if (transactionId.Length < TransactionIdLength)
        {
            throw new ArgumentException("The destination must hold a 12-byte STUN transaction identifier.", nameof(transactionId));
        }

        RandomNumberGenerator.Fill(transactionId[..TransactionIdLength]);
    }

    /// <summary>Writes one STUN binding request with no attributes.</summary>
    public static bool TryWriteBindingRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (destination.Length < HeaderLength || transactionId.Length < TransactionIdLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..TransactionIdLength].CopyTo(destination[8..]);
        bytesWritten = HeaderLength;
        return true;
    }

    /// <summary>Attempts to parse one STUN binding request and copy its transaction identifier.</summary>
    public static bool TryParseBindingRequest(
        ReadOnlySpan<byte> packet,
        Span<byte> transactionId)
    {
        if (packet.Length < HeaderLength ||
            transactionId.Length < TransactionIdLength ||
            BinaryPrimitives.ReadUInt16BigEndian(packet) != BindingRequest ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie)
        {
            return false;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((messageLength & 3) != 0 || messageLength != packet.Length - HeaderLength)
        {
            return false;
        }

        if (!HasWellFormedAttributes(packet.Slice(HeaderLength, messageLength)))
        {
            return false;
        }

        packet.Slice(8, TransactionIdLength).CopyTo(transactionId);
        return true;
    }

    /// <summary>Attempts to write a STUN binding success response for a parsed request packet.</summary>
    public static bool TryWriteBindingSuccessResponseForRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> request,
        IPEndPoint mappedEndPoint,
        out int bytesWritten)
    {
        bytesWritten = 0;
        Span<byte> transactionId = stackalloc byte[TransactionIdLength];
        return TryParseBindingRequest(request, transactionId) &&
            TryWriteBindingSuccessResponse(destination, transactionId, mappedEndPoint, out bytesWritten);
    }

    /// <summary>Attempts to parse a STUN binding success response and read the mapped endpoint.</summary>
    public static bool TryParseBindingSuccessResponse(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId,
        out IPEndPoint mappedEndPoint)
    {
        mappedEndPoint = new IPEndPoint(IPAddress.None, 0);
        if (packet.Length < HeaderLength ||
            transactionId.Length < TransactionIdLength ||
            BinaryPrimitives.ReadUInt16BigEndian(packet) != BindingSuccessResponse ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie ||
            !packet.Slice(8, TransactionIdLength).SequenceEqual(transactionId[..TransactionIdLength]))
        {
            return false;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((messageLength & 3) != 0 || messageLength != packet.Length - HeaderLength)
        {
            return false;
        }

        ReadOnlySpan<byte> attributes = packet.Slice(HeaderLength, messageLength);
        IPEndPoint? fallbackMappedAddress = null;
        while (attributes.Length >= 4)
        {
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(attributes);
            int length = BinaryPrimitives.ReadUInt16BigEndian(attributes[2..]);
            if (length > attributes.Length - 4)
            {
                return false;
            }

            ReadOnlySpan<byte> value = attributes.Slice(4, length);
            if (type == XorMappedAddress && TryParseMappedAddress(value, packet.Slice(8, TransactionIdLength), xor: true, out mappedEndPoint))
            {
                return true;
            }

            if (type == MappedAddress && TryParseMappedAddress(value, packet.Slice(8, TransactionIdLength), xor: false, out IPEndPoint mappedAddress))
            {
                fallbackMappedAddress = mappedAddress;
            }

            int paddedLength = (length + 3) & ~3;
            if (paddedLength > attributes.Length - 4)
            {
                return false;
            }

            attributes = attributes[(4 + paddedLength)..];
        }

        if (fallbackMappedAddress is not null)
        {
            mappedEndPoint = fallbackMappedAddress;
            return true;
        }

        return false;
    }

    /// <summary>Writes one STUN binding success response with an XOR-MAPPED-ADDRESS attribute.</summary>
    public static bool TryWriteBindingSuccessResponse(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        IPEndPoint mappedEndPoint,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (transactionId.Length < TransactionIdLength)
        {
            return false;
        }

        int valueLength = mappedEndPoint.AddressFamily == AddressFamily.InterNetwork ? 8 : 20;
        int attributeLength = 4 + valueLength;
        if (destination.Length < HeaderLength + attributeLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, BindingSuccessResponse);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)attributeLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..TransactionIdLength].CopyTo(destination[8..]);

        Span<byte> attribute = destination.Slice(HeaderLength, attributeLength);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, XorMappedAddress);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], (ushort)valueLength);
        WriteXorMappedAddress(attribute[4..], transactionId, mappedEndPoint);

        bytesWritten = HeaderLength + attributeLength;
        return true;
    }

    private static bool HasWellFormedAttributes(ReadOnlySpan<byte> attributes)
    {
        while (attributes.Length > 0)
        {
            if (attributes.Length < 4)
            {
                return false;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(attributes[2..]);
            if (length > attributes.Length - 4)
            {
                return false;
            }

            int paddedLength = (length + 3) & ~3;
            if (paddedLength > attributes.Length - 4)
            {
                return false;
            }

            attributes = attributes[(4 + paddedLength)..];
        }

        return true;
    }

    private static bool TryParseMappedAddress(
        ReadOnlySpan<byte> value,
        ReadOnlySpan<byte> transactionId,
        bool xor,
        out IPEndPoint mappedEndPoint)
    {
        mappedEndPoint = new IPEndPoint(IPAddress.None, 0);
        if (value.Length < 4 || value[0] != 0)
        {
            return false;
        }

        int family = value[1];
        int port = BinaryPrimitives.ReadUInt16BigEndian(value[2..]);
        if (xor)
        {
            port ^= (int)(MagicCookie >> 16);
        }

        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            return false;
        }

        if (family == 0x01)
        {
            if (value.Length != 8)
            {
                return false;
            }

            uint address = BinaryPrimitives.ReadUInt32BigEndian(value[4..]);
            if (xor)
            {
                address ^= MagicCookie;
            }

            Span<byte> addressBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(addressBytes, address);
            mappedEndPoint = new IPEndPoint(new IPAddress(addressBytes), port);
            return true;
        }

        if (family == 0x02)
        {
            if (value.Length != 20 || transactionId.Length < TransactionIdLength)
            {
                return false;
            }

            Span<byte> addressBytes = stackalloc byte[16];
            value.Slice(4, 16).CopyTo(addressBytes);
            if (xor)
            {
                Span<byte> mask = stackalloc byte[16];
                BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
                transactionId[..TransactionIdLength].CopyTo(mask[4..]);
                for (int i = 0; i < addressBytes.Length; i++)
                {
                    addressBytes[i] ^= mask[i];
                }
            }

            mappedEndPoint = new IPEndPoint(new IPAddress(addressBytes), port);
            return true;
        }

        return false;
    }

    private static void WriteXorMappedAddress(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        IPEndPoint mappedEndPoint)
    {
        destination.Clear();
        destination[1] = mappedEndPoint.AddressFamily == AddressFamily.InterNetwork ? (byte)0x01 : (byte)0x02;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(mappedEndPoint.Port ^ (int)(MagicCookie >> 16)));

        Span<byte> addressBytes = stackalloc byte[16];
        _ = mappedEndPoint.Address.TryWriteBytes(addressBytes, out int bytesWritten);
        if (bytesWritten == 4)
        {
            uint address = BinaryPrimitives.ReadUInt32BigEndian(addressBytes) ^ MagicCookie;
            BinaryPrimitives.WriteUInt32BigEndian(destination[4..], address);
            return;
        }

        Span<byte> mask = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
        transactionId[..TransactionIdLength].CopyTo(mask[4..]);
        for (int i = 0; i < 16; i++)
        {
            destination[4 + i] = (byte)(addressBytes[i] ^ mask[i]);
        }
    }
}

/// <summary>
/// Classifies TURN allocation parse and write results without exceptions for normal control flow.
/// </summary>
public enum TurnAllocationStatus
{
    Success = 0,
    InvalidPacket = 1,
    DestinationTooSmall = 2,
    UnsupportedTransport = 3,
    UnsupportedAddressFamily = 4,
    Unauthorized = 5,
    StaleNonce = 6
}

/// <summary>
/// Represents a TURN Allocate authentication challenge.
/// </summary>
public readonly struct TurnAllocationChallenge
{
    /// <summary>Gets the STUN error code.</summary>
    public required int ErrorCode { get; init; }

    /// <summary>Gets the long-term credential realm supplied by the TURN server.</summary>
    public required string Realm { get; init; }

    /// <summary>Gets the long-term credential nonce supplied by the TURN server.</summary>
    public required string Nonce { get; init; }
}

/// <summary>
/// Reads and writes the TURN Allocate request and success response fields needed for relay candidate gathering.
/// </summary>
public static class TurnAllocationMessage
{
    private const ushort AllocateRequest = 0x0003;
    private const ushort RefreshRequest = 0x0004;
    private const ushort CreatePermissionRequest = 0x0008;
    private const ushort ChannelBindRequest = 0x0009;
    private const ushort AllocateSuccessResponse = 0x0103;
    private const ushort RefreshSuccessResponse = 0x0104;
    private const ushort CreatePermissionSuccessResponse = 0x0108;
    private const ushort ChannelBindSuccessResponse = 0x0109;
    private const ushort AllocateErrorResponse = 0x0113;
    private const ushort Username = 0x0006;
    private const ushort MessageIntegrity = 0x0008;
    private const ushort ErrorCode = 0x0009;
    private const ushort ChannelNumber = 0x000C;
    private const ushort Realm = 0x0014;
    private const ushort Nonce = 0x0015;
    private const ushort RequestedTransport = 0x0019;
    private const ushort XorPeerAddress = 0x0012;
    private const ushort XorRelayedAddress = 0x0016;
    private const ushort Lifetime = 0x000D;
    private const byte UdpProtocolNumber = 17;
    private const uint MagicCookie = 0x2112A442;
    private const int MessageIntegrityValueLength = 20;
    private const ushort MinimumChannelNumber = 0x4000;
    private const ushort MaximumChannelNumber = 0x7FFF;

    /// <summary>Attempts to write a TURN Allocate request for a UDP relay transport.</summary>
    public static TurnAllocationStatus TryWriteUdpAllocateRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (destination.Length < StunBindingMessage.HeaderLength + 8)
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        if (transactionId.Length < StunBindingMessage.TransactionIdLength)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, AllocateRequest);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 8);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(destination[8..]);

        Span<byte> attribute = destination.Slice(StunBindingMessage.HeaderLength, 8);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, RequestedTransport);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], 4);
        attribute[4] = UdpProtocolNumber;
        attribute.Slice(5, 3).Clear();
        bytesWritten = StunBindingMessage.HeaderLength + 8;
        return TurnAllocationStatus.Success;
    }

    /// <summary>Creates the STUN long-term credential key for MESSAGE-INTEGRITY.</summary>
    public static byte[] CreateLongTermCredentialKey(string username, string realm, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(password);
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(username, ":", realm, ":", password));
        return MD5.HashData(input);
    }

    /// <summary>Attempts to write an authenticated TURN Allocate request for a UDP relay transport.</summary>
    public static TurnAllocationStatus TryWriteAuthenticatedUdpAllocateRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        string username,
        string realm,
        string nonce,
        ReadOnlySpan<byte> longTermCredentialKey,
        out int bytesWritten)
    {
        bytesWritten = 0;
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(nonce);
        if (transactionId.Length < StunBindingMessage.TransactionIdLength ||
            longTermCredentialKey.Length == 0)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        if (destination.Length < StunBindingMessage.HeaderLength)
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, AllocateRequest);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(destination[8..]);

        int offset = StunBindingMessage.HeaderLength;
        if (!TryWriteRequestedTransportAttribute(destination, ref offset) ||
            !TryWriteUtf8Attribute(destination, ref offset, Username, username) ||
            !TryWriteUtf8Attribute(destination, ref offset, Realm, realm) ||
            !TryWriteUtf8Attribute(destination, ref offset, Nonce, nonce) ||
            !TryWriteMessageIntegrityAttribute(destination, ref offset, longTermCredentialKey))
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        bytesWritten = offset;
        return TurnAllocationStatus.Success;
    }

    /// <summary>Attempts to parse a TURN Allocate request and verify that it asks for UDP relay transport.</summary>
    public static TurnAllocationStatus TryParseUdpAllocateRequest(
        ReadOnlySpan<byte> packet,
        Span<byte> transactionId)
    {
        if (packet.Length < StunBindingMessage.HeaderLength ||
            transactionId.Length < StunBindingMessage.TransactionIdLength ||
            BinaryPrimitives.ReadUInt16BigEndian(packet) != AllocateRequest ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((messageLength & 3) != 0 || messageLength != packet.Length - StunBindingMessage.HeaderLength)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        bool requestedUdp = false;
        ReadOnlySpan<byte> attributes = packet.Slice(StunBindingMessage.HeaderLength, messageLength);
        while (attributes.Length > 0)
        {
            if (!TryReadAttribute(ref attributes, out ushort type, out ReadOnlySpan<byte> value))
            {
                return TurnAllocationStatus.InvalidPacket;
            }

            if (type == RequestedTransport)
            {
                if (value.Length != 4)
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                requestedUdp = value[0] == UdpProtocolNumber;
            }
        }

        if (!requestedUdp)
        {
            return TurnAllocationStatus.UnsupportedTransport;
        }

        packet.Slice(8, StunBindingMessage.TransactionIdLength).CopyTo(transactionId);
        return TurnAllocationStatus.Success;
    }

    /// <summary>Attempts to verify a STUN MESSAGE-INTEGRITY attribute using a precomputed key.</summary>
    public static bool TryVerifyMessageIntegrity(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> longTermCredentialKey)
    {
        if (packet.Length < StunBindingMessage.HeaderLength ||
            longTermCredentialKey.Length == 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie)
        {
            return false;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((messageLength & 3) != 0 || messageLength != packet.Length - StunBindingMessage.HeaderLength)
        {
            return false;
        }

        int attributeOffset = StunBindingMessage.HeaderLength;
        int integrityOffset = -1;
        ReadOnlySpan<byte> attributes = packet.Slice(StunBindingMessage.HeaderLength, messageLength);
        while (attributes.Length > 0)
        {
            int absoluteOffset = attributeOffset;
            if (!TryReadAttribute(ref attributes, out ushort type, out ReadOnlySpan<byte> value))
            {
                return false;
            }

            if (type == MessageIntegrity)
            {
                if (value.Length != MessageIntegrityValueLength)
                {
                    return false;
                }

                integrityOffset = absoluteOffset;
                break;
            }

            int attributeLength = 4 + ((value.Length + 3) & ~3);
            attributeOffset += attributeLength;
        }

        if (integrityOffset < 0)
        {
            return false;
        }

        int integrityEnd = integrityOffset + 4 + MessageIntegrityValueLength;
        byte[] copy = packet[..integrityEnd].ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(2), (ushort)(integrityEnd - StunBindingMessage.HeaderLength));
        copy.AsSpan(integrityOffset + 4, MessageIntegrityValueLength).Clear();

        Span<byte> actual = stackalloc byte[MessageIntegrityValueLength];
        if (!HMACSHA1.TryHashData(longTermCredentialKey, copy, actual, out int bytesWritten) ||
            bytesWritten != MessageIntegrityValueLength)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            actual,
            packet.Slice(integrityOffset + 4, MessageIntegrityValueLength));
    }

    /// <summary>Attempts to write a TURN Allocate authentication challenge error response.</summary>
    public static TurnAllocationStatus TryWriteAllocateChallengeResponse(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        int errorCode,
        string realm,
        string nonce,
        out int bytesWritten)
    {
        bytesWritten = 0;
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(nonce);
        if (transactionId.Length < StunBindingMessage.TransactionIdLength)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        if (errorCode is not (401 or 438))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        if (destination.Length < StunBindingMessage.HeaderLength)
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, AllocateErrorResponse);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(destination[8..]);

        int offset = StunBindingMessage.HeaderLength;
        if (!TryWriteErrorCodeAttribute(destination, ref offset, errorCode) ||
            !TryWriteUtf8Attribute(destination, ref offset, Realm, realm) ||
            !TryWriteUtf8Attribute(destination, ref offset, Nonce, nonce))
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        bytesWritten = offset;
        return TurnAllocationStatus.Success;
    }

    /// <summary>Attempts to parse a TURN Allocate authentication challenge error response.</summary>
    public static TurnAllocationStatus TryParseAllocateChallengeResponse(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId,
        out TurnAllocationChallenge challenge)
    {
        challenge = default;
        if (packet.Length < StunBindingMessage.HeaderLength ||
            transactionId.Length < StunBindingMessage.TransactionIdLength ||
            BinaryPrimitives.ReadUInt16BigEndian(packet) != AllocateErrorResponse ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie ||
            !packet.Slice(8, StunBindingMessage.TransactionIdLength).SequenceEqual(transactionId[..StunBindingMessage.TransactionIdLength]))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((messageLength & 3) != 0 || messageLength != packet.Length - StunBindingMessage.HeaderLength)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        int parsedErrorCode = 0;
        string? realm = null;
        string? nonce = null;
        ReadOnlySpan<byte> attributes = packet.Slice(StunBindingMessage.HeaderLength, messageLength);
        while (attributes.Length > 0)
        {
            if (!TryReadAttribute(ref attributes, out ushort type, out ReadOnlySpan<byte> value))
            {
                return TurnAllocationStatus.InvalidPacket;
            }

            if (type == ErrorCode)
            {
                if (!TryParseErrorCode(value, out parsedErrorCode))
                {
                    return TurnAllocationStatus.InvalidPacket;
                }
            }
            else if (type == Realm)
            {
                realm = Encoding.UTF8.GetString(value);
            }
            else if (type == Nonce)
            {
                nonce = Encoding.UTF8.GetString(value);
            }
        }

        if (parsedErrorCode is not (401 or 438) ||
            string.IsNullOrEmpty(realm) ||
            string.IsNullOrEmpty(nonce))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        challenge = new TurnAllocationChallenge
        {
            ErrorCode = parsedErrorCode,
            Realm = realm,
            Nonce = nonce
        };

        return parsedErrorCode == 438
            ? TurnAllocationStatus.StaleNonce
            : TurnAllocationStatus.Unauthorized;
    }

    /// <summary>Attempts to write a TURN Allocate success response with XOR-RELAYED-ADDRESS and LIFETIME attributes.</summary>
    public static TurnAllocationStatus TryWriteAllocateSuccessResponse(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        IPEndPoint relayedEndPoint,
        TimeSpan lifetime,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (transactionId.Length < StunBindingMessage.TransactionIdLength)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        if (relayedEndPoint.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return TurnAllocationStatus.UnsupportedAddressFamily;
        }

        if (lifetime < TimeSpan.Zero || lifetime.TotalSeconds > uint.MaxValue)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        int addressValueLength = relayedEndPoint.AddressFamily == AddressFamily.InterNetwork ? 8 : 20;
        int messageLength = 4 + addressValueLength + 8;
        if (destination.Length < StunBindingMessage.HeaderLength + messageLength)
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, AllocateSuccessResponse);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)messageLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(destination[8..]);

        Span<byte> relayedAddress = destination.Slice(StunBindingMessage.HeaderLength, 4 + addressValueLength);
        BinaryPrimitives.WriteUInt16BigEndian(relayedAddress, XorRelayedAddress);
        BinaryPrimitives.WriteUInt16BigEndian(relayedAddress[2..], (ushort)addressValueLength);
        WriteXorAddress(relayedAddress[4..], transactionId, relayedEndPoint);

        Span<byte> lifetimeAttribute = destination.Slice(StunBindingMessage.HeaderLength + 4 + addressValueLength, 8);
        BinaryPrimitives.WriteUInt16BigEndian(lifetimeAttribute, Lifetime);
        BinaryPrimitives.WriteUInt16BigEndian(lifetimeAttribute[2..], 4);
        BinaryPrimitives.WriteUInt32BigEndian(lifetimeAttribute[4..], (uint)lifetime.TotalSeconds);

        bytesWritten = StunBindingMessage.HeaderLength + messageLength;
        return TurnAllocationStatus.Success;
    }

    /// <summary>Attempts to parse a TURN Allocate success response.</summary>
    public static TurnAllocationStatus TryParseAllocateSuccessResponse(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId,
        out IPEndPoint relayedEndPoint,
        out TimeSpan lifetime)
    {
        relayedEndPoint = new IPEndPoint(IPAddress.None, 0);
        lifetime = TimeSpan.Zero;
        if (packet.Length < StunBindingMessage.HeaderLength ||
            transactionId.Length < StunBindingMessage.TransactionIdLength ||
            BinaryPrimitives.ReadUInt16BigEndian(packet) != AllocateSuccessResponse ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie ||
            !packet.Slice(8, StunBindingMessage.TransactionIdLength).SequenceEqual(transactionId[..StunBindingMessage.TransactionIdLength]))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((messageLength & 3) != 0 || messageLength != packet.Length - StunBindingMessage.HeaderLength)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        bool hasRelayedAddress = false;
        ReadOnlySpan<byte> attributes = packet.Slice(StunBindingMessage.HeaderLength, messageLength);
        while (attributes.Length > 0)
        {
            if (!TryReadAttribute(ref attributes, out ushort type, out ReadOnlySpan<byte> value))
            {
                return TurnAllocationStatus.InvalidPacket;
            }

            if (type == XorRelayedAddress)
            {
                if (!TryParseXorAddress(value, packet.Slice(8, StunBindingMessage.TransactionIdLength), out relayedEndPoint))
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                hasRelayedAddress = true;
            }
            else if (type == Lifetime)
            {
                if (value.Length != 4)
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                lifetime = TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(value));
            }
        }

        return hasRelayedAddress ? TurnAllocationStatus.Success : TurnAllocationStatus.InvalidPacket;
    }

    /// <summary>Attempts to write an authenticated TURN CreatePermission request for one peer endpoint.</summary>
    public static TurnAllocationStatus TryWriteAuthenticatedCreatePermissionRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        IPEndPoint peerEndPoint,
        string username,
        string realm,
        string nonce,
        ReadOnlySpan<byte> longTermCredentialKey,
        out int bytesWritten)
    {
        bytesWritten = 0;
        ArgumentNullException.ThrowIfNull(peerEndPoint);
        if (!TryWriteAuthenticatedRequestHeader(
            destination,
            transactionId,
            CreatePermissionRequest,
            username,
            realm,
            nonce,
            longTermCredentialKey,
            out int offset))
        {
            return destination.Length < StunBindingMessage.HeaderLength
                ? TurnAllocationStatus.DestinationTooSmall
                : TurnAllocationStatus.InvalidPacket;
        }

        if (!TryWriteXorAddressAttribute(destination, ref offset, XorPeerAddress, transactionId, peerEndPoint) ||
            !TryWriteLongTermCredentialAttributes(destination, ref offset, username, realm, nonce, longTermCredentialKey))
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        bytesWritten = offset;
        return TurnAllocationStatus.Success;
    }

    /// <summary>Attempts to parse a TURN CreatePermission request and extract the peer endpoint.</summary>
    public static TurnAllocationStatus TryParseCreatePermissionRequest(
        ReadOnlySpan<byte> packet,
        Span<byte> transactionId,
        out IPEndPoint peerEndPoint)
    {
        peerEndPoint = new IPEndPoint(IPAddress.None, 0);
        if (!TryValidateRequestHeader(packet, transactionId, CreatePermissionRequest, out ReadOnlySpan<byte> attributes))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        bool hasPeer = false;
        ReadOnlySpan<byte> parsedTransactionId = packet.Slice(8, StunBindingMessage.TransactionIdLength);
        while (attributes.Length > 0)
        {
            if (!TryReadAttribute(ref attributes, out ushort type, out ReadOnlySpan<byte> value))
            {
                return TurnAllocationStatus.InvalidPacket;
            }

            if (type == XorPeerAddress)
            {
                if (!TryParseXorAddress(value, parsedTransactionId, out peerEndPoint))
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                hasPeer = true;
            }
        }

        return hasPeer ? TurnAllocationStatus.Success : TurnAllocationStatus.InvalidPacket;
    }

    /// <summary>Attempts to write an authenticated TURN Refresh request.</summary>
    public static TurnAllocationStatus TryWriteAuthenticatedRefreshRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        TimeSpan lifetime,
        string username,
        string realm,
        string nonce,
        ReadOnlySpan<byte> longTermCredentialKey,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (lifetime < TimeSpan.Zero || lifetime.TotalSeconds > uint.MaxValue)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        if (!TryWriteAuthenticatedRequestHeader(
            destination,
            transactionId,
            RefreshRequest,
            username,
            realm,
            nonce,
            longTermCredentialKey,
            out int offset))
        {
            return destination.Length < StunBindingMessage.HeaderLength
                ? TurnAllocationStatus.DestinationTooSmall
                : TurnAllocationStatus.InvalidPacket;
        }

        if (!TryWriteLifetimeAttribute(destination, ref offset, lifetime) ||
            !TryWriteLongTermCredentialAttributes(destination, ref offset, username, realm, nonce, longTermCredentialKey))
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        bytesWritten = offset;
        return TurnAllocationStatus.Success;
    }

    /// <summary>Attempts to parse a TURN Refresh request and extract the requested lifetime.</summary>
    public static TurnAllocationStatus TryParseRefreshRequest(
        ReadOnlySpan<byte> packet,
        Span<byte> transactionId,
        out TimeSpan lifetime)
    {
        lifetime = TimeSpan.Zero;
        if (!TryValidateRequestHeader(packet, transactionId, RefreshRequest, out ReadOnlySpan<byte> attributes))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        bool hasLifetime = false;
        while (attributes.Length > 0)
        {
            if (!TryReadAttribute(ref attributes, out ushort type, out ReadOnlySpan<byte> value))
            {
                return TurnAllocationStatus.InvalidPacket;
            }

            if (type == Lifetime)
            {
                if (value.Length != 4)
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                lifetime = TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(value));
                hasLifetime = true;
            }
        }

        return hasLifetime ? TurnAllocationStatus.Success : TurnAllocationStatus.InvalidPacket;
    }

    /// <summary>Attempts to write an authenticated TURN ChannelBind request.</summary>
    public static TurnAllocationStatus TryWriteAuthenticatedChannelBindRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        ushort channelNumber,
        IPEndPoint peerEndPoint,
        string username,
        string realm,
        string nonce,
        ReadOnlySpan<byte> longTermCredentialKey,
        out int bytesWritten)
    {
        bytesWritten = 0;
        ArgumentNullException.ThrowIfNull(peerEndPoint);
        if (channelNumber is < MinimumChannelNumber or > MaximumChannelNumber)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        if (!TryWriteAuthenticatedRequestHeader(
            destination,
            transactionId,
            ChannelBindRequest,
            username,
            realm,
            nonce,
            longTermCredentialKey,
            out int offset))
        {
            return destination.Length < StunBindingMessage.HeaderLength
                ? TurnAllocationStatus.DestinationTooSmall
                : TurnAllocationStatus.InvalidPacket;
        }

        if (!TryWriteChannelNumberAttribute(destination, ref offset, channelNumber) ||
            !TryWriteXorAddressAttribute(destination, ref offset, XorPeerAddress, transactionId, peerEndPoint) ||
            !TryWriteLongTermCredentialAttributes(destination, ref offset, username, realm, nonce, longTermCredentialKey))
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        bytesWritten = offset;
        return TurnAllocationStatus.Success;
    }

    /// <summary>Attempts to parse a TURN ChannelBind request.</summary>
    public static TurnAllocationStatus TryParseChannelBindRequest(
        ReadOnlySpan<byte> packet,
        Span<byte> transactionId,
        out ushort channelNumber,
        out IPEndPoint peerEndPoint)
    {
        channelNumber = 0;
        peerEndPoint = new IPEndPoint(IPAddress.None, 0);
        if (!TryValidateRequestHeader(packet, transactionId, ChannelBindRequest, out ReadOnlySpan<byte> attributes))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        bool hasChannelNumber = false;
        bool hasPeer = false;
        ReadOnlySpan<byte> parsedTransactionId = packet.Slice(8, StunBindingMessage.TransactionIdLength);
        while (attributes.Length > 0)
        {
            if (!TryReadAttribute(ref attributes, out ushort type, out ReadOnlySpan<byte> value))
            {
                return TurnAllocationStatus.InvalidPacket;
            }

            if (type == ChannelNumber)
            {
                if (value.Length != 4)
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                channelNumber = BinaryPrimitives.ReadUInt16BigEndian(value);
                if (channelNumber is < MinimumChannelNumber or > MaximumChannelNumber)
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                hasChannelNumber = true;
            }
            else if (type == XorPeerAddress)
            {
                if (!TryParseXorAddress(value, parsedTransactionId, out peerEndPoint))
                {
                    return TurnAllocationStatus.InvalidPacket;
                }

                hasPeer = true;
            }
        }

        return hasChannelNumber && hasPeer ? TurnAllocationStatus.Success : TurnAllocationStatus.InvalidPacket;
    }

    /// <summary>Attempts to write an empty TURN CreatePermission success response.</summary>
    public static TurnAllocationStatus TryWriteCreatePermissionSuccessResponse(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        out int bytesWritten) =>
        TryWriteEmptySuccessResponse(destination, transactionId, CreatePermissionSuccessResponse, out bytesWritten);

    /// <summary>Attempts to parse an empty TURN CreatePermission success response.</summary>
    public static TurnAllocationStatus TryParseCreatePermissionSuccessResponse(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId) =>
        TryParseEmptySuccessResponse(packet, transactionId, CreatePermissionSuccessResponse);

    /// <summary>Attempts to write an empty TURN Refresh success response.</summary>
    public static TurnAllocationStatus TryWriteRefreshSuccessResponse(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        out int bytesWritten) =>
        TryWriteEmptySuccessResponse(destination, transactionId, RefreshSuccessResponse, out bytesWritten);

    /// <summary>Attempts to parse an empty TURN Refresh success response.</summary>
    public static TurnAllocationStatus TryParseRefreshSuccessResponse(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId) =>
        TryParseEmptySuccessResponse(packet, transactionId, RefreshSuccessResponse);

    /// <summary>Attempts to write an empty TURN ChannelBind success response.</summary>
    public static TurnAllocationStatus TryWriteChannelBindSuccessResponse(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        out int bytesWritten) =>
        TryWriteEmptySuccessResponse(destination, transactionId, ChannelBindSuccessResponse, out bytesWritten);

    /// <summary>Attempts to parse an empty TURN ChannelBind success response.</summary>
    public static TurnAllocationStatus TryParseChannelBindSuccessResponse(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId) =>
        TryParseEmptySuccessResponse(packet, transactionId, ChannelBindSuccessResponse);

    private static bool TryReadAttribute(
        ref ReadOnlySpan<byte> attributes,
        out ushort type,
        out ReadOnlySpan<byte> value)
    {
        type = 0;
        value = default;
        if (attributes.Length < 4)
        {
            return false;
        }

        type = BinaryPrimitives.ReadUInt16BigEndian(attributes);
        int length = BinaryPrimitives.ReadUInt16BigEndian(attributes[2..]);
        if (length > attributes.Length - 4)
        {
            return false;
        }

        int paddedLength = (length + 3) & ~3;
        if (paddedLength > attributes.Length - 4)
        {
            return false;
        }

        value = attributes.Slice(4, length);
        attributes = attributes[(4 + paddedLength)..];
        return true;
    }

    private static bool TryValidateRequestHeader(
        ReadOnlySpan<byte> packet,
        Span<byte> transactionId,
        ushort expectedMessageType,
        out ReadOnlySpan<byte> attributes)
    {
        attributes = default;
        if (packet.Length < StunBindingMessage.HeaderLength ||
            transactionId.Length < StunBindingMessage.TransactionIdLength ||
            BinaryPrimitives.ReadUInt16BigEndian(packet) != expectedMessageType ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie)
        {
            return false;
        }

        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(packet[2..]);
        if ((messageLength & 3) != 0 || messageLength != packet.Length - StunBindingMessage.HeaderLength)
        {
            return false;
        }

        packet.Slice(8, StunBindingMessage.TransactionIdLength).CopyTo(transactionId);
        attributes = packet.Slice(StunBindingMessage.HeaderLength, messageLength);
        return true;
    }

    private static bool TryWriteAuthenticatedRequestHeader(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        ushort messageType,
        string username,
        string realm,
        string nonce,
        ReadOnlySpan<byte> longTermCredentialKey,
        out int offset)
    {
        offset = 0;
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(nonce);
        if (transactionId.Length < StunBindingMessage.TransactionIdLength ||
            longTermCredentialKey.Length == 0)
        {
            return false;
        }

        if (destination.Length < StunBindingMessage.HeaderLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, messageType);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(destination[8..]);
        offset = StunBindingMessage.HeaderLength;
        return true;
    }

    private static bool TryWriteLongTermCredentialAttributes(
        Span<byte> destination,
        ref int offset,
        string username,
        string realm,
        string nonce,
        ReadOnlySpan<byte> longTermCredentialKey) =>
        TryWriteUtf8Attribute(destination, ref offset, Username, username) &&
        TryWriteUtf8Attribute(destination, ref offset, Realm, realm) &&
        TryWriteUtf8Attribute(destination, ref offset, Nonce, nonce) &&
        TryWriteMessageIntegrityAttribute(destination, ref offset, longTermCredentialKey);

    private static bool TryWriteRequestedTransportAttribute(Span<byte> destination, ref int offset)
    {
        if (destination.Length < offset + 8)
        {
            return false;
        }

        Span<byte> attribute = destination.Slice(offset, 8);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, RequestedTransport);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], 4);
        attribute[4] = UdpProtocolNumber;
        attribute.Slice(5, 3).Clear();
        offset += 8;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(offset - StunBindingMessage.HeaderLength));
        return true;
    }

    private static bool TryWriteLifetimeAttribute(Span<byte> destination, ref int offset, TimeSpan lifetime)
    {
        if (destination.Length < offset + 8)
        {
            return false;
        }

        Span<byte> attribute = destination.Slice(offset, 8);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, Lifetime);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], 4);
        BinaryPrimitives.WriteUInt32BigEndian(attribute[4..], (uint)lifetime.TotalSeconds);
        offset += 8;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(offset - StunBindingMessage.HeaderLength));
        return true;
    }

    private static bool TryWriteChannelNumberAttribute(Span<byte> destination, ref int offset, ushort channelNumber)
    {
        if (destination.Length < offset + 8)
        {
            return false;
        }

        Span<byte> attribute = destination.Slice(offset, 8);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, ChannelNumber);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], 4);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[4..], channelNumber);
        attribute.Slice(6, 2).Clear();
        offset += 8;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(offset - StunBindingMessage.HeaderLength));
        return true;
    }

    private static bool TryWriteXorAddressAttribute(
        Span<byte> destination,
        ref int offset,
        ushort type,
        ReadOnlySpan<byte> transactionId,
        IPEndPoint endPoint)
    {
        if (endPoint.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
        {
            return false;
        }

        int addressValueLength = endPoint.AddressFamily == AddressFamily.InterNetwork ? 8 : 20;
        if (destination.Length < offset + 4 + addressValueLength)
        {
            return false;
        }

        Span<byte> attribute = destination.Slice(offset, 4 + addressValueLength);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, type);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], (ushort)addressValueLength);
        WriteXorAddress(attribute[4..], transactionId, endPoint);
        offset += 4 + addressValueLength;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(offset - StunBindingMessage.HeaderLength));
        return true;
    }

    private static bool TryWriteUtf8Attribute(Span<byte> destination, ref int offset, ushort type, string value)
    {
        int valueLength = Encoding.UTF8.GetByteCount(value);
        int paddedValueLength = (valueLength + 3) & ~3;
        if (valueLength > ushort.MaxValue || destination.Length < offset + 4 + paddedValueLength)
        {
            return false;
        }

        Span<byte> attribute = destination.Slice(offset, 4 + paddedValueLength);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, type);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], (ushort)valueLength);
        int written = Encoding.UTF8.GetBytes(value, attribute[4..]);
        attribute.Slice(4 + written, paddedValueLength - written).Clear();
        offset += 4 + paddedValueLength;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(offset - StunBindingMessage.HeaderLength));
        return true;
    }

    private static bool TryWriteErrorCodeAttribute(Span<byte> destination, ref int offset, int errorCode)
    {
        if (destination.Length < offset + 8)
        {
            return false;
        }

        Span<byte> attribute = destination.Slice(offset, 8);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, ErrorCode);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], 4);
        attribute[4] = 0;
        attribute[5] = 0;
        attribute[6] = (byte)(errorCode / 100);
        attribute[7] = (byte)(errorCode % 100);
        offset += 8;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(offset - StunBindingMessage.HeaderLength));
        return true;
    }

    private static bool TryParseErrorCode(ReadOnlySpan<byte> value, out int errorCode)
    {
        errorCode = 0;
        if (value.Length < 4 ||
            value[0] != 0 ||
            value[1] != 0 ||
            value[2] is < 3 or > 6 ||
            value[3] > 99)
        {
            return false;
        }

        errorCode = value[2] * 100 + value[3];
        return true;
    }

    private static bool TryWriteMessageIntegrityAttribute(
        Span<byte> destination,
        ref int offset,
        ReadOnlySpan<byte> longTermCredentialKey)
    {
        if (destination.Length < offset + 4 + MessageIntegrityValueLength)
        {
            return false;
        }

        Span<byte> attribute = destination.Slice(offset, 4 + MessageIntegrityValueLength);
        BinaryPrimitives.WriteUInt16BigEndian(attribute, MessageIntegrity);
        BinaryPrimitives.WriteUInt16BigEndian(attribute[2..], MessageIntegrityValueLength);
        attribute.Slice(4, MessageIntegrityValueLength).Clear();
        offset += 4 + MessageIntegrityValueLength;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(offset - StunBindingMessage.HeaderLength));

        Span<byte> hash = stackalloc byte[MessageIntegrityValueLength];
        if (!HMACSHA1.TryHashData(longTermCredentialKey, destination[..offset], hash, out int bytesWritten) ||
            bytesWritten != MessageIntegrityValueLength)
        {
            return false;
        }

        hash.CopyTo(attribute[4..]);
        return true;
    }

    private static bool TryParseXorAddress(
        ReadOnlySpan<byte> value,
        ReadOnlySpan<byte> transactionId,
        out IPEndPoint endPoint)
    {
        endPoint = new IPEndPoint(IPAddress.None, 0);
        if (value.Length < 4 || value[0] != 0)
        {
            return false;
        }

        int port = BinaryPrimitives.ReadUInt16BigEndian(value[2..]) ^ (int)(MagicCookie >> 16);
        if (port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            return false;
        }

        if (value[1] == 0x01)
        {
            if (value.Length != 8)
            {
                return false;
            }

            uint address = BinaryPrimitives.ReadUInt32BigEndian(value[4..]) ^ MagicCookie;
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, address);
            endPoint = new IPEndPoint(new IPAddress(bytes), port);
            return true;
        }

        if (value[1] == 0x02)
        {
            if (value.Length != 20 || transactionId.Length < StunBindingMessage.TransactionIdLength)
            {
                return false;
            }

            Span<byte> bytes = stackalloc byte[16];
            value.Slice(4, 16).CopyTo(bytes);
            Span<byte> mask = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
            transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(mask[4..]);
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= mask[i];
            }

            endPoint = new IPEndPoint(new IPAddress(bytes), port);
            return true;
        }

        return false;
    }

    private static void WriteXorAddress(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        IPEndPoint endPoint)
    {
        destination.Clear();
        destination[1] = endPoint.AddressFamily == AddressFamily.InterNetwork ? (byte)0x01 : (byte)0x02;
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(endPoint.Port ^ (int)(MagicCookie >> 16)));

        Span<byte> addressBytes = stackalloc byte[16];
        _ = endPoint.Address.TryWriteBytes(addressBytes, out int bytesWritten);
        if (bytesWritten == 4)
        {
            uint address = BinaryPrimitives.ReadUInt32BigEndian(addressBytes) ^ MagicCookie;
            BinaryPrimitives.WriteUInt32BigEndian(destination[4..], address);
            return;
        }

        Span<byte> mask = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(mask, MagicCookie);
        transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(mask[4..]);
        for (int i = 0; i < 16; i++)
        {
            destination[4 + i] = (byte)(addressBytes[i] ^ mask[i]);
        }
    }

    private static TurnAllocationStatus TryWriteEmptySuccessResponse(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        ushort messageType,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (transactionId.Length < StunBindingMessage.TransactionIdLength)
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        if (destination.Length < StunBindingMessage.HeaderLength)
        {
            return TurnAllocationStatus.DestinationTooSmall;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, messageType);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], MagicCookie);
        transactionId[..StunBindingMessage.TransactionIdLength].CopyTo(destination[8..]);
        bytesWritten = StunBindingMessage.HeaderLength;
        return TurnAllocationStatus.Success;
    }

    private static TurnAllocationStatus TryParseEmptySuccessResponse(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId,
        ushort messageType)
    {
        if (packet.Length != StunBindingMessage.HeaderLength ||
            transactionId.Length < StunBindingMessage.TransactionIdLength ||
            BinaryPrimitives.ReadUInt16BigEndian(packet) != messageType ||
            BinaryPrimitives.ReadUInt16BigEndian(packet[2..]) != 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != MagicCookie ||
            !packet.Slice(8, StunBindingMessage.TransactionIdLength).SequenceEqual(transactionId[..StunBindingMessage.TransactionIdLength]))
        {
            return TurnAllocationStatus.InvalidPacket;
        }

        return TurnAllocationStatus.Success;
    }
}

/// <summary>
/// Gathers server-reflexive ICE candidates by sending STUN binding requests over UDP.
/// </summary>
public sealed class UdpStunServerReflexiveCandidateGatherer : IIceServerReflexiveCandidateGatherer
{
    private readonly TimeSpan timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpStunServerReflexiveCandidateGatherer"/> class.
    /// </summary>
    public UdpStunServerReflexiveCandidateGatherer(TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    /// <inheritdoc />
    public async ValueTask<IceCandidate?> GatherAsync(
        IceServerOptions server,
        IPEndPoint localEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetUdpStunServerEndPoint(server.Uri, out string host, out int port))
        {
            return null;
        }

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        IPAddress? address = SelectCompatibleAddress(addresses, localEndPoint.AddressFamily);
        if (address is null)
        {
            return null;
        }

        using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(NormalizeBindAddress(localEndPoint.Address, address.AddressFamily), 0));

        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        StunBindingMessage.CreateTransactionId(transactionId);
        byte[] request = new byte[StunBindingMessage.HeaderLength];
        if (!StunBindingMessage.TryWriteBindingRequest(request, transactionId, out int requestLength))
        {
            return null;
        }

        var serverEndPoint = new IPEndPoint(address, port);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        _ = await socket.SendToAsync(request.AsMemory(0, requestLength), SocketFlags.None, serverEndPoint, timeoutSource.Token)
            .ConfigureAwait(false);

        byte[] response = new byte[576];
        EndPoint remote = new IPEndPoint(address.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        SocketReceiveFromResult result = await socket.ReceiveFromAsync(
            response,
            SocketFlags.None,
            remote,
            timeoutSource.Token).ConfigureAwait(false);

        if (result.RemoteEndPoint is not IPEndPoint responseEndPoint ||
            !responseEndPoint.Address.Equals(address) ||
            responseEndPoint.Port != port ||
            !StunBindingMessage.TryParseBindingSuccessResponse(response.AsSpan(0, result.ReceivedBytes), transactionId, out IPEndPoint mappedEndPoint))
        {
            return null;
        }

        return new IceCandidate
        {
            Foundation = "srflx",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 1_690_000_000,
            EndPoint = mappedEndPoint,
            CandidateType = IceCandidateType.ServerReflexive,
            ExtensionAttributes = new[]
            {
                new IceCandidateAttribute { Name = "raddr", Value = localEndPoint.Address.ToString() },
                new IceCandidateAttribute { Name = "rport", Value = localEndPoint.Port.ToString(CultureInfo.InvariantCulture) }
            }
        };
    }

    private static bool TryGetUdpStunServerEndPoint(Uri uri, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (!uri.Scheme.Equals("stun", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string server = uri.IsAbsoluteUri && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : uri.OriginalString[(uri.Scheme.Length + 1)..];
        int colon = server.LastIndexOf(':');
        if (colon >= 0 && int.TryParse(server[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort))
        {
            host = server[..colon];
            port = parsedPort;
        }
        else
        {
            host = server;
            port = 3478;
        }

        return !string.IsNullOrWhiteSpace(host) && port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
    }

    private static IPAddress? SelectCompatibleAddress(IPAddress[] addresses, AddressFamily addressFamily)
    {
        for (int i = 0; i < addresses.Length; i++)
        {
            if (addresses[i].AddressFamily == addressFamily)
            {
                return addresses[i];
            }
        }

        return addresses.Length == 0 ? null : addresses[0];
    }

    private static IPAddress NormalizeBindAddress(IPAddress address, AddressFamily addressFamily)
    {
        if (addressFamily == AddressFamily.InterNetworkV6)
        {
            return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.Equals(IPAddress.IPv6Loopback)
                ? address
                : IPAddress.IPv6Any;
        }

        return address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Loopback)
            ? address
            : IPAddress.Any;
    }
}

/// <summary>
/// Allocates relay ICE candidates by sending TURN Allocate requests over UDP.
/// </summary>
public sealed class UdpTurnRelayCandidateAllocator : IIceRelayCandidateAllocator
{
    private readonly TimeSpan timeout;

    /// <summary>
    /// Initializes a new instance of the <see cref="UdpTurnRelayCandidateAllocator"/> class.
    /// </summary>
    public UdpTurnRelayCandidateAllocator(TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    /// <inheritdoc />
    public async ValueTask<IceCandidate?> AllocateAsync(
        IceServerOptions server,
        IPEndPoint localEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetUdpTurnServerEndPoint(server.Uri, out string host, out int port))
        {
            return null;
        }

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        IPAddress? address = SelectCompatibleAddress(addresses, localEndPoint.AddressFamily);
        if (address is null)
        {
            return null;
        }

        using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(NormalizeBindAddress(localEndPoint.Address, address.AddressFamily), 0));

        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        StunBindingMessage.CreateTransactionId(transactionId);
        byte[] request = new byte[576];
        TurnAllocationStatus requestStatus = TryWriteAllocateRequest(server, request, transactionId, out int requestLength);
        if (requestStatus != TurnAllocationStatus.Success)
        {
            return null;
        }

        var serverEndPoint = new IPEndPoint(address, port);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        _ = await socket.SendToAsync(request.AsMemory(0, requestLength), SocketFlags.None, serverEndPoint, timeoutSource.Token)
            .ConfigureAwait(false);

        byte[] response = new byte[576];
        EndPoint remote = new IPEndPoint(address.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        SocketReceiveFromResult result = await socket.ReceiveFromAsync(
            response,
            SocketFlags.None,
            remote,
            timeoutSource.Token).ConfigureAwait(false);

        if (result.RemoteEndPoint is not IPEndPoint responseEndPoint ||
            !IsExpectedTurnServer(responseEndPoint, address, port))
        {
            return null;
        }

        TurnAllocationStatus responseStatus = TurnAllocationMessage.TryParseAllocateSuccessResponse(
            response.AsSpan(0, result.ReceivedBytes),
            transactionId,
            out IPEndPoint relayedEndPoint,
            out TimeSpan lifetime);
        if (responseStatus != TurnAllocationStatus.Success)
        {
            TurnAllocationStatus challengeStatus = TurnAllocationMessage.TryParseAllocateChallengeResponse(
                response.AsSpan(0, result.ReceivedBytes),
                transactionId,
                out TurnAllocationChallenge challenge);
            if (challengeStatus is not (TurnAllocationStatus.Unauthorized or TurnAllocationStatus.StaleNonce) ||
                string.IsNullOrEmpty(server.Username) ||
                string.IsNullOrEmpty(server.Credential))
            {
                return null;
            }

            StunBindingMessage.CreateTransactionId(transactionId);
            byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey(
                server.Username,
                challenge.Realm,
                server.Credential);
            requestStatus = TurnAllocationMessage.TryWriteAuthenticatedUdpAllocateRequest(
                request,
                transactionId,
                server.Username,
                challenge.Realm,
                challenge.Nonce,
                key,
                out requestLength);
            if (requestStatus != TurnAllocationStatus.Success)
            {
                return null;
            }

            _ = await socket.SendToAsync(request.AsMemory(0, requestLength), SocketFlags.None, serverEndPoint, timeoutSource.Token)
                .ConfigureAwait(false);

            result = await socket.ReceiveFromAsync(
                response,
                SocketFlags.None,
                remote,
                timeoutSource.Token).ConfigureAwait(false);

            if (result.RemoteEndPoint is not IPEndPoint retryResponseEndPoint ||
                !IsExpectedTurnServer(retryResponseEndPoint, address, port) ||
                TurnAllocationMessage.TryParseAllocateSuccessResponse(
                    response.AsSpan(0, result.ReceivedBytes),
                    transactionId,
                    out relayedEndPoint,
                    out lifetime) != TurnAllocationStatus.Success)
            {
                return null;
            }
        }

        return new IceCandidate
        {
            Foundation = "relay",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 16_777_215,
            EndPoint = relayedEndPoint,
            CandidateType = IceCandidateType.Relay,
            ExtensionAttributes = new[]
            {
                new IceCandidateAttribute { Name = "raddr", Value = localEndPoint.Address.ToString() },
                new IceCandidateAttribute { Name = "rport", Value = localEndPoint.Port.ToString(CultureInfo.InvariantCulture) },
                new IceCandidateAttribute { Name = "turn-lifetime", Value = ((uint)lifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture) }
            }
        };
    }

    private static bool TryGetUdpTurnServerEndPoint(Uri uri, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (!uri.Scheme.Equals("turn", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string server = uri.IsAbsoluteUri && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : uri.OriginalString[(uri.Scheme.Length + 1)..];
        int colon = server.LastIndexOf(':');
        if (colon >= 0 && int.TryParse(server[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort))
        {
            host = server[..colon];
            port = parsedPort;
        }
        else
        {
            host = server;
            port = 3478;
        }

        return !string.IsNullOrWhiteSpace(host) && port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
    }

    private static bool IsExpectedTurnServer(IPEndPoint responseEndPoint, IPAddress address, int port) =>
        responseEndPoint.Address.Equals(address) && responseEndPoint.Port == port;

    private static TurnAllocationStatus TryWriteAllocateRequest(
        IceServerOptions server,
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        out int requestLength)
    {
        requestLength = 0;
        if (!string.IsNullOrEmpty(server.Username) &&
            !string.IsNullOrEmpty(server.Credential) &&
            !string.IsNullOrEmpty(server.Realm) &&
            !string.IsNullOrEmpty(server.Nonce))
        {
            byte[] key = TurnAllocationMessage.CreateLongTermCredentialKey(server.Username, server.Realm, server.Credential);
            return TurnAllocationMessage.TryWriteAuthenticatedUdpAllocateRequest(
                destination,
                transactionId,
                server.Username,
                server.Realm,
                server.Nonce,
                key,
                out requestLength);
        }

        return TurnAllocationMessage.TryWriteUdpAllocateRequest(destination, transactionId, out requestLength);
    }

    private static IPAddress? SelectCompatibleAddress(IPAddress[] addresses, AddressFamily addressFamily)
    {
        for (int i = 0; i < addresses.Length; i++)
        {
            if (addresses[i].AddressFamily == addressFamily)
            {
                return addresses[i];
            }
        }

        return addresses.Length == 0 ? null : addresses[0];
    }

    private static IPAddress NormalizeBindAddress(IPAddress address, AddressFamily addressFamily)
    {
        if (addressFamily == AddressFamily.InterNetworkV6)
        {
            return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.Equals(IPAddress.IPv6Loopback)
                ? address
                : IPAddress.IPv6Any;
        }

        return address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Loopback)
            ? address
            : IPAddress.Any;
    }
}

/// <summary>
/// Maintains a UDP TURN relay allocation and its authenticated control-plane operations.
/// </summary>
public sealed class UdpTurnRelayAllocation : ITurnRelayAllocation
{
    private readonly Socket socket;
    private readonly IPEndPoint serverEndPoint;
    private readonly string username;
    private readonly string realm;
    private readonly string nonce;
    private readonly byte[] longTermCredentialKey;
    private readonly TimeSpan timeout;
    private bool disposed;

    private UdpTurnRelayAllocation(
        Socket socket,
        IPEndPoint serverEndPoint,
        IceCandidate candidate,
        TimeSpan lifetime,
        string username,
        string realm,
        string nonce,
        byte[] longTermCredentialKey,
        TimeSpan timeout)
    {
        this.socket = socket;
        this.serverEndPoint = serverEndPoint;
        Candidate = candidate;
        RelayedEndPoint = candidate.EndPoint ?? new IPEndPoint(IPAddress.None, 0);
        Lifetime = lifetime;
        this.username = username;
        this.realm = realm;
        this.nonce = nonce;
        this.longTermCredentialKey = longTermCredentialKey;
        this.timeout = timeout;
    }

    /// <inheritdoc />
    public IceCandidate Candidate { get; }

    /// <inheritdoc />
    public IPEndPoint RelayedEndPoint { get; }

    /// <inheritdoc />
    public TimeSpan Lifetime { get; private set; }

    /// <summary>Allocates a UDP TURN relay and keeps the allocation socket open for lifecycle operations.</summary>
    public static async ValueTask<UdpTurnRelayAllocation?> AllocateAsync(
        IceServerOptions server,
        IPEndPoint localEndPoint,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        cancellationToken.ThrowIfCancellationRequested();
        TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(3);
        if (string.IsNullOrEmpty(server.Username) ||
            string.IsNullOrEmpty(server.Credential) ||
            !TryGetUdpTurnServerEndPoint(server.Uri, out string host, out int port))
        {
            return null;
        }

        IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        IPAddress? address = SelectCompatibleAddress(addresses, localEndPoint.AddressFamily);
        if (address is null)
        {
            return null;
        }

        var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(NormalizeBindAddress(localEndPoint.Address, address.AddressFamily), 0));
            var serverEndPoint = new IPEndPoint(address, port);
            byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
            byte[] request = new byte[576];
            byte[] response = new byte[576];
            string? realm = server.Realm;
            string? nonce = server.Nonce;
            byte[]? key = !string.IsNullOrEmpty(realm) && !string.IsNullOrEmpty(nonce)
                ? TurnAllocationMessage.CreateLongTermCredentialKey(server.Username, realm, server.Credential)
                : null;

            StunBindingMessage.CreateTransactionId(transactionId);
            TurnAllocationStatus writeStatus = key is null
                ? TurnAllocationMessage.TryWriteUdpAllocateRequest(request, transactionId, out int requestLength)
                : TurnAllocationMessage.TryWriteAuthenticatedUdpAllocateRequest(
                    request,
                    transactionId,
                    server.Username,
                    realm!,
                    nonce!,
                    key,
                    out requestLength);
            if (writeStatus != TurnAllocationStatus.Success)
            {
                return null;
            }

            int receivedBytes = await SendReceiveAsync(
                socket,
                serverEndPoint,
                request.AsMemory(0, requestLength),
                response,
                effectiveTimeout,
                cancellationToken).ConfigureAwait(false);
            if (receivedBytes <= 0)
            {
                return null;
            }

            TurnAllocationStatus successStatus = TurnAllocationMessage.TryParseAllocateSuccessResponse(
                response.AsSpan(0, receivedBytes),
                transactionId,
                out IPEndPoint relayedEndPoint,
                out TimeSpan lifetime);
            if (successStatus != TurnAllocationStatus.Success)
            {
                TurnAllocationStatus challengeStatus = TurnAllocationMessage.TryParseAllocateChallengeResponse(
                    response.AsSpan(0, receivedBytes),
                    transactionId,
                    out TurnAllocationChallenge challenge);
                if (challengeStatus is not (TurnAllocationStatus.Unauthorized or TurnAllocationStatus.StaleNonce))
                {
                    return null;
                }

                realm = challenge.Realm;
                nonce = challenge.Nonce;
                key = TurnAllocationMessage.CreateLongTermCredentialKey(server.Username, realm, server.Credential);
                StunBindingMessage.CreateTransactionId(transactionId);
                writeStatus = TurnAllocationMessage.TryWriteAuthenticatedUdpAllocateRequest(
                    request,
                    transactionId,
                    server.Username,
                    realm,
                    nonce,
                    key,
                    out requestLength);
                if (writeStatus != TurnAllocationStatus.Success)
                {
                    return null;
                }

                receivedBytes = await SendReceiveAsync(
                    socket,
                    serverEndPoint,
                    request.AsMemory(0, requestLength),
                    response,
                    effectiveTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (receivedBytes <= 0 ||
                    TurnAllocationMessage.TryParseAllocateSuccessResponse(
                        response.AsSpan(0, receivedBytes),
                        transactionId,
                        out relayedEndPoint,
                        out lifetime) != TurnAllocationStatus.Success)
                {
                    return null;
                }
            }

            if (string.IsNullOrEmpty(realm) || string.IsNullOrEmpty(nonce) || key is null)
            {
                return null;
            }

            var localSocketEndPoint = (IPEndPoint)socket.LocalEndPoint!;
            var candidate = new IceCandidate
            {
                Foundation = "relay",
                ComponentId = 1,
                Transport = "UDP",
                Priority = 16_777_215,
                EndPoint = relayedEndPoint,
                CandidateType = IceCandidateType.Relay,
                ExtensionAttributes = new[]
                {
                    new IceCandidateAttribute { Name = "raddr", Value = localSocketEndPoint.Address.ToString() },
                    new IceCandidateAttribute { Name = "rport", Value = localSocketEndPoint.Port.ToString(CultureInfo.InvariantCulture) },
                    new IceCandidateAttribute { Name = "turn-lifetime", Value = ((uint)lifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture) }
                }
            };

            var allocation = new UdpTurnRelayAllocation(
                socket,
                serverEndPoint,
                candidate,
                lifetime,
                server.Username,
                realm,
                nonce,
                key,
                effectiveTimeout);
            socket = null!;
            return allocation;
        }
        finally
        {
            socket?.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> CreatePermissionAsync(IPEndPoint peerEndPoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerEndPoint);
        return SendControlRequestAsync(
            (Span<byte> destination, ReadOnlySpan<byte> transactionId, out int bytesWritten) =>
                TurnAllocationMessage.TryWriteAuthenticatedCreatePermissionRequest(
                    destination,
                    transactionId,
                    peerEndPoint,
                    username,
                    realm,
                    nonce,
                    longTermCredentialKey,
                    out bytesWritten),
            TurnAllocationMessage.TryParseCreatePermissionSuccessResponse,
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<bool> BindChannelAsync(
        ushort channelNumber,
        IPEndPoint peerEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerEndPoint);
        return SendControlRequestAsync(
            (Span<byte> destination, ReadOnlySpan<byte> transactionId, out int bytesWritten) =>
                TurnAllocationMessage.TryWriteAuthenticatedChannelBindRequest(
                    destination,
                    transactionId,
                    channelNumber,
                    peerEndPoint,
                    username,
                    realm,
                    nonce,
                    longTermCredentialKey,
                    out bytesWritten),
            TurnAllocationMessage.TryParseChannelBindSuccessResponse,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IDatagramPath?> OpenChannelDataPathAsync(
        ushort channelNumber,
        IPEndPoint peerEndPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerEndPoint);
        if (disposed ||
            !await CreatePermissionAsync(peerEndPoint, cancellationToken).ConfigureAwait(false) ||
            !await BindChannelAsync(channelNumber, peerEndPoint, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TurnChannelDataDatagramPath(
            new UdpDatagramPath(socket, serverEndPoint, ownsSocket: false),
            channelNumber);
    }

    /// <inheritdoc />
    public async ValueTask<bool> RefreshAsync(TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        bool refreshed = await SendControlRequestAsync(
            (Span<byte> destination, ReadOnlySpan<byte> transactionId, out int bytesWritten) =>
                TurnAllocationMessage.TryWriteAuthenticatedRefreshRequest(
                    destination,
                    transactionId,
                    lifetime,
                    username,
                    realm,
                    nonce,
                    longTermCredentialKey,
                    out bytesWritten),
            TurnAllocationMessage.TryParseRefreshSuccessResponse,
            cancellationToken).ConfigureAwait(false);
        if (refreshed)
        {
            Lifetime = lifetime;
        }

        return refreshed;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            using var source = new CancellationTokenSource(timeout);
            _ = await RefreshAsync(TimeSpan.Zero, source.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            disposed = true;
            socket.Dispose();
        }
    }

    private delegate TurnAllocationStatus TryWriteTurnRequest(
        Span<byte> destination,
        ReadOnlySpan<byte> transactionId,
        out int bytesWritten);

    private delegate TurnAllocationStatus TryParseTurnSuccess(
        ReadOnlySpan<byte> packet,
        ReadOnlySpan<byte> transactionId);

    private async ValueTask<bool> SendControlRequestAsync(
        TryWriteTurnRequest writeRequest,
        TryParseTurnSuccess parseSuccess,
        CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return false;
        }

        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        StunBindingMessage.CreateTransactionId(transactionId);
        byte[] request = new byte[576];
        TurnAllocationStatus writeStatus = writeRequest(request, transactionId, out int requestLength);
        if (writeStatus != TurnAllocationStatus.Success)
        {
            return false;
        }

        byte[] response = new byte[576];
        int receivedBytes = await SendReceiveAsync(
            socket,
            serverEndPoint,
            request.AsMemory(0, requestLength),
            response,
            timeout,
            cancellationToken).ConfigureAwait(false);

        return receivedBytes > 0 &&
            parseSuccess(response.AsSpan(0, receivedBytes), transactionId) == TurnAllocationStatus.Success;
    }

    private static async ValueTask<int> SendReceiveAsync(
        Socket socket,
        IPEndPoint serverEndPoint,
        ReadOnlyMemory<byte> request,
        byte[] response,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        _ = await socket.SendToAsync(request, SocketFlags.None, serverEndPoint, timeoutSource.Token)
            .ConfigureAwait(false);

        EndPoint remote = new IPEndPoint(
            serverEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
            0);
        SocketReceiveFromResult result = await socket.ReceiveFromAsync(
            response,
            SocketFlags.None,
            remote,
            timeoutSource.Token).ConfigureAwait(false);
        return result.RemoteEndPoint is IPEndPoint responseEndPoint &&
            responseEndPoint.Address.Equals(serverEndPoint.Address) &&
            responseEndPoint.Port == serverEndPoint.Port
            ? result.ReceivedBytes
            : 0;
    }

    private static bool TryGetUdpTurnServerEndPoint(Uri uri, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (!uri.Scheme.Equals("turn", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string server = uri.IsAbsoluteUri && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : uri.OriginalString[(uri.Scheme.Length + 1)..];
        int colon = server.LastIndexOf(':');
        if (colon >= 0 && int.TryParse(server[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedPort))
        {
            host = server[..colon];
            port = parsedPort;
        }
        else
        {
            host = server;
            port = 3478;
        }

        return !string.IsNullOrWhiteSpace(host) && port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
    }

    private static IPAddress? SelectCompatibleAddress(IPAddress[] addresses, AddressFamily addressFamily)
    {
        for (int i = 0; i < addresses.Length; i++)
        {
            if (addresses[i].AddressFamily == addressFamily)
            {
                return addresses[i];
            }
        }

        return addresses.Length == 0 ? null : addresses[0];
    }

    private static IPAddress NormalizeBindAddress(IPAddress address, AddressFamily addressFamily)
    {
        if (addressFamily == AddressFamily.InterNetworkV6)
        {
            return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.Equals(IPAddress.IPv6Loopback)
                ? address
                : IPAddress.IPv6Any;
        }

        return address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Loopback)
            ? address
            : IPAddress.Any;
    }
}

/// <summary>
/// Performs UDP STUN binding checks for directly reachable ICE candidate pairs.
/// </summary>
public sealed class UdpIceConnectivityChecker : IIceConnectivityChecker
{
    /// <inheritdoc />
    public async ValueTask<bool> CheckAsync(
        Socket socket,
        IceCredentials localCredentials,
        IceCredentials remoteCredentials,
        IceCandidate localCandidate,
        IceCandidate remoteCandidate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(socket);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _ = localCredentials;
        _ = remoteCredentials;

        if (!IsCheckableCandidate(localCandidate, socket.AddressFamily) ||
            !IsCheckableCandidate(remoteCandidate, socket.AddressFamily) ||
            remoteCandidate.EndPoint is null)
        {
            return false;
        }

        byte[] transactionId = new byte[StunBindingMessage.TransactionIdLength];
        StunBindingMessage.CreateTransactionId(transactionId);
        byte[] request = new byte[StunBindingMessage.HeaderLength];
        if (!StunBindingMessage.TryWriteBindingRequest(request, transactionId, out int requestLength))
        {
            return false;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            _ = await socket.SendToAsync(
                request.AsMemory(0, requestLength),
                SocketFlags.None,
                remoteCandidate.EndPoint,
                timeoutSource.Token).ConfigureAwait(false);

            byte[] response = new byte[576];
            while (true)
            {
                EndPoint remote = new IPEndPoint(
                    socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any,
                    0);
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                    response,
                    SocketFlags.None,
                    remote,
                    timeoutSource.Token).ConfigureAwait(false);

                if (result.RemoteEndPoint is not IPEndPoint responseEndPoint ||
                    !responseEndPoint.Equals(remoteCandidate.EndPoint))
                {
                    continue;
                }

                return StunBindingMessage.TryParseBindingSuccessResponse(
                    response.AsSpan(0, result.ReceivedBytes),
                    transactionId,
                    out _);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static bool IsCheckableCandidate(in IceCandidate candidate, AddressFamily addressFamily)
    {
        return candidate.Transport.Equals("UDP", StringComparison.OrdinalIgnoreCase) &&
            candidate.EndPoint is not null &&
            candidate.EndPoint.AddressFamily == addressFamily;
    }
}

internal sealed class IceControlledDatagramPath(IDatagramPath inner) : IDatagramPath
{
    private readonly byte[] stunResponseScratch = new byte[64];

    public IPEndPoint LocalEndPoint => inner.LocalEndPoint;

    public IPEndPoint RemoteEndPoint => inner.RemoteEndPoint;

    public PathState State => inner.State;

    public ValueTask<PathStateChange?> ReadStateChangeAsync(CancellationToken cancellationToken = default)
    {
        return inner.ReadStateChangeAsync(cancellationToken);
    }

    public async ValueTask<DatagramReceiveResult> ReceiveAsync(
        Memory<byte> destination,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            DatagramReceiveResult result = await inner.ReceiveAsync(destination, cancellationToken).ConfigureAwait(false);
            if (!result.HasDatagram || !IsIceControlDatagram(result))
            {
                return result;
            }

            await HandleIceControlDatagramAsync(
                destination.Slice(0, result.BytesWritten),
                result.RemoteEndPoint!,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        return inner.SendAsync(payload, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return inner.DisposeAsync();
    }

    private static bool IsIceControlDatagram(in DatagramReceiveResult result)
    {
        return result.Hint == DatagramProtocolHint.Stun && result.RemoteEndPoint is not null;
    }

    private async ValueTask HandleIceControlDatagramAsync(
        Memory<byte> packet,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        if (StunBindingMessage.TryWriteBindingSuccessResponseForRequest(
            stunResponseScratch,
            packet.Span,
            remoteEndPoint,
            out int bytesWritten))
        {
            await inner.SendAsync(stunResponseScratch.AsMemory(0, bytesWritten), cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Resolves host names through <see cref="Dns"/> for deployments that provide mDNS through the platform resolver.
/// </summary>
public sealed class SystemNetIceMdnsResolver : IIceMdnsResolver
{
    /// <inheritdoc />
    public async ValueTask<IPAddress?> ResolveAsync(string hostName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostName);
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostName, cancellationToken).ConfigureAwait(false);
        return addresses.Length == 0 ? null : addresses[0];
    }
}

/// <summary>
/// Configures an ICE datagram path factory.
/// </summary>
public sealed class IceDatagramPathOptions
{
    /// <summary>Gets the ICE mode.</summary>
    public required IceMode Mode { get; init; }

    /// <summary>Gets the local UDP bind endpoint.</summary>
    public required IPEndPoint BindEndPoint { get; init; }

    /// <summary>Gets an advertised public address override for host candidates.</summary>
    public IPAddress? AdvertisedAddress { get; init; }

    /// <summary>Gets configured STUN and TURN servers.</summary>
    public ReadOnlyMemory<IceServerOptions> IceServers { get; init; }

    /// <summary>Gets the local candidate gathering policy.</summary>
    public IceGatheringPolicy GatheringPolicy { get; init; } = IceGatheringPolicy.All;

    /// <summary>Gets a value indicating whether remote mDNS candidates may be resolved.</summary>
    public bool EnableMdnsCandidateResolution { get; init; } = true;

    /// <summary>Gets the explicit mDNS resolver used for remote mDNS candidates.</summary>
    public IIceMdnsResolver? MdnsResolver { get; init; }

    /// <summary>Gets the timeout for one remote mDNS candidate resolution attempt.</summary>
    public TimeSpan MdnsResolutionTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets the number of additional mDNS resolution attempts after the first failure.</summary>
    public int MdnsResolutionRetryCount { get; init; }

    /// <summary>Gets the explicit STUN provider used to gather server-reflexive candidates.</summary>
    public IIceServerReflexiveCandidateGatherer? ServerReflexiveCandidateGatherer { get; init; }

    /// <summary>Gets the explicit TURN provider used to allocate relay candidates.</summary>
    public IIceRelayCandidateAllocator? RelayCandidateAllocator { get; init; }

    /// <summary>Gets the explicit provider used to run ICE connectivity checks.</summary>
    public IIceConnectivityChecker? ConnectivityChecker { get; init; }

    /// <summary>Gets a value indicating whether ICE restart operations are allowed.</summary>
    public bool EnableIceRestart { get; init; } = true;

    /// <summary>Gets the connectivity-check pacing interval.</summary>
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Gets the selected-pair keepalive interval.</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets the liveness timeout before degraded state.</summary>
    public TimeSpan DegradedTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Gets the liveness timeout before failed state.</summary>
    public TimeSpan FailedTimeout { get; init; } = TimeSpan.FromSeconds(16);

    /// <summary>Gets a value indicating whether trickle ICE is enabled.</summary>
    public bool Trickle { get; init; } = true;
}

internal static class IceDatagramPathOptionsValidator
{
    public static void Validate(IceDatagramPathOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.BindEndPoint);

        if (!Enum.IsDefined(options.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ICE mode must be a defined value.");
        }

        if (!Enum.IsDefined(options.GatheringPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ICE gathering policy must be a defined value.");
        }

        if (options.AdvertisedAddress is not null &&
            options.AdvertisedAddress.AddressFamily != options.BindEndPoint.AddressFamily)
        {
            throw new ArgumentException("Advertised ICE address family must match the bind endpoint address family.", nameof(options));
        }

        if (options.CheckInterval <= TimeSpan.Zero ||
            options.KeepAliveInterval <= TimeSpan.Zero ||
            options.DegradedTimeout <= TimeSpan.Zero ||
            options.FailedTimeout <= TimeSpan.Zero ||
            options.MdnsResolutionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ICE pacing and timeout values must be positive.");
        }

        if (options.MdnsResolutionRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ICE mDNS retry count must not be negative.");
        }

        if (options.FailedTimeout <= options.DegradedTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ICE failed timeout must be greater than degraded timeout.");
        }

        ReadOnlySpan<IceServerOptions> iceServers = options.IceServers.Span;
        for (int i = 0; i < iceServers.Length; i++)
        {
            ValidateIceServer(iceServers[i]);
        }
    }

    private static void ValidateIceServer(IceServerOptions? server)
    {
        if (server is null)
        {
            throw new ArgumentException("ICE server entries must not be null.");
        }

        if (server.Uri is null)
        {
            throw new ArgumentException("ICE server URI is required.");
        }

        string scheme = server.Uri.Scheme;
        if (!scheme.Equals("stun", StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals("stuns", StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals("turn", StringComparison.OrdinalIgnoreCase) &&
            !scheme.Equals("turns", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("ICE server URI scheme must be stun, stuns, turn, or turns.");
        }
    }
}

/// <summary>
/// Represents a parsed ICE candidate accepted by the ICE layer.
/// </summary>
public readonly struct IceCandidate
{
    /// <summary>Gets the candidate foundation.</summary>
    public required string Foundation { get; init; }

    /// <summary>Gets the component identifier.</summary>
    public required int ComponentId { get; init; }

    /// <summary>Gets the transport, usually UDP.</summary>
    public required string Transport { get; init; }

    /// <summary>Gets the candidate priority.</summary>
    public required uint Priority { get; init; }

    /// <summary>Gets the candidate endpoint when directly addressable.</summary>
    public IPEndPoint? EndPoint { get; init; }

    /// <summary>Gets the candidate port when the endpoint address is unresolved.</summary>
    public int? Port { get; init; }

    /// <summary>Gets the unresolved mDNS host name when present.</summary>
    public string? MdnsHostName { get; init; }

    /// <summary>Gets the candidate type.</summary>
    public required IceCandidateType CandidateType { get; init; }

    /// <summary>Gets the SDP media identifier when present.</summary>
    public string? SdpMid { get; init; }

    /// <summary>Gets optional unknown candidate extension attributes parsed outside media hot paths.</summary>
    public ReadOnlyMemory<IceCandidateAttribute> ExtensionAttributes { get; init; }
}

/// <summary>
/// Represents one parsed ICE candidate extension attribute.
/// </summary>
public readonly struct IceCandidateAttribute
{
    /// <summary>Gets the attribute name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the attribute value, or null for valueless attributes.</summary>
    public string? Value { get; init; }
}

/// <summary>
/// Parses and writes ICE candidate attributes through manual AOT-safe logic.
/// </summary>
public static class IceCandidateParser
{
    /// <summary>
    /// Attempts to parse one ICE candidate attribute value.
    /// </summary>
    public static bool TryParse(
        ReadOnlySpan<char> candidate,
        string? sdpMid,
        out IceCandidate parsedCandidate,
        out IceCandidateRejectReason rejectReason)
    {
        parsedCandidate = default;
        rejectReason = IceCandidateRejectReason.None;

        ReadOnlySpan<char> value = candidate.Trim();
        if (sdpMid is not null && string.IsNullOrWhiteSpace(sdpMid))
        {
            rejectReason = IceCandidateRejectReason.InvalidSyntax;
            return false;
        }

        if (value.IsEmpty || ContainsDisallowedCandidateWhitespace(value))
        {
            rejectReason = IceCandidateRejectReason.InvalidSyntax;
            return false;
        }

        const string Prefix = "candidate:";
        if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[Prefix.Length..];
        }

        string[] parts = value.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8 || !parts[6].Equals("typ", StringComparison.OrdinalIgnoreCase))
        {
            rejectReason = IceCandidateRejectReason.InvalidSyntax;
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int componentId) ||
            !IsValidComponentId(componentId) ||
            !uint.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint priority) ||
            !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            rejectReason = IceCandidateRejectReason.InvalidSyntax;
            return false;
        }

        string transport = parts[2];
        if (!transport.Equals("udp", StringComparison.OrdinalIgnoreCase))
        {
            rejectReason = IceCandidateRejectReason.UnsupportedTransport;
            return false;
        }

        if (!TryParseCandidateType(parts[7], out IceCandidateType candidateType))
        {
            rejectReason = IceCandidateRejectReason.InvalidSyntax;
            return false;
        }

        IPEndPoint? endPoint = null;
        string? mdnsHostName = null;
        if (IPAddress.TryParse(parts[4], out IPAddress? address))
        {
            endPoint = new IPEndPoint(address, port);
        }
        else if (parts[4].EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            if (candidateType != IceCandidateType.Host)
            {
                rejectReason = IceCandidateRejectReason.InvalidSyntax;
                return false;
            }

            mdnsHostName = parts[4];
        }
        else
        {
            rejectReason = IceCandidateRejectReason.InvalidSyntax;
            return false;
        }

        var attributes = new List<IceCandidateAttribute>();
        for (int i = 8; i < parts.Length; i++)
        {
            string name = parts[i];
            string? attributeValue = null;
            bool requiresValue = CandidateAttributeRequiresValue(name);
            if (i + 1 < parts.Length && !CandidateAttributeRequiresValue(parts[i + 1]))
            {
                attributeValue = parts[++i];
            }
            else if (requiresValue)
            {
                rejectReason = IceCandidateRejectReason.InvalidSyntax;
                return false;
            }

            attributes.Add(new IceCandidateAttribute { Name = name, Value = attributeValue });
        }

        parsedCandidate = new IceCandidate
        {
            Foundation = parts[0],
            ComponentId = componentId,
            Transport = transport.ToUpperInvariant(),
            Priority = priority,
            EndPoint = endPoint,
            Port = port,
            MdnsHostName = mdnsHostName,
            CandidateType = candidateType,
            SdpMid = sdpMid,
            ExtensionAttributes = attributes.ToArray()
        };
        return true;
    }

    /// <summary>
    /// Writes one ICE candidate attribute value including the candidate prefix.
    /// </summary>
    public static bool TryWrite(in IceCandidate candidate, IBufferWriter<char> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        string? address = candidate.EndPoint?.Address.ToString() ?? candidate.MdnsHostName;
        int? port = candidate.EndPoint?.Port ?? candidate.Port;
        bool isMdnsCandidate = candidate.EndPoint is null && candidate.MdnsHostName is not null;
        if (!TryValidateWriteCandidate(candidate, address, port, isMdnsCandidate, out string candidateType))
        {
            return false;
        }

        Write(destination, "candidate:");
        Write(destination, candidate.Foundation);
        Write(destination, " ");
        Write(destination, candidate.ComponentId.ToString(CultureInfo.InvariantCulture));
        Write(destination, " ");
        Write(destination, candidate.Transport.ToLowerInvariant());
        Write(destination, " ");
        Write(destination, candidate.Priority.ToString(CultureInfo.InvariantCulture));
        Write(destination, " ");
        Write(destination, address);
        Write(destination, " ");
        Write(destination, port.Value.ToString(CultureInfo.InvariantCulture));
        Write(destination, " typ ");
        Write(destination, candidateType);

        foreach (IceCandidateAttribute attribute in candidate.ExtensionAttributes.Span)
        {
            Write(destination, " ");
            Write(destination, attribute.Name);
            if (attribute.Value is not null)
            {
                Write(destination, " ");
                Write(destination, attribute.Value);
            }
        }

        return true;
    }

    private static bool TryValidateWriteCandidate(
        in IceCandidate candidate,
        string? address,
        int? port,
        bool isMdnsCandidate,
        out string candidateType)
    {
        candidateType = string.Empty;
        if (!IsValidCandidateToken(candidate.Foundation) ||
            string.IsNullOrWhiteSpace(candidate.Transport) ||
            ContainsAsciiWhitespace(candidate.Transport.AsSpan()) ||
            !candidate.Transport.Equals("udp", StringComparison.OrdinalIgnoreCase) ||
            !IsValidComponentId(candidate.ComponentId) ||
            (candidate.EndPoint is not null && candidate.MdnsHostName is not null) ||
            !IsValidCandidateToken(address) ||
            port is null ||
            port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort ||
            (isMdnsCandidate && candidate.CandidateType != IceCandidateType.Host) ||
            !TryFormatCandidateType(candidate.CandidateType, out candidateType))
        {
            return false;
        }

        foreach (IceCandidateAttribute attribute in candidate.ExtensionAttributes.Span)
        {
            if (!IsValidCandidateToken(attribute.Name) ||
                (attribute.Value is null && CandidateAttributeRequiresValue(attribute.Name)) ||
                (attribute.Value is not null && !IsValidCandidateToken(attribute.Value)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidComponentId(int componentId)
    {
        return componentId is 1 or 2;
    }

    private static bool TryParseCandidateType(string value, out IceCandidateType candidateType)
    {
        candidateType = value.ToLowerInvariant() switch
        {
            "host" => IceCandidateType.Host,
            "srflx" => IceCandidateType.ServerReflexive,
            "prflx" => IceCandidateType.PeerReflexive,
            "relay" => IceCandidateType.Relay,
            _ => (IceCandidateType)(-1)
        };
        return candidateType != (IceCandidateType)(-1);
    }

    private static bool TryFormatCandidateType(IceCandidateType candidateType, out string value)
    {
        value = candidateType switch
        {
            IceCandidateType.Host => "host",
            IceCandidateType.ServerReflexive => "srflx",
            IceCandidateType.PeerReflexive => "prflx",
            IceCandidateType.Relay => "relay",
            _ => string.Empty
        };
        return value.Length != 0;
    }

    private static bool CandidateAttributeRequiresValue(string value)
    {
        return value.Equals("raddr", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("rport", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("tcptype", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("generation", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("network-id", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("turn-channel", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("network-cost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAsciiWhitespace(ReadOnlySpan<char> value)
    {
        foreach (char ch in value)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDisallowedCandidateWhitespace(ReadOnlySpan<char> value)
    {
        foreach (char ch in value)
        {
            if (char.IsWhiteSpace(ch) && ch != ' ')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidCandidateToken(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !ContainsAsciiWhitespace(value.AsSpan());
    }

    private static void Write(IBufferWriter<char> destination, string value)
    {
        Span<char> span = destination.GetSpan(value.Length);
        value.AsSpan().CopyTo(span);
        destination.Advance(value.Length);
    }
}

/// <summary>
/// Represents an ICE restart request.
/// </summary>
public readonly struct IceRestartRequest
{
    /// <summary>Gets the new remote credentials.</summary>
    public required IceCredentials RemoteCredentials { get; init; }

    /// <summary>Gets the generation identifier associated with the restart.</summary>
    public required string RestartId { get; init; }
}

/// <summary>
/// Identifies an ICE candidate event kind.
/// </summary>
public enum IceCandidateEventKind
{
    LocalCandidateDiscovered = 0,
    LocalCandidateGatheringComplete = 1,
    RemoteCandidateAccepted = 2,
    RemoteEndOfCandidatesAccepted = 3,
    CandidateRejected = 4
}

/// <summary>
/// Represents an ICE candidate event.
/// </summary>
public readonly struct IceCandidateEvent
{
    /// <summary>Gets the event kind.</summary>
    public required IceCandidateEventKind Kind { get; init; }

    /// <summary>Gets the ICE candidate when present.</summary>
    public IceCandidate Candidate { get; init; }

    /// <summary>Gets the local candidate for WebRTC signaling when present.</summary>
    public WebRtcIceCandidate SignalingCandidate { get; init; }

    /// <summary>Gets the typed rejection reason for rejected candidates.</summary>
    public IceCandidateRejectReason RejectReason { get; init; }

    /// <summary>Gets an optional human-readable rejection message.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Identifies an ICE path event kind.
/// </summary>
public enum IcePathEventKind
{
    CheckingStarted = 0,
    CandidatePairSucceeded = 1,
    CandidatePairNominated = 2,
    SelectedPairChanged = 3,
    Ready = 4,
    Degraded = 5,
    Failed = 6,
    Closed = 7,
    RestartStarted = 8,
    RestartCompleted = 9
}

/// <summary>
/// Represents an ICE path event.
/// </summary>
public readonly struct IcePathEvent
{
    /// <summary>Gets the event kind.</summary>
    public required IcePathEventKind Kind { get; init; }

    /// <summary>Gets the event timestamp.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets the local endpoint for pair events.</summary>
    public IPEndPoint? LocalEndPoint { get; init; }

    /// <summary>Gets the remote endpoint for pair events.</summary>
    public IPEndPoint? RemoteEndPoint { get; init; }

    /// <summary>Gets an optional reason.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets an optional restart identifier.</summary>
    public string? RestartId { get; init; }
}

/// <summary>
/// Produces a validated datagram path using ICE.
/// </summary>
public interface IIceDatagramPathFactory : IAsyncDisposable
{
    /// <summary>Gets the ICE mode.</summary>
    IceMode Mode { get; }

    /// <summary>Gets local ICE credentials.</summary>
    IceCredentials LocalCredentials { get; }

    /// <summary>Reads one local or remote candidate event, or null when candidate events complete.</summary>
    ValueTask<IceCandidateEvent?> ReadCandidateEventAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one ICE path event, or null when path events complete.</summary>
    ValueTask<IcePathEvent?> ReadPathEventAsync(CancellationToken cancellationToken = default);

    /// <summary>Installs remote ICE credentials.</summary>
    ValueTask SetRemoteCredentialsAsync(IceCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Adds a remote ICE candidate.</summary>
    ValueTask AddRemoteCandidateAsync(IceCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>Signals that no more remote candidates are expected.</summary>
    ValueTask EndRemoteCandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts or resumes ICE and returns the selected datagram path.</summary>
    ValueTask<IDatagramPath> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Performs an ICE restart while preserving the factory contract.</summary>
    ValueTask<IDatagramPath> RestartAsync(IceRestartRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates an ICE-lite datagram path using directly reachable UDP host candidates.
/// </summary>
public sealed class IceLiteDatagramPathFactory : IIceDatagramPathFactory
{
    private readonly PublicHostFullIceDatagramPathFactory inner;

    /// <summary>
    /// Initializes a new instance of the <see cref="IceLiteDatagramPathFactory"/> class.
    /// </summary>
    public IceLiteDatagramPathFactory(IceDatagramPathOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        inner = new PublicHostFullIceDatagramPathFactory(options);
    }

    /// <inheritdoc />
    public IceMode Mode => IceMode.IceLite;

    /// <inheritdoc />
    public IceCredentials LocalCredentials => inner.LocalCredentials;

    /// <inheritdoc />
    public ValueTask<IceCandidateEvent?> ReadCandidateEventAsync(CancellationToken cancellationToken = default)
    {
        return inner.ReadCandidateEventAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IcePathEvent?> ReadPathEventAsync(CancellationToken cancellationToken = default)
    {
        return inner.ReadPathEventAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SetRemoteCredentialsAsync(IceCredentials credentials, CancellationToken cancellationToken = default)
    {
        return inner.SetRemoteCredentialsAsync(credentials, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask AddRemoteCandidateAsync(IceCandidate candidate, CancellationToken cancellationToken = default)
    {
        return inner.AddRemoteCandidateAsync(candidate, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask EndRemoteCandidatesAsync(CancellationToken cancellationToken = default)
    {
        return inner.EndRemoteCandidatesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IDatagramPath> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return inner.ConnectAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IDatagramPath> RestartAsync(IceRestartRequest request, CancellationToken cancellationToken = default)
    {
        return inner.RestartAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return inner.DisposeAsync();
    }
}

/// <summary>
/// Creates a public-host ICE datagram path using directly reachable UDP candidates.
/// </summary>
public sealed class PublicHostFullIceDatagramPathFactory : IIceDatagramPathFactory
{
    private readonly WebRtcAsyncEventQueue<IceCandidateEvent> candidateEvents = new();
    private readonly WebRtcAsyncEventQueue<IcePathEvent> pathEvents = new();
    private readonly Socket socket;
    private readonly IPEndPoint localEndPoint;
    private readonly object gate = new();
    private readonly IceDatagramPathOptions options;
    private readonly IceCandidate? localCandidate;
    private IceCandidate? selectedRemoteCandidate;
    private IceCredentials? remoteCredentials;
    private bool disposed;
    private bool remoteCandidatesEnded;
    private readonly List<IceCandidate> acceptedRemoteCandidates = [];
    private const string TurnChannelAttributeName = "turn-channel";

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicHostFullIceDatagramPathFactory"/> class.
    /// </summary>
    public PublicHostFullIceDatagramPathFactory(IceDatagramPathOptions options)
    {
        IceDatagramPathOptionsValidator.Validate(options);
        this.options = options;
        socket = new Socket(options.BindEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(options.BindEndPoint);
        localEndPoint = (IPEndPoint)socket.LocalEndPoint!;
        LocalCredentials = CreateCredentials();
        Mode = IceMode.PublicHostFull;

        if (options.GatheringPolicy != IceGatheringPolicy.RelayOnly)
        {
            localCandidate = CreateHostCandidate();
            EnqueueCandidateEvent(new IceCandidateEvent
            {
                Kind = IceCandidateEventKind.LocalCandidateDiscovered,
                Candidate = localCandidate.Value,
                SignalingCandidate = new WebRtcIceCandidate
                {
                    Candidate = FormatCandidate(localCandidate.Value),
                    SdpMid = localCandidate.Value.SdpMid
                }
            });
        }

        EnqueueCandidateEvent(new IceCandidateEvent
        {
            Kind = IceCandidateEventKind.LocalCandidateGatheringComplete
        });
    }

    /// <inheritdoc />
    public IceMode Mode { get; }

    /// <inheritdoc />
    public IceCredentials LocalCredentials { get; private set; }

    /// <inheritdoc />
    public ValueTask<IceCandidateEvent?> ReadCandidateEventAsync(CancellationToken cancellationToken = default)
    {
        return candidateEvents.ReadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IcePathEvent?> ReadPathEventAsync(CancellationToken cancellationToken = default)
    {
        return pathEvents.ReadAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SetRemoteCredentialsAsync(IceCredentials credentials, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ValidateRemoteCredentials(credentials, nameof(credentials));

        lock (gate)
        {
            remoteCredentials = credentials;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask AddRemoteCandidateAsync(IceCandidate candidate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        IceCandidateRejectReason rejectReason = ValidateRemoteCandidate(candidate);
        if (rejectReason != IceCandidateRejectReason.None)
        {
            EnqueueCandidateEvent(new IceCandidateEvent
            {
                Kind = IceCandidateEventKind.CandidateRejected,
                Candidate = candidate,
                RejectReason = rejectReason,
                Message = FormatRejectReason(rejectReason)
            });
            return;
        }

        IceCandidate acceptedCandidate = candidate;
        if (acceptedCandidate.EndPoint is null &&
            acceptedCandidate.MdnsHostName is not null &&
            options.EnableMdnsCandidateResolution)
        {
            if (acceptedCandidate.Port is null || options.MdnsResolver is null)
            {
                EnqueueRejectedCandidate(candidate, IceCandidateRejectReason.MdnsResolutionFailed);
                return;
            }

            IPAddress? resolvedAddress = await ResolveMdnsCandidateAsync(acceptedCandidate.MdnsHostName, cancellationToken)
                .ConfigureAwait(false);

            if (resolvedAddress is null)
            {
                EnqueueRejectedCandidate(candidate, IceCandidateRejectReason.MdnsResolutionFailed);
                return;
            }

            if (resolvedAddress.AddressFamily != localEndPoint.AddressFamily)
            {
                EnqueueRejectedCandidate(candidate, IceCandidateRejectReason.InvalidSyntax);
                return;
            }

            acceptedCandidate = WithResolvedEndPoint(
                acceptedCandidate,
                new IPEndPoint(resolvedAddress, acceptedCandidate.Port.Value));
        }

        lock (gate)
        {
            foreach (IceCandidate existing in acceptedRemoteCandidates)
            {
                if (AreSameCandidate(existing, acceptedCandidate))
                {
                    EnqueueRejectedCandidate(candidate, IceCandidateRejectReason.Duplicate);
                    return;
                }
            }

            acceptedRemoteCandidates.Add(acceptedCandidate);
            selectedRemoteCandidate = SelectPreferredRemoteCandidate(selectedRemoteCandidate, acceptedCandidate);
        }

        EnqueueCandidateEvent(new IceCandidateEvent
        {
            Kind = IceCandidateEventKind.RemoteCandidateAccepted,
            Candidate = acceptedCandidate
        });
    }

    /// <inheritdoc />
    public ValueTask EndRemoteCandidatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        lock (gate)
        {
            remoteCandidatesEnded = true;
        }

        EnqueueCandidateEvent(new IceCandidateEvent
        {
            Kind = IceCandidateEventKind.RemoteEndOfCandidatesAccepted
        });
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<IDatagramPath> ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        IceCredentials remoteCredentialSnapshot;
        IceCandidate local;
        List<IceCandidate> remoteCandidates;
        lock (gate)
        {
            if (remoteCredentials is null)
            {
                EnqueuePathEvent(IcePathEventKind.Failed, "Remote ICE credentials are missing.");
                throw new InvalidOperationException("Remote ICE credentials must be installed before connecting.");
            }

            if (selectedRemoteCandidate is null || selectedRemoteCandidate.Value.EndPoint is null)
            {
                EnqueuePathEvent(IcePathEventKind.Failed, "No directly reachable remote ICE candidate is available.");
                throw new InvalidOperationException("A directly reachable remote ICE candidate is required.");
            }

            remoteCredentialSnapshot = remoteCredentials.Value;
            local = localCandidate ?? CreateHostCandidate();
            remoteCandidates = CreatePriorityOrderedRemoteCandidateSnapshot();
        }

        IceCandidate? selectedRemote = null;
        for (int i = 0; i < remoteCandidates.Count; i++)
        {
            IceCandidate remote = remoteCandidates[i];
            EnqueuePathEvent(IcePathEventKind.CheckingStarted, "Public host candidate pair check started.", remote.EndPoint);

            if (options.ConnectivityChecker is null)
            {
                selectedRemote = remote;
                break;
            }

            bool succeeded = await options.ConnectivityChecker
                .CheckAsync(
                    socket,
                    LocalCredentials,
                    remoteCredentialSnapshot,
                    local,
                    remote,
                    options.DegradedTimeout,
                    cancellationToken)
                .ConfigureAwait(false);

            if (succeeded)
            {
                selectedRemote = remote;
                break;
            }
        }

        if (selectedRemote is null)
        {
            IPEndPoint? failedEndPoint = remoteCandidates.Count == 0 ? null : remoteCandidates[^1].EndPoint;
            EnqueuePathEvent(IcePathEventKind.Failed, "Public host candidate pair check failed.", failedEndPoint);
            throw new InvalidOperationException("ICE connectivity check failed for all candidate pairs.");
        }

        IceCandidate selected = selectedRemote.Value;
        lock (gate)
        {
            selectedRemoteCandidate = selected;
        }

        EnqueuePathEvent(IcePathEventKind.CandidatePairSucceeded, "Public host candidate pair selected.", selected.EndPoint);
        EnqueuePathEvent(IcePathEventKind.CandidatePairNominated, "Public host candidate pair nominated.", selected.EndPoint);
        EnqueuePathEvent(IcePathEventKind.SelectedPairChanged, "Selected public host candidate pair changed.", selected.EndPoint);
        EnqueuePathEvent(IcePathEventKind.Ready, "ICE public host path ready.", selected.EndPoint);

        IDatagramPath path = new IceControlledDatagramPath(new UdpDatagramPath(socket, selected.EndPoint!, ownsSocket: false));
        return selected.CandidateType == IceCandidateType.Relay &&
            TryGetTurnChannelNumber(selected, out ushort channelNumber)
            ? new TurnChannelDataDatagramPath(path, channelNumber)
            : path;
    }

    /// <inheritdoc />
    public ValueTask<IDatagramPath> RestartAsync(IceRestartRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!options.EnableIceRestart)
        {
            EnqueuePathEvent(IcePathEventKind.Failed, "ICE restart is disabled.", restartId: request.RestartId);
            throw new InvalidOperationException("ICE restart is disabled for this factory.");
        }

        ValidateRestartRequest(request);
        EnqueuePathEvent(IcePathEventKind.RestartStarted, "ICE restart started.", restartId: request.RestartId);
        lock (gate)
        {
            remoteCredentials = request.RemoteCredentials;
            LocalCredentials = CreateCredentials();
        }

        EnqueuePathEvent(IcePathEventKind.RestartCompleted, "ICE restart completed.", restartId: request.RestartId);
        return ConnectAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
        }

        EnqueuePathEvent(IcePathEventKind.Closed, "ICE factory disposed.");
        candidateEvents.Complete();
        pathEvents.Complete();
        socket.Dispose();

        return ValueTask.CompletedTask;
    }

    private IceCandidateRejectReason ValidateRemoteCandidate(in IceCandidate candidate)
    {
        if (!IsValidCandidateToken(candidate.Foundation) ||
            !IsValidComponentId(candidate.ComponentId) ||
            !TryFormatCandidateType(candidate.CandidateType, out _) ||
            (candidate.EndPoint is not null && candidate.MdnsHostName is not null) ||
            (candidate.EndPoint is not null && candidate.EndPoint.AddressFamily != localEndPoint.AddressFamily) ||
            (candidate.EndPoint is null && candidate.MdnsHostName is null) ||
            (candidate.MdnsHostName is not null &&
             (!IsValidCandidateToken(candidate.MdnsHostName) ||
              !candidate.MdnsHostName.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
              candidate.Port is null or < IPEndPoint.MinPort or > IPEndPoint.MaxPort ||
              candidate.CandidateType != IceCandidateType.Host)))
        {
            return IceCandidateRejectReason.InvalidSyntax;
        }

        foreach (IceCandidateAttribute attribute in candidate.ExtensionAttributes.Span)
        {
            if (!IsValidCandidateToken(attribute.Name) ||
                (attribute.Value is null && CandidateAttributeRequiresValue(attribute.Name)) ||
                (attribute.Value is not null && !IsValidCandidateToken(attribute.Value)) ||
                IsInvalidTurnChannelAttribute(attribute))
            {
                return IceCandidateRejectReason.InvalidSyntax;
            }
        }

        if (string.IsNullOrWhiteSpace(candidate.Transport) ||
            ContainsAsciiWhitespace(candidate.Transport.AsSpan()))
        {
            return IceCandidateRejectReason.UnsupportedTransport;
        }

        if (!candidate.Transport.Equals("UDP", StringComparison.OrdinalIgnoreCase))
        {
            return IceCandidateRejectReason.UnsupportedTransport;
        }

        if (candidate.EndPoint is null && !options.EnableMdnsCandidateResolution)
        {
            return IceCandidateRejectReason.MdnsResolutionFailed;
        }

        if (options.GatheringPolicy == IceGatheringPolicy.RelayOnly && candidate.CandidateType != IceCandidateType.Relay)
        {
            return IceCandidateRejectReason.PolicyRejected;
        }

        return IceCandidateRejectReason.None;
    }

    private static bool IsValidComponentId(int componentId)
    {
        return componentId is 1 or 2;
    }

    private static bool TryFormatCandidateType(IceCandidateType candidateType, out string value)
    {
        value = candidateType switch
        {
            IceCandidateType.Host => "host",
            IceCandidateType.ServerReflexive => "srflx",
            IceCandidateType.PeerReflexive => "prflx",
            IceCandidateType.Relay => "relay",
            _ => string.Empty
        };
        return value.Length != 0;
    }

    private static bool CandidateAttributeRequiresValue(string value)
    {
        return value.Equals("raddr", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("rport", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("tcptype", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("generation", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("network-id", StringComparison.OrdinalIgnoreCase) ||
            value.Equals(TurnChannelAttributeName, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("network-cost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidTurnChannelAttribute(in IceCandidateAttribute attribute)
    {
        return attribute.Name.Equals(TurnChannelAttributeName, StringComparison.OrdinalIgnoreCase) &&
            (!ushort.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort channelNumber) ||
             !TurnChannelDataMessage.IsValidChannelNumber(channelNumber));
    }

    private static bool ContainsAsciiWhitespace(ReadOnlySpan<char> value)
    {
        foreach (char ch in value)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidCandidateToken(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !ContainsAsciiWhitespace(value.AsSpan());
    }

    private void EnqueueRejectedCandidate(in IceCandidate candidate, IceCandidateRejectReason rejectReason)
    {
        EnqueueCandidateEvent(new IceCandidateEvent
        {
            Kind = IceCandidateEventKind.CandidateRejected,
            Candidate = candidate,
            RejectReason = rejectReason,
            Message = FormatRejectReason(rejectReason)
        });
    }

    private static IceCandidate WithResolvedEndPoint(in IceCandidate candidate, IPEndPoint endPoint)
    {
        return new IceCandidate
        {
            Foundation = candidate.Foundation,
            ComponentId = candidate.ComponentId,
            Transport = candidate.Transport,
            Priority = candidate.Priority,
            EndPoint = endPoint,
            Port = candidate.Port,
            MdnsHostName = candidate.MdnsHostName,
            CandidateType = candidate.CandidateType,
            SdpMid = candidate.SdpMid,
            ExtensionAttributes = candidate.ExtensionAttributes
        };
    }

    private async ValueTask<IPAddress?> ResolveMdnsCandidateAsync(
        string hostName,
        CancellationToken cancellationToken)
    {
        IIceMdnsResolver resolver = options.MdnsResolver!;
        int attemptCount = options.MdnsResolutionRetryCount + 1;
        for (int attempt = 0; attempt < attemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(options.MdnsResolutionTimeout);

            try
            {
                IPAddress? address = await resolver.ResolveAsync(hostName, attemptCts.Token).ConfigureAwait(false);
                if (address is not null)
                {
                    return address;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException)
            {
            }
        }

        return null;
    }

    private IceCandidate CreateHostCandidate()
    {
        IPAddress address = options.AdvertisedAddress ?? localEndPoint.Address;
        if (address.Equals(IPAddress.Any))
        {
            address = IPAddress.Loopback;
        }
        else if (address.Equals(IPAddress.IPv6Any))
        {
            address = IPAddress.IPv6Loopback;
        }

        return new IceCandidate
        {
            Foundation = "1",
            ComponentId = 1,
            Transport = "UDP",
            Priority = 2_126_430_207,
            EndPoint = new IPEndPoint(address, localEndPoint.Port),
            CandidateType = IceCandidateType.Host
        };
    }

    private static IceCredentials CreateCredentials()
    {
        return new IceCredentials
        {
            UsernameFragment = CreateToken(8),
            Password = CreateToken(24)
        };
    }

    private static string CreateToken(int byteCount)
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes[..byteCount]);
        return Convert.ToBase64String(bytes[..byteCount])
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string FormatCandidate(in IceCandidate candidate)
    {
        var writer = new ArrayBufferWriter<char>();
        _ = IceCandidateParser.TryWrite(candidate, writer);
        return new string(writer.WrittenSpan);
    }

    private static string FormatRejectReason(IceCandidateRejectReason reason)
    {
        return reason switch
        {
            IceCandidateRejectReason.UnsupportedTransport => "Only UDP ICE candidates are supported by this factory.",
            IceCandidateRejectReason.PolicyRejected => "The ICE candidate was rejected by gathering policy.",
            IceCandidateRejectReason.Duplicate => "The ICE candidate was already accepted.",
            IceCandidateRejectReason.MdnsResolutionFailed => "The ICE candidate requires mDNS resolution that is not available.",
            IceCandidateRejectReason.MissingCredentials => "Remote ICE credentials are missing.",
            _ => "The ICE candidate was rejected."
        };
    }

    private static bool AreSameCandidate(in IceCandidate left, in IceCandidate right)
    {
        if (!left.Foundation.Equals(right.Foundation, StringComparison.Ordinal) ||
            left.ComponentId != right.ComponentId ||
            !left.Transport.Equals(right.Transport, StringComparison.OrdinalIgnoreCase) ||
            left.CandidateType != right.CandidateType)
        {
            return false;
        }

        if (left.EndPoint is not null || right.EndPoint is not null)
        {
            return EqualityComparer<IPEndPoint>.Default.Equals(left.EndPoint, right.EndPoint);
        }

        return string.Equals(left.MdnsHostName, right.MdnsHostName, StringComparison.OrdinalIgnoreCase) &&
            left.Port == right.Port;
    }

    private static IceCandidate SelectPreferredRemoteCandidate(IceCandidate? current, in IceCandidate candidate)
    {
        return current is null || candidate.Priority > current.Value.Priority
            ? candidate
            : current.Value;
    }

    private static bool TryGetTurnChannelNumber(in IceCandidate candidate, out ushort channelNumber)
    {
        foreach (IceCandidateAttribute attribute in candidate.ExtensionAttributes.Span)
        {
            if (attribute.Name.Equals(TurnChannelAttributeName, StringComparison.OrdinalIgnoreCase) &&
                ushort.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out channelNumber) &&
                TurnChannelDataMessage.IsValidChannelNumber(channelNumber))
            {
                return true;
            }
        }

        channelNumber = 0;
        return false;
    }

    private List<IceCandidate> CreatePriorityOrderedRemoteCandidateSnapshot()
    {
        List<IceCandidate> candidates = new(acceptedRemoteCandidates.Count);
        foreach (IceCandidate candidate in acceptedRemoteCandidates)
        {
            if (candidate.EndPoint is not null)
            {
                candidates.Add(candidate);
            }
        }

        candidates.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
        return candidates;
    }

    private void EnqueueCandidateEvent(IceCandidateEvent candidateEvent)
    {
        candidateEvents.Enqueue(candidateEvent);
    }

    /// <summary>Attempts to read an already-available ICE candidate event without awaiting future events.</summary>
    public bool TryReadCandidateEvent(out IceCandidateEvent candidateEvent)
    {
        return candidateEvents.TryRead(out candidateEvent);
    }

    private void EnqueuePathEvent(
        IcePathEventKind kind,
        string reason,
        IPEndPoint? remoteEndPoint = null,
        string? restartId = null)
    {
        pathEvents.Enqueue(new IcePathEvent
        {
            Kind = kind,
            At = DateTimeOffset.UtcNow,
            LocalEndPoint = localEndPoint,
            RemoteEndPoint = remoteEndPoint,
            Reason = reason,
            RestartId = restartId
        });
    }

    private void ThrowIfDisposed()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }
    }

    private static void ValidateRemoteCredentials(in IceCredentials credentials, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(credentials.UsernameFragment) || string.IsNullOrWhiteSpace(credentials.Password))
        {
            throw new ArgumentException("Remote ICE credentials must include username fragment and password.", parameterName);
        }
    }

    private static void ValidateRestartRequest(in IceRestartRequest request)
    {
        ValidateRemoteCredentials(request.RemoteCredentials, nameof(request));
        if (string.IsNullOrWhiteSpace(request.RestartId))
        {
            throw new ArgumentException("ICE restart requests must include a restart generation identifier.", nameof(request));
        }
    }
}

/// <summary>
/// Creates a full ICE datagram path and gathers host, STUN server-reflexive, and TURN relay candidates through explicit providers.
/// </summary>
public sealed class FullIceDatagramPathFactory : IIceDatagramPathFactory
{
    private readonly PublicHostFullIceDatagramPathFactory inner;
    private readonly IceDatagramPathOptions options;
    private readonly Queue<IceCandidateEvent> candidateEvents = new();
    private readonly SemaphoreSlim gatheringGate = new(1, 1);
    private readonly object gate = new();
    private bool candidateGatheringCompleted;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FullIceDatagramPathFactory"/> class.
    /// </summary>
    public FullIceDatagramPathFactory(IceDatagramPathOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        inner = new PublicHostFullIceDatagramPathFactory(options);
    }

    /// <inheritdoc />
    public IceMode Mode => IceMode.Full;

    /// <inheritdoc />
    public IceCredentials LocalCredentials => inner.LocalCredentials;

    /// <inheritdoc />
    public async ValueTask<IceCandidateEvent?> ReadCandidateEventAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureCandidateGatheringCompletedAsync(cancellationToken).ConfigureAwait(false);

        lock (gate)
        {
            if (candidateEvents.Count != 0)
            {
                return candidateEvents.Dequeue();
            }

            if (disposed)
            {
                return null;
            }
        }

        return await inner.ReadCandidateEventAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<IcePathEvent?> ReadPathEventAsync(CancellationToken cancellationToken = default)
    {
        return inner.ReadPathEventAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SetRemoteCredentialsAsync(IceCredentials credentials, CancellationToken cancellationToken = default)
    {
        return inner.SetRemoteCredentialsAsync(credentials, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask AddRemoteCandidateAsync(IceCandidate candidate, CancellationToken cancellationToken = default)
    {
        return inner.AddRemoteCandidateAsync(candidate, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask EndRemoteCandidatesAsync(CancellationToken cancellationToken = default)
    {
        return inner.EndRemoteCandidatesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IDatagramPath> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return inner.ConnectAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IDatagramPath> RestartAsync(IceRestartRequest request, CancellationToken cancellationToken = default)
    {
        return inner.RestartAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        gatheringGate.Dispose();
        await inner.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask EnsureCandidateGatheringCompletedAsync(CancellationToken cancellationToken)
    {
        if (candidateGatheringCompleted)
        {
            return;
        }

        await gatheringGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (candidateGatheringCompleted)
            {
                return;
            }

            IPEndPoint providerLocalEndPoint = options.BindEndPoint;
            while (inner.TryReadCandidateEvent(out IceCandidateEvent innerEvent))
            {
                if (innerEvent.Kind == IceCandidateEventKind.LocalCandidateGatheringComplete)
                {
                    continue;
                }

                if (innerEvent.Kind == IceCandidateEventKind.LocalCandidateDiscovered &&
                    innerEvent.Candidate.EndPoint is not null)
                {
                    providerLocalEndPoint = innerEvent.Candidate.EndPoint;
                }

                EnqueueCandidateEvent(innerEvent);
            }

            await GatherServerReflexiveCandidatesAsync(providerLocalEndPoint, cancellationToken).ConfigureAwait(false);
            await AllocateRelayCandidatesAsync(providerLocalEndPoint, cancellationToken).ConfigureAwait(false);

            EnqueueCandidateEvent(new IceCandidateEvent
            {
                Kind = IceCandidateEventKind.LocalCandidateGatheringComplete
            });
            candidateGatheringCompleted = true;
        }
        finally
        {
            gatheringGate.Release();
        }
    }

    private async ValueTask GatherServerReflexiveCandidatesAsync(
        IPEndPoint localEndPoint,
        CancellationToken cancellationToken)
    {
        if (options.GatheringPolicy == IceGatheringPolicy.RelayOnly ||
            options.ServerReflexiveCandidateGatherer is null)
        {
            return;
        }

        for (int i = 0; i < options.IceServers.Length; i++)
        {
            IceServerOptions server = options.IceServers.Span[i];
            if (!IsStunServer(server.Uri))
            {
                continue;
            }

            IceCandidate? candidate = await options.ServerReflexiveCandidateGatherer
                .GatherAsync(server, localEndPoint, cancellationToken)
                .ConfigureAwait(false);

            if (candidate is not null)
            {
                EnqueueLocalCandidate(candidate.Value);
            }
        }
    }

    private async ValueTask AllocateRelayCandidatesAsync(
        IPEndPoint localEndPoint,
        CancellationToken cancellationToken)
    {
        if (options.GatheringPolicy == IceGatheringPolicy.HostOnly ||
            options.RelayCandidateAllocator is null)
        {
            return;
        }

        for (int i = 0; i < options.IceServers.Length; i++)
        {
            IceServerOptions server = options.IceServers.Span[i];
            if (!IsTurnServer(server.Uri))
            {
                continue;
            }

            IceCandidate? candidate = await options.RelayCandidateAllocator
                .AllocateAsync(server, localEndPoint, cancellationToken)
                .ConfigureAwait(false);

            if (candidate is not null)
            {
                EnqueueLocalCandidate(candidate.Value);
            }
        }
    }

    private void EnqueueLocalCandidate(in IceCandidate candidate)
    {
        EnqueueCandidateEvent(new IceCandidateEvent
        {
            Kind = IceCandidateEventKind.LocalCandidateDiscovered,
            Candidate = candidate,
            SignalingCandidate = new WebRtcIceCandidate
            {
                Candidate = FormatCandidate(candidate),
                SdpMid = candidate.SdpMid
            }
        });
    }

    private void EnqueueCandidateEvent(in IceCandidateEvent candidateEvent)
    {
        lock (gate)
        {
            candidateEvents.Enqueue(candidateEvent);
        }
    }

    private static bool IsStunServer(Uri uri)
    {
        return uri.Scheme.Equals("stun", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("stuns", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTurnServer(Uri uri)
    {
        return uri.Scheme.Equals("turn", StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals("turns", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCandidate(in IceCandidate candidate)
    {
        var writer = new ArrayBufferWriter<char>();
        _ = IceCandidateParser.TryWrite(candidate, writer);
        return new string(writer.WrittenSpan);
    }
}

internal sealed class WebRtcAsyncEventQueue<T>
    where T : struct
{
    private readonly Queue<T> events = new();
    private readonly Queue<AsyncEventWaiter> waiters = new();
    private readonly object gate = new();
    private bool completed;

    public void Enqueue(T value)
    {
        AsyncEventWaiter? waiter = null;
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            while (waiters.Count != 0)
            {
                waiter = waiters.Dequeue();
                if (waiter.TrySetResult(value))
                {
                    break;
                }

                waiter.Dispose();
                waiter = null;
            }

            if (waiter is null)
            {
                events.Enqueue(value);
                return;
            }
        }

        waiter.Dispose();
    }

    public bool TryRead(out T value)
    {
        lock (gate)
        {
            if (events.Count == 0)
            {
                value = default;
                return false;
            }

            value = events.Dequeue();
            return true;
        }
    }

    public ValueTask<T?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (events.Count != 0)
            {
                return new ValueTask<T?>((T?)events.Dequeue());
            }

            if (completed)
            {
                return new ValueTask<T?>((T?)null);
            }

            var waiter = new AsyncEventWaiter(cancellationToken);
            waiters.Enqueue(waiter);
            return new ValueTask<T?>(waiter.Task);
        }
    }

    public void Complete()
    {
        Queue<AsyncEventWaiter> drained;
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            drained = new Queue<AsyncEventWaiter>(waiters);
            waiters.Clear();
        }

        while (drained.Count != 0)
        {
            AsyncEventWaiter waiter = drained.Dequeue();
            _ = waiter.TrySetResult(null);
            waiter.Dispose();
        }
    }

    private sealed class AsyncEventWaiter : IDisposable
    {
        private readonly CancellationTokenRegistration cancellationRegistration;

        public AsyncEventWaiter(CancellationToken cancellationToken)
        {
            Source = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(
                    static state => ((AsyncEventWaiter)state!).Source.TrySetCanceled(),
                    this);
            }
        }

        public Task<T?> Task => Source.Task;

        private TaskCompletionSource<T?> Source { get; }

        public bool TrySetResult(T? value) => Source.TrySetResult(value);

        public void Dispose() => cancellationRegistration.Dispose();
    }
}
