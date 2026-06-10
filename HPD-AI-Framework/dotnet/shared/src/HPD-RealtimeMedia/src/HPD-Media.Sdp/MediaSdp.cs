#nullable enable

using System.Buffers;
using System.Globalization;

namespace HPD.Media.Sdp;

/// <summary>
/// Identifies an SDP media kind.
/// </summary>
public enum SdpMediaKind
{
    Unknown = 0,
    Audio = 1,
    Video = 2,
    Application = 3
}

/// <summary>
/// Classifies SDP parse and write results without requiring exceptions for expected parse failures.
/// </summary>
public enum SdpStatus
{
    Success = 0,
    InvalidSyntax = 1,
    UnsupportedVersion = 2,
    DestinationTooSmall = 3,
    UnsupportedAttribute = 4,
    MissingRequiredAttribute = 5,
    UnsupportedMediaProfile = 6
}

/// <summary>
/// Identifies SDP media direction.
/// </summary>
public enum SdpMediaDirection
{
    SendRecv = 0,
    SendOnly = 1,
    RecvOnly = 2,
    Inactive = 3
}

/// <summary>
/// Represents one generic SDP attribute.
/// </summary>
public readonly struct SdpAttribute
{
    /// <summary>Gets the attribute name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the attribute value, or null for valueless attributes.</summary>
    public string? Value { get; init; }
}

/// <summary>
/// Represents one SDP rtpmap payload binding.
/// </summary>
public readonly struct SdpRtpMap
{
    /// <summary>Gets the RTP payload type.</summary>
    public required byte PayloadType { get; init; }

    /// <summary>Gets the codec encoding name.</summary>
    public required string EncodingName { get; init; }

    /// <summary>Gets the RTP clock rate.</summary>
    public required int ClockRate { get; init; }

    /// <summary>Gets the channel count when present.</summary>
    public int? ChannelCount { get; init; }
}

/// <summary>
/// Represents one SDP fmtp attribute.
/// </summary>
public readonly struct SdpFmtp
{
    /// <summary>Gets the RTP payload type.</summary>
    public required byte PayloadType { get; init; }

    /// <summary>Gets the raw fmtp parameter text.</summary>
    public required string Parameters { get; init; }
}

/// <summary>
/// Represents one SDP RTCP feedback declaration.
/// </summary>
public readonly struct SdpRtcpFeedback
{
    /// <summary>Gets the RTP payload type, or null for wildcard feedback.</summary>
    public byte? PayloadType { get; init; }

    /// <summary>Gets the feedback type.</summary>
    public required string Type { get; init; }

    /// <summary>Gets optional feedback parameters.</summary>
    public string? Parameters { get; init; }
}

/// <summary>
/// Represents one SDP RTP header extension mapping.
/// </summary>
public readonly struct SdpExtMap
{
    /// <summary>Gets the extension identifier.</summary>
    public required int Id { get; init; }

    /// <summary>Gets the optional extension direction qualifier.</summary>
    public string? Direction { get; init; }

    /// <summary>Gets the extension URI.</summary>
    public required string Uri { get; init; }

    /// <summary>Gets optional extension attributes.</summary>
    public string? Attributes { get; init; }
}

/// <summary>
/// Represents one SDP certificate fingerprint.
/// </summary>
public readonly struct SdpFingerprint
{
    /// <summary>Gets the fingerprint hash algorithm.</summary>
    public required string Algorithm { get; init; }

    /// <summary>Gets the fingerprint bytes.</summary>
    public required ReadOnlyMemory<byte> Fingerprint { get; init; }
}

/// <summary>
/// Represents one SDP SSRC attribute.
/// </summary>
public readonly struct SdpSsrcAttribute
{
    /// <summary>Gets the RTP synchronization source identifier.</summary>
    public required uint Ssrc { get; init; }

    /// <summary>Gets the SSRC attribute name.</summary>
    public required string Attribute { get; init; }

    /// <summary>Gets the SSRC attribute value, or null for valueless attributes.</summary>
    public string? Value { get; init; }
}

/// <summary>
/// Represents one SDP MSID declaration.
/// </summary>
public readonly struct SdpMsid
{
    /// <summary>Gets the media stream identifier.</summary>
    public required string StreamId { get; init; }

    /// <summary>Gets the media track identifier when present.</summary>
    public string? TrackId { get; init; }
}

/// <summary>
/// Represents one parsed SDP media section.
/// </summary>
public readonly struct SdpMediaSection
{
    /// <summary>Gets the media kind.</summary>
    public required SdpMediaKind Kind { get; init; }

    /// <summary>Gets the media identifier when present.</summary>
    public string? Mid { get; init; }

    /// <summary>Gets the transport profile.</summary>
    public string? Protocol { get; init; }

    /// <summary>Gets the media port from the m-line.</summary>
    public int Port { get; init; }

    /// <summary>Gets the media direction.</summary>
    public SdpMediaDirection Direction { get; init; }

    /// <summary>Gets the payload types listed by the media section.</summary>
    public ReadOnlyMemory<byte> PayloadTypes { get; init; }

    /// <summary>Gets parsed RTP map bindings.</summary>
    public ReadOnlyMemory<SdpRtpMap> RtpMaps { get; init; }

    /// <summary>Gets parsed fmtp attributes.</summary>
    public ReadOnlyMemory<SdpFmtp> Fmtps { get; init; }

    /// <summary>Gets parsed RTCP feedback attributes.</summary>
    public ReadOnlyMemory<SdpRtcpFeedback> RtcpFeedback { get; init; }

    /// <summary>Gets parsed RTP header extension mappings.</summary>
    public ReadOnlyMemory<SdpExtMap> ExtMaps { get; init; }

    /// <summary>Gets media-level certificate fingerprints.</summary>
    public ReadOnlyMemory<SdpFingerprint> Fingerprints { get; init; }

    /// <summary>Gets media-level ICE username fragment when present.</summary>
    public string? IceUsernameFragment { get; init; }

    /// <summary>Gets media-level ICE password when present.</summary>
    public string? IcePassword { get; init; }

    /// <summary>Gets the DTLS setup role attribute when present.</summary>
    public string? Setup { get; init; }

    /// <summary>Gets a value indicating whether RTP and RTCP are multiplexed.</summary>
    public bool RtcpMux { get; init; }

    /// <summary>Gets a value indicating whether reduced-size RTCP is requested.</summary>
    public bool RtcpReducedSize { get; init; }

    /// <summary>Gets raw ICE candidate attribute values without the candidate prefix.</summary>
    public ReadOnlyMemory<string> IceCandidates { get; init; }

    /// <summary>Gets a value indicating whether end-of-candidates was signaled.</summary>
    public bool EndOfCandidates { get; init; }

    /// <summary>Gets parsed SSRC attributes.</summary>
    public ReadOnlyMemory<SdpSsrcAttribute> SsrcAttributes { get; init; }

    /// <summary>Gets parsed MSID declarations.</summary>
    public ReadOnlyMemory<SdpMsid> Msids { get; init; }

    /// <summary>Gets raw or extension attributes that are not modeled by strongly typed fields.</summary>
    public ReadOnlyMemory<SdpAttribute> Attributes { get; init; }
}

/// <summary>
/// Represents parsed SDP control-plane data.
/// </summary>
public readonly struct SdpSessionDescription
{
    /// <summary>Gets the SDP origin line value.</summary>
    public required string Origin { get; init; }

    /// <summary>Gets the SDP session name.</summary>
    public required string SessionName { get; init; }

    /// <summary>Gets BUNDLE group mids when present.</summary>
    public ReadOnlyMemory<string> BundleMids { get; init; }

    /// <summary>Gets session-level fingerprints.</summary>
    public ReadOnlyMemory<SdpFingerprint> Fingerprints { get; init; }

    /// <summary>Gets session-level ICE username fragment when present.</summary>
    public string? IceUsernameFragment { get; init; }

    /// <summary>Gets session-level ICE password when present.</summary>
    public string? IcePassword { get; init; }

    /// <summary>Gets parsed media sections.</summary>
    public ReadOnlyMemory<SdpMediaSection> MediaSections { get; init; }

    /// <summary>Gets raw or extension session attributes that are not modeled by strongly typed fields.</summary>
    public ReadOnlyMemory<SdpAttribute> Attributes { get; init; }
}

/// <summary>
/// Parses SDP text through an AOT-safe implementation.
/// </summary>
public interface ISdpParser
{
    /// <summary>Attempts to parse SDP text into a control-plane description.</summary>
    SdpStatus TryParse(ReadOnlySpan<char> sdp, out SdpSessionDescription description);
}

/// <summary>
/// Writes SDP control-plane descriptions without requiring reflection-driven serializers.
/// </summary>
public interface ISdpWriter
{
    /// <summary>Attempts to write SDP text into caller-provided storage.</summary>
    SdpStatus TryWrite(in SdpSessionDescription description, IBufferWriter<char> destination);
}

/// <summary>
/// Parses SDP text through a manual AOT-safe parser.
/// </summary>
public sealed class SdpParser : ISdpParser
{
    private const int MaxUdpPort = 65535;

    /// <inheritdoc />
    public SdpStatus TryParse(ReadOnlySpan<char> sdp, out SdpSessionDescription description)
    {
        description = default;
        if (sdp.IsEmpty)
        {
            return SdpStatus.InvalidSyntax;
        }

        string? origin = null;
        string? sessionName = null;
        bool sawVersion = false;
        bool sawTiming = false;
        string? sessionIceUfrag = null;
        string? sessionIcePwd = null;
        var bundleMids = new List<string>();
        var sessionFingerprints = new List<SdpFingerprint>();
        var sessionAttributes = new List<SdpAttribute>();
        var mediaSections = new List<MutableMediaSection>();
        MutableMediaSection? currentMedia = null;

        foreach (string rawLine in sdp.ToString().Split('\n'))
        {
            ReadOnlySpan<char> line = rawLine.AsSpan().TrimEnd('\r');
            if (line.IsEmpty)
            {
                continue;
            }

            if (line.Length < 2 || line[1] != '=')
            {
                return SdpStatus.InvalidSyntax;
            }

            char type = line[0];
            ReadOnlySpan<char> value = line[2..];
            switch (type)
            {
                case 'v':
                    if (!value.SequenceEqual("0"))
                    {
                        return SdpStatus.UnsupportedVersion;
                    }

                    sawVersion = true;
                    break;
                case 'o':
                    origin = value.ToString();
                    break;
                case 's':
                    sessionName = value.ToString();
                    break;
                case 't':
                    if (!TryParseTimingLine(value))
                    {
                        return SdpStatus.InvalidSyntax;
                    }

                    sawTiming = true;
                    break;
                case 'c':
                    AddAttribute(currentMedia, sessionAttributes, new SdpAttribute { Name = type.ToString(), Value = value.ToString() });
                    break;
                case 'm':
                    currentMedia = new MutableMediaSection();
                    if (!TryParseMediaLine(value, currentMedia))
                    {
                        return SdpStatus.InvalidSyntax;
                    }

                    mediaSections.Add(currentMedia);
                    break;
                case 'a':
                    SdpStatus status = ParseAttribute(
                        value,
                        currentMedia,
                        sessionAttributes,
                        bundleMids,
                        sessionFingerprints,
                        ref sessionIceUfrag,
                        ref sessionIcePwd);
                    if (status != SdpStatus.Success)
                    {
                        return status;
                    }

                    break;
                default:
                    AddAttribute(currentMedia, sessionAttributes, new SdpAttribute { Name = type.ToString(), Value = value.ToString() });
                    break;
            }
        }

        if (!sawVersion || origin is null || sessionName is null || !sawTiming)
        {
            return SdpStatus.MissingRequiredAttribute;
        }

        var parsedMedia = new SdpMediaSection[mediaSections.Count];
        for (int i = 0; i < mediaSections.Count; i++)
        {
            if (!ValidateMediaPayloadReferences(mediaSections[i]))
            {
                return SdpStatus.InvalidSyntax;
            }

            parsedMedia[i] = mediaSections[i].ToImmutable();
        }

        description = new SdpSessionDescription
        {
            Origin = origin,
            SessionName = sessionName,
            BundleMids = bundleMids.ToArray(),
            Fingerprints = sessionFingerprints.ToArray(),
            IceUsernameFragment = sessionIceUfrag,
            IcePassword = sessionIcePwd,
            MediaSections = parsedMedia,
            Attributes = sessionAttributes.ToArray()
        };
        return SdpStatus.Success;
    }

    private static bool ValidateMediaPayloadReferences(MutableMediaSection media)
    {
        if (media.PayloadTypes.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < media.PayloadTypes.Count; i++)
        {
            for (int j = i + 1; j < media.PayloadTypes.Count; j++)
            {
                if (media.PayloadTypes[i] == media.PayloadTypes[j])
                {
                    return false;
                }
            }
        }

        foreach (SdpRtpMap rtpMap in media.RtpMaps)
        {
            if (!media.PayloadTypes.Contains(rtpMap.PayloadType))
            {
                return false;
            }
        }

        for (int i = 0; i < media.RtpMaps.Count; i++)
        {
            for (int j = i + 1; j < media.RtpMaps.Count; j++)
            {
                if (media.RtpMaps[i].PayloadType == media.RtpMaps[j].PayloadType)
                {
                    return false;
                }
            }
        }

        foreach (SdpFmtp fmtp in media.Fmtps)
        {
            if (!media.PayloadTypes.Contains(fmtp.PayloadType))
            {
                return false;
            }
        }

        for (int i = 0; i < media.Fmtps.Count; i++)
        {
            for (int j = i + 1; j < media.Fmtps.Count; j++)
            {
                if (media.Fmtps[i].PayloadType == media.Fmtps[j].PayloadType)
                {
                    return false;
                }
            }
        }

        foreach (SdpRtcpFeedback feedback in media.RtcpFeedback)
        {
            if (feedback.PayloadType is { } payloadType && !media.PayloadTypes.Contains(payloadType))
            {
                return false;
            }
        }

        for (int i = 0; i < media.ExtMaps.Count; i++)
        {
            for (int j = i + 1; j < media.ExtMaps.Count; j++)
            {
                if (media.ExtMaps[i].Id == media.ExtMaps[j].Id)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryParseMediaLine(ReadOnlySpan<char> value, MutableMediaSection media)
    {
        string[] parts = value.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            return false;
        }

        media.Kind = parts[0] switch
        {
            "audio" => SdpMediaKind.Audio,
            "video" => SdpMediaKind.Video,
            "application" => SdpMediaKind.Application,
            _ => SdpMediaKind.Unknown
        };

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int port) ||
            port is < 0 or > MaxUdpPort)
        {
            return false;
        }

        media.Port = port;
        media.Protocol = parts[2];

        for (int i = 3; i < parts.Length; i++)
        {
            if (!TryParsePayloadType(parts[i], out byte payloadType))
            {
                return false;
            }

            media.PayloadTypes.Add(payloadType);
        }

        return true;
    }

    private static bool TryParseTimingLine(ReadOnlySpan<char> value)
    {
        string[] parts = value.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
            ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
            ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static SdpStatus ParseAttribute(
        ReadOnlySpan<char> value,
        MutableMediaSection? currentMedia,
        List<SdpAttribute> sessionAttributes,
        List<string> bundleMids,
        List<SdpFingerprint> sessionFingerprints,
        ref string? sessionIceUfrag,
        ref string? sessionIcePwd)
    {
        SplitAttribute(value, out ReadOnlySpan<char> name, out ReadOnlySpan<char> attributeValue);
        if (!IsValidAttributeName(name))
        {
            return SdpStatus.InvalidSyntax;
        }

        string nameText = name.ToString();

        switch (nameText)
        {
            case "group":
                if (attributeValue.StartsWith("BUNDLE ", StringComparison.Ordinal))
                {
                    int midsBefore = bundleMids.Count;
                    foreach (string mid in attributeValue[7..].ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        bundleMids.Add(mid);
                    }

                    if (bundleMids.Count == midsBefore)
                    {
                        return SdpStatus.InvalidSyntax;
                    }
                }
                else
                {
                    AddAttribute(currentMedia, sessionAttributes, new SdpAttribute { Name = nameText, Value = attributeValue.ToString() });
                }

                return SdpStatus.Success;
            case "mid":
                if (currentMedia is null)
                {
                    return SdpStatus.InvalidSyntax;
                }

                if (attributeValue.IsWhiteSpace())
                {
                    return SdpStatus.InvalidSyntax;
                }

                currentMedia.Mid = attributeValue.ToString();
                return SdpStatus.Success;
            case "sendrecv":
            case "sendonly":
            case "recvonly":
            case "inactive":
                if (currentMedia is null)
                {
                    return SdpStatus.InvalidSyntax;
                }

                currentMedia.Direction = nameText switch
                {
                    "sendonly" => SdpMediaDirection.SendOnly,
                    "recvonly" => SdpMediaDirection.RecvOnly,
                    "inactive" => SdpMediaDirection.Inactive,
                    _ => SdpMediaDirection.SendRecv
                };
                return SdpStatus.Success;
            case "rtpmap":
                return currentMedia is null || !TryParseRtpMap(attributeValue, out SdpRtpMap rtpMap)
                    ? SdpStatus.InvalidSyntax
                    : Add(currentMedia.RtpMaps, rtpMap);
            case "fmtp":
                return currentMedia is null || !TryParseFmtp(attributeValue, out SdpFmtp fmtp)
                    ? SdpStatus.InvalidSyntax
                    : Add(currentMedia.Fmtps, fmtp);
            case "rtcp-fb":
                return currentMedia is null || !TryParseRtcpFeedback(attributeValue, out SdpRtcpFeedback feedback)
                    ? SdpStatus.InvalidSyntax
                    : Add(currentMedia.RtcpFeedback, feedback);
            case "extmap":
                return currentMedia is null || !TryParseExtMap(attributeValue, out SdpExtMap extMap)
                    ? SdpStatus.InvalidSyntax
                    : Add(currentMedia.ExtMaps, extMap);
            case "fingerprint":
                return TryParseFingerprint(attributeValue, out SdpFingerprint fingerprint)
                    ? Add(currentMedia?.Fingerprints ?? sessionFingerprints, fingerprint)
                    : SdpStatus.InvalidSyntax;
            case "ice-ufrag":
                if (attributeValue.IsWhiteSpace())
                {
                    return SdpStatus.InvalidSyntax;
                }

                if (currentMedia is null)
                {
                    sessionIceUfrag = attributeValue.ToString();
                }
                else
                {
                    currentMedia.IceUsernameFragment = attributeValue.ToString();
                }

                return SdpStatus.Success;
            case "ice-pwd":
                if (attributeValue.IsWhiteSpace())
                {
                    return SdpStatus.InvalidSyntax;
                }

                if (currentMedia is null)
                {
                    sessionIcePwd = attributeValue.ToString();
                }
                else
                {
                    currentMedia.IcePassword = attributeValue.ToString();
                }

                return SdpStatus.Success;
            case "setup":
                if (currentMedia is null)
                {
                    AddAttribute(currentMedia, sessionAttributes, new SdpAttribute { Name = nameText, Value = attributeValue.ToString() });
                }
                else
                {
                    if (attributeValue.IsWhiteSpace() || !IsValidSetupValue(attributeValue))
                    {
                        return SdpStatus.InvalidSyntax;
                    }

                    currentMedia.Setup = attributeValue.ToString();
                }

                return SdpStatus.Success;
            case "rtcp-mux":
                if (currentMedia is null)
                {
                    return SdpStatus.InvalidSyntax;
                }

                currentMedia.RtcpMux = true;
                return SdpStatus.Success;
            case "rtcp-rsize":
                if (currentMedia is null)
                {
                    return SdpStatus.InvalidSyntax;
                }

                currentMedia.RtcpReducedSize = true;
                return SdpStatus.Success;
            case "candidate":
                if (currentMedia is null || attributeValue.IsWhiteSpace())
                {
                    return SdpStatus.InvalidSyntax;
                }

                currentMedia.IceCandidates.Add(attributeValue.ToString());
                return SdpStatus.Success;
            case "end-of-candidates":
                if (currentMedia is null)
                {
                    return SdpStatus.InvalidSyntax;
                }

                currentMedia.EndOfCandidates = true;
                return SdpStatus.Success;
            case "ssrc":
                return currentMedia is null || !TryParseSsrcAttribute(attributeValue, out SdpSsrcAttribute ssrc)
                    ? SdpStatus.InvalidSyntax
                    : Add(currentMedia.SsrcAttributes, ssrc);
            case "msid":
                return currentMedia is null || !TryParseMsid(attributeValue, out SdpMsid msid)
                    ? SdpStatus.InvalidSyntax
                    : Add(currentMedia.Msids, msid);
            default:
                AddAttribute(
                    currentMedia,
                    sessionAttributes,
                    new SdpAttribute { Name = nameText, Value = attributeValue.IsEmpty ? null : attributeValue.ToString() });
                return SdpStatus.Success;
        }
    }

    private static void SplitAttribute(ReadOnlySpan<char> value, out ReadOnlySpan<char> name, out ReadOnlySpan<char> attributeValue)
    {
        int separator = value.IndexOf(':');
        if (separator < 0)
        {
            name = value;
            attributeValue = ReadOnlySpan<char>.Empty;
            return;
        }

        name = value[..separator];
        attributeValue = value[(separator + 1)..];
    }

    private static bool TryParseRtpMap(ReadOnlySpan<char> value, out SdpRtpMap rtpMap)
    {
        rtpMap = default;
        int space = value.IndexOf(' ');
        if (space <= 0 || !TryParsePayloadType(value[..space], out byte payloadType))
        {
            return false;
        }

        ReadOnlySpan<char> encoding = value[(space + 1)..];
        int firstSlash = encoding.IndexOf('/');
        if (firstSlash <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> name = encoding[..firstSlash];
        if (name.IsWhiteSpace())
        {
            return false;
        }

        ReadOnlySpan<char> rateAndChannels = encoding[(firstSlash + 1)..];
        int secondSlash = rateAndChannels.IndexOf('/');
        ReadOnlySpan<char> rate = secondSlash < 0 ? rateAndChannels : rateAndChannels[..secondSlash];
        if (!int.TryParse(rate, NumberStyles.None, CultureInfo.InvariantCulture, out int clockRate) ||
            clockRate <= 0)
        {
            return false;
        }

        int? channelCount = null;
        if (secondSlash >= 0)
        {
            if (!int.TryParse(rateAndChannels[(secondSlash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedChannels) ||
                parsedChannels <= 0)
            {
                return false;
            }

            channelCount = parsedChannels;
        }

        rtpMap = new SdpRtpMap
        {
            PayloadType = payloadType,
            EncodingName = name.ToString(),
            ClockRate = clockRate,
            ChannelCount = channelCount
        };
        return true;
    }

    private static bool TryParseFmtp(ReadOnlySpan<char> value, out SdpFmtp fmtp)
    {
        fmtp = default;
        int space = value.IndexOf(' ');
        if (space <= 0 || !TryParsePayloadType(value[..space], out byte payloadType))
        {
            return false;
        }

        ReadOnlySpan<char> parameters = value[(space + 1)..];
        if (parameters.IsWhiteSpace())
        {
            return false;
        }

        fmtp = new SdpFmtp
        {
            PayloadType = payloadType,
            Parameters = parameters.ToString()
        };
        return true;
    }

    private static bool TryParseRtcpFeedback(ReadOnlySpan<char> value, out SdpRtcpFeedback feedback)
    {
        feedback = default;
        string[] parts = value.ToString().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        byte? payloadType = null;
        if (parts[0] != "*")
        {
            if (!TryParsePayloadType(parts[0], out byte parsedPayloadType))
            {
                return false;
            }

            payloadType = parsedPayloadType;
        }

        feedback = new SdpRtcpFeedback
        {
            PayloadType = payloadType,
            Type = parts[1],
            Parameters = parts.Length == 3 ? parts[2] : null
        };
        return true;
    }

    private static bool TryParseExtMap(ReadOnlySpan<char> value, out SdpExtMap extMap)
    {
        extMap = default;
        string[] parts = value.ToString().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        string idText = parts[0];
        string? direction = null;
        int slash = idText.IndexOf('/');
        if (slash >= 0)
        {
            direction = idText[(slash + 1)..];
            idText = idText[..slash];
        }

        if (!int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ||
            id <= 0 ||
            direction is { Length: 0 } ||
            (direction is not null && !IsValidExtMapDirection(direction)))
        {
            return false;
        }

        extMap = new SdpExtMap
        {
            Id = id,
            Direction = direction,
            Uri = parts[1],
            Attributes = parts.Length == 3 ? parts[2] : null
        };
        return true;
    }

    private static bool IsValidExtMapDirection(string direction)
    {
        return direction is "sendrecv" or "sendonly" or "recvonly" or "inactive";
    }

    private static bool TryParsePayloadType(ReadOnlySpan<char> value, out byte payloadType)
    {
        return byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out payloadType) &&
            IsValidPayloadType(payloadType);
    }

    private static bool IsValidPayloadType(byte payloadType)
    {
        return payloadType <= 127;
    }

    private static bool IsValidSetupValue(ReadOnlySpan<char> setup)
    {
        return setup.SequenceEqual("active") ||
            setup.SequenceEqual("passive") ||
            setup.SequenceEqual("actpass") ||
            setup.SequenceEqual("holdconn");
    }

    private static bool IsValidAttributeName(ReadOnlySpan<char> name)
    {
        if (name.IsWhiteSpace())
        {
            return false;
        }

        foreach (char value in name)
        {
            if (char.IsWhiteSpace(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseSsrcAttribute(ReadOnlySpan<char> value, out SdpSsrcAttribute ssrcAttribute)
    {
        ssrcAttribute = default;
        int space = value.IndexOf(' ');
        if (space <= 0 || !uint.TryParse(value[..space], NumberStyles.None, CultureInfo.InvariantCulture, out uint ssrc))
        {
            return false;
        }

        ReadOnlySpan<char> attribute = value[(space + 1)..];
        int separator = attribute.IndexOf(':');
        ReadOnlySpan<char> name = separator < 0 ? attribute : attribute[..separator];
        if (!IsValidAttributeName(name))
        {
            return false;
        }

        ReadOnlySpan<char> attributeValue = separator < 0 ? ReadOnlySpan<char>.Empty : attribute[(separator + 1)..];
        if (separator >= 0 && attributeValue.IsWhiteSpace())
        {
            return false;
        }

        ssrcAttribute = new SdpSsrcAttribute
        {
            Ssrc = ssrc,
            Attribute = name.ToString(),
            Value = separator < 0 ? null : attributeValue.ToString()
        };
        return true;
    }

    private static bool TryParseMsid(ReadOnlySpan<char> value, out SdpMsid msid)
    {
        msid = default;
        string[] parts = value.ToString().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        msid = new SdpMsid
        {
            StreamId = parts[0],
            TrackId = parts.Length == 2 ? parts[1] : null
        };
        return true;
    }

    private static bool TryParseFingerprint(ReadOnlySpan<char> value, out SdpFingerprint fingerprint)
    {
        fingerprint = default;
        int space = value.IndexOf(' ');
        if (space <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> bytes = value[(space + 1)..];
        if (bytes.IsEmpty)
        {
            return false;
        }

        int octets = (bytes.Length + 1) / 3;
        var fingerprintBytes = new byte[octets];
        int written = 0;
        for (int cursor = 0; cursor < bytes.Length;)
        {
            if (cursor + 2 > bytes.Length || !TryParseHexByte(bytes.Slice(cursor, 2), out fingerprintBytes[written++]))
            {
                return false;
            }

            cursor += 2;
            if (cursor < bytes.Length)
            {
                if (bytes[cursor] != ':')
                {
                    return false;
                }

                cursor++;
                if (cursor == bytes.Length)
                {
                    return false;
                }
            }
        }

        fingerprint = new SdpFingerprint
        {
            Algorithm = value[..space].ToString(),
            Fingerprint = fingerprintBytes
        };
        return true;
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> value, out byte result)
    {
        result = 0;
        if (value.Length != 2 || !TryParseHexNibble(value[0], out byte high) || !TryParseHexNibble(value[1], out byte low))
        {
            return false;
        }

        result = (byte)((high << 4) | low);
        return true;
    }

    private static bool TryParseHexNibble(char value, out byte result)
    {
        result = value switch
        {
            >= '0' and <= '9' => (byte)(value - '0'),
            >= 'a' and <= 'f' => (byte)(value - 'a' + 10),
            >= 'A' and <= 'F' => (byte)(value - 'A' + 10),
            _ => byte.MaxValue
        };
        return result != byte.MaxValue;
    }

    private static void AddAttribute(MutableMediaSection? currentMedia, List<SdpAttribute> sessionAttributes, SdpAttribute attribute)
    {
        if (currentMedia is null)
        {
            sessionAttributes.Add(attribute);
        }
        else
        {
            currentMedia.Attributes.Add(attribute);
        }
    }

    private static SdpStatus Add<T>(List<T> values, T value)
    {
        values.Add(value);
        return SdpStatus.Success;
    }

    private sealed class MutableMediaSection
    {
        public SdpMediaKind Kind { get; set; }

        public string? Mid { get; set; }

        public string? Protocol { get; set; }

        public int Port { get; set; }

        public SdpMediaDirection Direction { get; set; } = SdpMediaDirection.SendRecv;

        public List<byte> PayloadTypes { get; } = [];

        public List<SdpRtpMap> RtpMaps { get; } = [];

        public List<SdpFmtp> Fmtps { get; } = [];

        public List<SdpRtcpFeedback> RtcpFeedback { get; } = [];

        public List<SdpExtMap> ExtMaps { get; } = [];

        public List<SdpFingerprint> Fingerprints { get; } = [];

        public string? IceUsernameFragment { get; set; }

        public string? IcePassword { get; set; }

        public string? Setup { get; set; }

        public bool RtcpMux { get; set; }

        public bool RtcpReducedSize { get; set; }

        public List<string> IceCandidates { get; } = [];

        public bool EndOfCandidates { get; set; }

        public List<SdpSsrcAttribute> SsrcAttributes { get; } = [];

        public List<SdpMsid> Msids { get; } = [];

        public List<SdpAttribute> Attributes { get; } = [];

        public SdpMediaSection ToImmutable()
        {
            return new SdpMediaSection
            {
                Kind = Kind,
                Mid = Mid,
                Protocol = Protocol,
                Port = Port,
                Direction = Direction,
                PayloadTypes = PayloadTypes.ToArray(),
                RtpMaps = RtpMaps.ToArray(),
                Fmtps = Fmtps.ToArray(),
                RtcpFeedback = RtcpFeedback.ToArray(),
                ExtMaps = ExtMaps.ToArray(),
                Fingerprints = Fingerprints.ToArray(),
                IceUsernameFragment = IceUsernameFragment,
                IcePassword = IcePassword,
                Setup = Setup,
                RtcpMux = RtcpMux,
                RtcpReducedSize = RtcpReducedSize,
                IceCandidates = IceCandidates.ToArray(),
                EndOfCandidates = EndOfCandidates,
                SsrcAttributes = SsrcAttributes.ToArray(),
                Msids = Msids.ToArray(),
                Attributes = Attributes.ToArray()
            };
        }
    }

    private static string FormatFingerprint(ReadOnlySpan<byte> fingerprint)
    {
        const string Hex = "0123456789ABCDEF";
        if (fingerprint.IsEmpty)
        {
            return string.Empty;
        }

        return string.Create((fingerprint.Length * 3) - 1, fingerprint.ToArray(), static (destination, bytes) =>
        {
            int cursor = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    destination[cursor++] = ':';
                }

                destination[cursor++] = Hex[bytes[i] >> 4];
                destination[cursor++] = Hex[bytes[i] & 0x0F];
            }
        });
    }
}

/// <summary>
/// Writes SDP descriptions using a manual AOT-safe writer.
/// </summary>
public sealed class SdpWriter : ISdpWriter
{
    private const int MaxUdpPort = 65535;

    /// <inheritdoc />
    public SdpStatus TryWrite(in SdpSessionDescription description, IBufferWriter<char> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        SdpStatus validationStatus = ValidateDescription(description);
        if (validationStatus != SdpStatus.Success)
        {
            return validationStatus;
        }

        WriteLine(destination, "v=0");
        WriteLine(destination, "o=", description.Origin);
        WriteLine(destination, "s=", description.SessionName);
        WriteLine(destination, "t=0 0");

        if (!description.BundleMids.IsEmpty)
        {
            Write(destination, "a=group:BUNDLE");
            foreach (string mid in description.BundleMids.Span)
            {
                Write(destination, " ");
                Write(destination, mid);
            }

            WriteCrlf(destination);
        }

        foreach (SdpFingerprint fingerprint in description.Fingerprints.Span)
        {
            Write(destination, "a=fingerprint:");
            Write(destination, fingerprint.Algorithm);
            Write(destination, " ");
            WriteFingerprint(destination, fingerprint.Fingerprint.Span);
            WriteCrlf(destination);
        }

        if (description.IceUsernameFragment is not null)
        {
            WriteLine(destination, "a=ice-ufrag:", description.IceUsernameFragment);
        }

        if (description.IcePassword is not null)
        {
            WriteLine(destination, "a=ice-pwd:", description.IcePassword);
        }

        foreach (SdpAttribute attribute in description.Attributes.Span)
        {
            WriteAttribute(destination, attribute);
        }

        foreach (SdpMediaSection media in description.MediaSections.Span)
        {
            WriteMediaSection(destination, media);
        }

        return SdpStatus.Success;
    }

    private static SdpStatus ValidateDescription(in SdpSessionDescription description)
    {
        if (string.IsNullOrWhiteSpace(description.Origin) || string.IsNullOrWhiteSpace(description.SessionName))
        {
            return SdpStatus.MissingRequiredAttribute;
        }

        if (ContainsLineBreak(description.Origin) || ContainsLineBreak(description.SessionName))
        {
            return SdpStatus.InvalidSyntax;
        }

        foreach (SdpFingerprint fingerprint in description.Fingerprints.Span)
        {
            if (!IsValidFingerprint(fingerprint))
            {
                return SdpStatus.InvalidSyntax;
            }
        }

        foreach (string mid in description.BundleMids.Span)
        {
            if (!IsValidText(mid))
            {
                return SdpStatus.InvalidSyntax;
            }
        }

        if ((description.IceUsernameFragment is not null && !IsValidText(description.IceUsernameFragment)) ||
            (description.IcePassword is not null && !IsValidText(description.IcePassword)))
        {
            return SdpStatus.InvalidSyntax;
        }

        foreach (SdpAttribute attribute in description.Attributes.Span)
        {
            if (!IsValidAttribute(attribute))
            {
                return SdpStatus.InvalidSyntax;
            }
        }

        foreach (SdpMediaSection media in description.MediaSections.Span)
        {
            if (media.Port is < 0 or > MaxUdpPort ||
                media.PayloadTypes.IsEmpty ||
                (media.Protocol is not null && !IsValidText(media.Protocol)))
            {
                return SdpStatus.InvalidSyntax;
            }

            foreach (byte payloadType in media.PayloadTypes.Span)
            {
                if (!IsValidPayloadType(payloadType))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            if (!ValidateMediaPayloadReferences(media))
            {
                return SdpStatus.InvalidSyntax;
            }

            foreach (SdpFingerprint fingerprint in media.Fingerprints.Span)
            {
                if (!IsValidFingerprint(fingerprint))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            if (media.Mid is not null && !IsValidText(media.Mid))
            {
                return SdpStatus.InvalidSyntax;
            }

            foreach (SdpRtpMap rtpMap in media.RtpMaps.Span)
            {
                if (!IsValidRtpMap(rtpMap))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            foreach (SdpFmtp fmtp in media.Fmtps.Span)
            {
                if (!IsValidPayloadType(fmtp.PayloadType) || !IsValidText(fmtp.Parameters))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            foreach (SdpRtcpFeedback feedback in media.RtcpFeedback.Span)
            {
                if ((feedback.PayloadType is not null && !IsValidPayloadType(feedback.PayloadType.Value)) ||
                    !IsValidText(feedback.Type) ||
                    (feedback.Parameters is not null && !IsValidText(feedback.Parameters)))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            foreach (SdpExtMap extMap in media.ExtMaps.Span)
            {
                if (extMap.Id <= 0 ||
                    !IsValidText(extMap.Uri) ||
                    (extMap.Direction is not null && !IsValidExtMapDirection(extMap.Direction)) ||
                    (extMap.Attributes is not null && !IsValidText(extMap.Attributes)))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            foreach (string candidate in media.IceCandidates.Span)
            {
                if (!IsValidText(candidate))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            if ((media.IceUsernameFragment is not null && !IsValidText(media.IceUsernameFragment)) ||
                (media.IcePassword is not null && !IsValidText(media.IcePassword)) ||
                (media.Setup is not null && !IsValidSetupValue(media.Setup)))
            {
                return SdpStatus.InvalidSyntax;
            }

            foreach (SdpSsrcAttribute ssrc in media.SsrcAttributes.Span)
            {
                if (!IsValidAttributeName(ssrc.Attribute) ||
                    (ssrc.Value is not null && !IsValidText(ssrc.Value)))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            foreach (SdpMsid msid in media.Msids.Span)
            {
                if (!IsValidText(msid.StreamId) ||
                    (msid.TrackId is not null && !IsValidText(msid.TrackId)))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }

            foreach (SdpAttribute attribute in media.Attributes.Span)
            {
                if (!IsValidAttribute(attribute))
                {
                    return SdpStatus.InvalidSyntax;
                }
            }
        }

        return SdpStatus.Success;
    }

    private static bool IsValidFingerprint(in SdpFingerprint fingerprint)
    {
        return IsValidText(fingerprint.Algorithm) && !fingerprint.Fingerprint.IsEmpty;
    }

    private static bool IsValidRtpMap(in SdpRtpMap rtpMap)
    {
        return IsValidPayloadType(rtpMap.PayloadType) &&
            IsValidText(rtpMap.EncodingName) &&
            rtpMap.ClockRate > 0 &&
            rtpMap.ChannelCount is null or > 0;
    }

    private static bool IsValidPayloadType(byte payloadType)
    {
        return payloadType <= 127;
    }

    private static bool ValidateMediaPayloadReferences(in SdpMediaSection media)
    {
        ReadOnlySpan<byte> payloadTypes = media.PayloadTypes.Span;
        if (payloadTypes.IsEmpty)
        {
            return false;
        }

        for (int i = 0; i < payloadTypes.Length; i++)
        {
            for (int j = i + 1; j < payloadTypes.Length; j++)
            {
                if (payloadTypes[i] == payloadTypes[j])
                {
                    return false;
                }
            }
        }

        foreach (SdpRtpMap rtpMap in media.RtpMaps.Span)
        {
            if (!PayloadTypeIsListed(payloadTypes, rtpMap.PayloadType))
            {
                return false;
            }
        }

        ReadOnlySpan<SdpRtpMap> rtpMaps = media.RtpMaps.Span;
        for (int i = 0; i < rtpMaps.Length; i++)
        {
            for (int j = i + 1; j < rtpMaps.Length; j++)
            {
                if (rtpMaps[i].PayloadType == rtpMaps[j].PayloadType)
                {
                    return false;
                }
            }
        }

        foreach (SdpFmtp fmtp in media.Fmtps.Span)
        {
            if (!PayloadTypeIsListed(payloadTypes, fmtp.PayloadType))
            {
                return false;
            }
        }

        ReadOnlySpan<SdpFmtp> fmtps = media.Fmtps.Span;
        for (int i = 0; i < fmtps.Length; i++)
        {
            for (int j = i + 1; j < fmtps.Length; j++)
            {
                if (fmtps[i].PayloadType == fmtps[j].PayloadType)
                {
                    return false;
                }
            }
        }

        foreach (SdpRtcpFeedback feedback in media.RtcpFeedback.Span)
        {
            if (feedback.PayloadType is { } payloadType && !PayloadTypeIsListed(payloadTypes, payloadType))
            {
                return false;
            }
        }

        ReadOnlySpan<SdpExtMap> extMaps = media.ExtMaps.Span;
        for (int i = 0; i < extMaps.Length; i++)
        {
            for (int j = i + 1; j < extMaps.Length; j++)
            {
                if (extMaps[i].Id == extMaps[j].Id)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool PayloadTypeIsListed(ReadOnlySpan<byte> payloadTypes, byte payloadType)
    {
        foreach (byte listedPayloadType in payloadTypes)
        {
            if (listedPayloadType == payloadType)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidExtMapDirection(string direction)
    {
        return direction is "sendrecv" or "sendonly" or "recvonly" or "inactive";
    }

    private static bool IsValidSetupValue(string setup)
    {
        return setup is "active" or "passive" or "actpass" or "holdconn";
    }

    private static bool IsValidAttributeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (char value in name)
        {
            if (char.IsWhiteSpace(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidAttribute(in SdpAttribute attribute)
    {
        return IsValidAttributeName(attribute.Name) &&
            (attribute.Value is null || !ContainsLineBreak(attribute.Value));
    }

    private static bool IsValidText(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && !ContainsLineBreak(value);
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.AsSpan().ContainsAny('\r', '\n');
    }

    private static void WriteMediaSection(IBufferWriter<char> destination, in SdpMediaSection media)
    {
        Write(destination, "m=");
        Write(destination, media.Kind switch
        {
            SdpMediaKind.Audio => "audio",
            SdpMediaKind.Video => "video",
            SdpMediaKind.Application => "application",
            _ => "unknown"
        });
        Write(destination, " ");
        Write(destination, media.Port.ToString(CultureInfo.InvariantCulture));
        Write(destination, " ");
        Write(destination, media.Protocol ?? "UDP/TLS/RTP/SAVPF");
        foreach (byte payloadType in media.PayloadTypes.Span)
        {
            Write(destination, " ");
            Write(destination, payloadType.ToString(CultureInfo.InvariantCulture));
        }

        WriteCrlf(destination);
        if (media.Mid is not null)
        {
            WriteLine(destination, "a=mid:", media.Mid);
        }

        WriteLine(destination, media.Direction switch
        {
            SdpMediaDirection.SendOnly => "a=sendonly",
            SdpMediaDirection.RecvOnly => "a=recvonly",
            SdpMediaDirection.Inactive => "a=inactive",
            _ => "a=sendrecv"
        });

        foreach (SdpRtpMap rtpMap in media.RtpMaps.Span)
        {
            Write(destination, "a=rtpmap:");
            Write(destination, rtpMap.PayloadType.ToString(CultureInfo.InvariantCulture));
            Write(destination, " ");
            Write(destination, rtpMap.EncodingName);
            Write(destination, "/");
            Write(destination, rtpMap.ClockRate.ToString(CultureInfo.InvariantCulture));
            if (rtpMap.ChannelCount is not null)
            {
                Write(destination, "/");
                Write(destination, rtpMap.ChannelCount.Value.ToString(CultureInfo.InvariantCulture));
            }

            WriteCrlf(destination);
        }

        foreach (SdpFmtp fmtp in media.Fmtps.Span)
        {
            Write(destination, "a=fmtp:");
            Write(destination, fmtp.PayloadType.ToString(CultureInfo.InvariantCulture));
            Write(destination, " ");
            WriteLine(destination, fmtp.Parameters);
        }

        foreach (SdpRtcpFeedback feedback in media.RtcpFeedback.Span)
        {
            Write(destination, "a=rtcp-fb:");
            Write(destination, feedback.PayloadType?.ToString(CultureInfo.InvariantCulture) ?? "*");
            Write(destination, " ");
            Write(destination, feedback.Type);
            if (feedback.Parameters is not null)
            {
                Write(destination, " ");
                Write(destination, feedback.Parameters);
            }

            WriteCrlf(destination);
        }

        foreach (SdpExtMap extMap in media.ExtMaps.Span)
        {
            Write(destination, "a=extmap:");
            Write(destination, extMap.Id.ToString(CultureInfo.InvariantCulture));
            if (extMap.Direction is not null)
            {
                Write(destination, "/");
                Write(destination, extMap.Direction);
            }

            Write(destination, " ");
            Write(destination, extMap.Uri);
            if (extMap.Attributes is not null)
            {
                Write(destination, " ");
                Write(destination, extMap.Attributes);
            }

            WriteCrlf(destination);
        }

        foreach (SdpFingerprint fingerprint in media.Fingerprints.Span)
        {
            Write(destination, "a=fingerprint:");
            Write(destination, fingerprint.Algorithm);
            Write(destination, " ");
            WriteFingerprint(destination, fingerprint.Fingerprint.Span);
            WriteCrlf(destination);
        }

        if (media.IceUsernameFragment is not null)
        {
            WriteLine(destination, "a=ice-ufrag:", media.IceUsernameFragment);
        }

        if (media.IcePassword is not null)
        {
            WriteLine(destination, "a=ice-pwd:", media.IcePassword);
        }

        if (media.Setup is not null)
        {
            WriteLine(destination, "a=setup:", media.Setup);
        }

        if (media.RtcpMux)
        {
            WriteLine(destination, "a=rtcp-mux");
        }

        if (media.RtcpReducedSize)
        {
            WriteLine(destination, "a=rtcp-rsize");
        }

        foreach (string candidate in media.IceCandidates.Span)
        {
            WriteLine(destination, "a=candidate:", candidate);
        }

        if (media.EndOfCandidates)
        {
            WriteLine(destination, "a=end-of-candidates");
        }

        foreach (SdpSsrcAttribute ssrc in media.SsrcAttributes.Span)
        {
            Write(destination, "a=ssrc:");
            Write(destination, ssrc.Ssrc.ToString(CultureInfo.InvariantCulture));
            Write(destination, " ");
            Write(destination, ssrc.Attribute);
            if (ssrc.Value is not null)
            {
                Write(destination, ":");
                Write(destination, ssrc.Value);
            }

            WriteCrlf(destination);
        }

        foreach (SdpMsid msid in media.Msids.Span)
        {
            Write(destination, "a=msid:");
            Write(destination, msid.StreamId);
            if (msid.TrackId is not null)
            {
                Write(destination, " ");
                Write(destination, msid.TrackId);
            }

            WriteCrlf(destination);
        }

        foreach (SdpAttribute attribute in media.Attributes.Span)
        {
            WriteAttribute(destination, attribute);
        }
    }

    private static void WriteAttribute(IBufferWriter<char> destination, in SdpAttribute attribute)
    {
        if (attribute.Name.Length == 1 && attribute.Name[0] is not 'a')
        {
            Write(destination, attribute.Name);
            Write(destination, "=");
            WriteLine(destination, attribute.Value ?? string.Empty);
            return;
        }

        Write(destination, "a=");
        Write(destination, attribute.Name);
        if (attribute.Value is not null)
        {
            Write(destination, ":");
            Write(destination, attribute.Value);
        }

        WriteCrlf(destination);
    }

    private static void WriteLine(IBufferWriter<char> destination, string value)
    {
        Write(destination, value);
        WriteCrlf(destination);
    }

    private static void WriteLine(IBufferWriter<char> destination, string prefix, string value)
    {
        Write(destination, prefix);
        Write(destination, value);
        WriteCrlf(destination);
    }

    private static void Write(IBufferWriter<char> destination, string value)
    {
        Span<char> span = destination.GetSpan(value.Length);
        value.AsSpan().CopyTo(span);
        destination.Advance(value.Length);
    }

    private static void WriteCrlf(IBufferWriter<char> destination)
    {
        Span<char> span = destination.GetSpan(2);
        span[0] = '\r';
        span[1] = '\n';
        destination.Advance(2);
    }

    private static void WriteFingerprint(IBufferWriter<char> destination, ReadOnlySpan<byte> fingerprint)
    {
        const string Hex = "0123456789ABCDEF";
        for (int i = 0; i < fingerprint.Length; i++)
        {
            if (i > 0)
            {
                Write(destination, ":");
            }

            Span<char> span = destination.GetSpan(2);
            span[0] = Hex[fingerprint[i] >> 4];
            span[1] = Hex[fingerprint[i] & 0x0F];
            destination.Advance(2);
        }
    }
}
