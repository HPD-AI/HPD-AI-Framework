#nullable enable

using System.Globalization;
using HPD.Audio.Codecs;
using HPD.Media.Rtp.Audio;
using HPD.Media.Sdp;

namespace HPD.Media.Rtp.Audio.Sdp;

/// <summary>
/// Builds versioned RTP audio payload maps from parsed SDP media sections.
/// </summary>
public static class SdpRtpAudioFormatMapBuilder
{
    /// <summary>
    /// Attempts to build an RTP audio format map from one parsed SDP audio media section.
    /// </summary>
    public static bool TryBuild(
        in SdpMediaSection media,
        ulong version,
        out RtpAudioFormatMap formatMap)
    {
        formatMap = null!;
        if (media.Kind != SdpMediaKind.Audio)
        {
            return false;
        }

        var bindings = new List<RtpAudioFormatBinding>(media.RtpMaps.Length);
        foreach (SdpRtpMap rtpMap in media.RtpMaps.Span)
        {
            if (!IsUsableRtpMap(rtpMap))
            {
                continue;
            }

            if (!IsListedPayloadType(media.PayloadTypes.Span, rtpMap.PayloadType))
            {
                continue;
            }

            if (!TryMapEncoding(rtpMap.EncodingName, out AudioEncoding encoding))
            {
                continue;
            }

            SdpFmtp? fmtp = FindFmtp(media.Fmtps.Span, rtpMap.PayloadType);
            var parameters = SdpEncodedAudioFormatParameters.Parse(fmtp?.Parameters, media.Attributes.Span);
            int channelCount = rtpMap.ChannelCount ?? GetDefaultChannelCount(encoding);
            int sampleRate = GetSampleRate(encoding, rtpMap.ClockRate);
            TimeSpan? packetTime = TryGetPacketTime(media, parameters, out TimeSpan parsedPacketTime)
                ? parsedPacketTime
                : null;

            if (ContainsPayloadType(bindings, rtpMap.PayloadType))
            {
                formatMap = null!;
                return false;
            }

            bindings.Add(new RtpAudioFormatBinding
            {
                PayloadType = rtpMap.PayloadType,
                EncodedFormat = new EncodedAudioFormat
                {
                    Encoding = encoding,
                    SampleRate = sampleRate,
                    ChannelCount = channelCount,
                    RtpClockRate = rtpMap.ClockRate,
                    Parameters = parameters.IsEmpty ? null : parameters
                },
                DefaultPacketTime = packetTime
            });
        }

        if (bindings.Count == 0)
        {
            return false;
        }

        formatMap = new RtpAudioFormatMap(version, CollectionsMarshalAsSpan(bindings));
        return true;
    }

    private static bool TryMapEncoding(string encodingName, out AudioEncoding encoding)
    {
        encoding = encodingName.ToUpperInvariant() switch
        {
            "OPUS" => AudioEncoding.Opus,
            "PCMU" => AudioEncoding.Pcmu,
            "PCMA" => AudioEncoding.Pcma,
            "G722" => AudioEncoding.G722,
            "L16" => AudioEncoding.Pcm16,
            _ => (AudioEncoding)(-1)
        };

        return encoding != (AudioEncoding)(-1);
    }

    private static bool IsUsableRtpMap(in SdpRtpMap rtpMap)
    {
        return rtpMap.PayloadType <= 127 &&
            rtpMap.ClockRate > 0 &&
            rtpMap.ChannelCount is null or > 0;
    }

    private static bool IsListedPayloadType(ReadOnlySpan<byte> payloadTypes, byte payloadType)
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

    private static bool ContainsPayloadType(List<RtpAudioFormatBinding> bindings, byte payloadType)
    {
        foreach (RtpAudioFormatBinding binding in bindings)
        {
            if (binding.PayloadType == payloadType)
            {
                return true;
            }
        }

        return false;
    }

    private static int GetDefaultChannelCount(AudioEncoding encoding)
    {
        return encoding == AudioEncoding.Opus ? 2 : 1;
    }

    private static int GetSampleRate(AudioEncoding encoding, int clockRate)
    {
        return encoding == AudioEncoding.G722 ? 16000 : clockRate;
    }

    private static SdpFmtp? FindFmtp(ReadOnlySpan<SdpFmtp> fmtps, byte payloadType)
    {
        foreach (SdpFmtp fmtp in fmtps)
        {
            if (fmtp.PayloadType == payloadType)
            {
                return fmtp;
            }
        }

        return null;
    }

    private static bool TryGetPacketTime(
        in SdpMediaSection media,
        SdpEncodedAudioFormatParameters parameters,
        out TimeSpan packetTime)
    {
        foreach (SdpAttribute attribute in media.Attributes.Span)
        {
            if (attribute.Name.Equals("ptime", StringComparison.OrdinalIgnoreCase) &&
                TryParseMilliseconds(attribute.Value, out packetTime))
            {
                return true;
            }
        }

        if (parameters.TryGet(EncodedAudioParameter.PacketTimeMilliseconds, out EncodedAudioFormatParameter value) &&
            value.Int32Value > 0)
        {
            packetTime = TimeSpan.FromMilliseconds(value.Int32Value);
            return true;
        }

        packetTime = default;
        return false;
    }

    private static bool TryParseMilliseconds(string? value, out TimeSpan result)
    {
        result = default;
        if (value is null ||
            !double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double milliseconds) ||
            !double.IsFinite(milliseconds) ||
            milliseconds <= 0 ||
            milliseconds > TimeSpan.MaxValue.TotalMilliseconds)
        {
            return false;
        }

        result = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    private static ReadOnlySpan<RtpAudioFormatBinding> CollectionsMarshalAsSpan(List<RtpAudioFormatBinding> bindings)
    {
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bindings);
    }
}

/// <summary>
/// Encoded audio format parameters parsed from SDP fmtp text.
/// </summary>
public sealed class SdpEncodedAudioFormatParameters : IEncodedAudioFormatParameters
{
    private readonly EncodedAudioFormatParameter[] parameters;

    private SdpEncodedAudioFormatParameters(EncodedAudioFormatParameter[] parameters)
    {
        this.parameters = parameters;
    }

    /// <summary>Gets a value indicating whether this set has no parameters.</summary>
    public bool IsEmpty => parameters.Length == 0;

    /// <summary>Parses SDP fmtp parameter text into typed audio parameters.</summary>
    public static SdpEncodedAudioFormatParameters Parse(string? fmtp)
    {
        return Parse(fmtp, ReadOnlySpan<SdpAttribute>.Empty);
    }

    /// <summary>Parses SDP fmtp parameter text and media-level timing attributes into typed audio parameters.</summary>
    public static SdpEncodedAudioFormatParameters Parse(string? fmtp, ReadOnlySpan<SdpAttribute> attributes)
    {
        if (string.IsNullOrWhiteSpace(fmtp))
        {
            if (attributes.IsEmpty)
            {
                return new SdpEncodedAudioFormatParameters([]);
            }
        }

        var parsed = new List<EncodedAudioFormatParameter>();
        if (!string.IsNullOrWhiteSpace(fmtp))
        {
            foreach (string segment in fmtp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int separator = segment.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string name = segment[..separator].Trim();
                string value = segment[(separator + 1)..].Trim();
                if (TryParseParameter(name, value, out EncodedAudioFormatParameter parameter))
                {
                    Upsert(parsed, parameter);
                }
            }
        }

        foreach (SdpAttribute attribute in attributes)
        {
            if (attribute.Value is null)
            {
                continue;
            }

            if (attribute.Name.Equals("ptime", StringComparison.OrdinalIgnoreCase) &&
                TryParseInt32(EncodedAudioParameter.PacketTimeMilliseconds, attribute.Value, out EncodedAudioFormatParameter packetTime))
            {
                Upsert(parsed, packetTime);
            }
            else if (attribute.Name.Equals("maxptime", StringComparison.OrdinalIgnoreCase) &&
                TryParseInt32(EncodedAudioParameter.MaxPacketTimeMilliseconds, attribute.Value, out EncodedAudioFormatParameter maxPacketTime))
            {
                Upsert(parsed, maxPacketTime);
            }
        }

        return new SdpEncodedAudioFormatParameters(parsed.ToArray());
    }

    /// <inheritdoc />
    public bool TryGet(EncodedAudioParameter parameter, out EncodedAudioFormatParameter value)
    {
        foreach (EncodedAudioFormatParameter candidate in parameters)
        {
            if (candidate.Parameter == parameter)
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseParameter(
        string name,
        string value,
        out EncodedAudioFormatParameter parameter)
    {
        parameter = default;
        return name.ToLowerInvariant() switch
        {
            "useinbandfec" => TryParseBoolean(
                EncodedAudioParameter.OpusUseInBandFec,
                value,
                out parameter),
            "usedtx" => TryParseBoolean(
                EncodedAudioParameter.OpusDtx,
                value,
                out parameter),
            "maxplaybackrate" => TryParsePositiveInt32(
                EncodedAudioParameter.OpusMaxPlaybackRate,
                value,
                out parameter),
            "stereo" => TryParseBoolean(
                EncodedAudioParameter.OpusStereo,
                value,
                out parameter),
            "minptime" or "ptime" => TryParsePositiveInt32(
                EncodedAudioParameter.PacketTimeMilliseconds,
                value,
                out parameter),
            "maxptime" => TryParsePositiveInt32(
                EncodedAudioParameter.MaxPacketTimeMilliseconds,
                value,
                out parameter),
            _ => false
        };
    }

    private static void Upsert(List<EncodedAudioFormatParameter> parameters, EncodedAudioFormatParameter parameter)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Parameter == parameter.Parameter)
            {
                parameters[i] = parameter;
                return;
            }
        }

        parameters.Add(parameter);
    }

    private static bool TryParseBoolean(
        EncodedAudioParameter parameterKind,
        string value,
        out EncodedAudioFormatParameter parameter)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int intValue))
        {
            parameter = default;
            return false;
        }

        if (intValue is not 0 and not 1)
        {
            parameter = default;
            return false;
        }

        parameter = new EncodedAudioFormatParameter
        {
            Parameter = parameterKind,
            Int32Value = intValue,
            BooleanValue = intValue != 0
        };
        return true;
    }

    private static bool TryParseInt32(
        EncodedAudioParameter parameterKind,
        string value,
        out EncodedAudioFormatParameter parameter)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int intValue))
        {
            parameter = default;
            return false;
        }

        parameter = new EncodedAudioFormatParameter
        {
            Parameter = parameterKind,
            Int32Value = intValue,
            BooleanValue = intValue != 0
        };
        return true;
    }

    private static bool TryParsePositiveInt32(
        EncodedAudioParameter parameterKind,
        string value,
        out EncodedAudioFormatParameter parameter)
    {
        if (!TryParseInt32(parameterKind, value, out parameter) || parameter.Int32Value <= 0)
        {
            parameter = default;
            return false;
        }

        return true;
    }
}
