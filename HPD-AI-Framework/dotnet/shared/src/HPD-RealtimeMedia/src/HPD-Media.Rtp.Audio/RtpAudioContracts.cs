#nullable enable

using HPD.Audio.Codecs;
using HPD.Media.Rtp;

namespace HPD.Media.Rtp.Audio;

/// <summary>
/// Binds an RTP payload type to an encoded audio format.
/// </summary>
public readonly struct RtpAudioFormatBinding
{
    /// <summary>Gets the RTP payload type.</summary>
    public required byte PayloadType { get; init; }

    /// <summary>Gets the encoded audio format for the payload type.</summary>
    public required EncodedAudioFormat EncodedFormat { get; init; }

    /// <summary>Gets the default packet duration when timestamp deltas are not yet available.</summary>
    public TimeSpan? DefaultPacketTime { get; init; }
}

/// <summary>
/// Resolves RTP audio payload types through a versioned map that can be replaced during renegotiation.
/// </summary>
public interface IRtpAudioFormatMap
{
    /// <summary>Gets the version of this payload-type map.</summary>
    ulong Version { get; }

    /// <summary>Attempts to resolve an RTP payload type to an encoded audio format binding.</summary>
    bool TryGetFormat(byte payloadType, out RtpAudioFormatBinding binding);
}

/// <summary>
/// Represents an RTP audio access-unit or loss event.
/// </summary>
public readonly struct RtpAudioAccessUnitEvent
{
    /// <summary>Gets a value indicating whether this event represents packet loss.</summary>
    public required bool IsLoss { get; init; }

    /// <summary>Gets the encoded frame when this event carries audio bytes.</summary>
    public EncodedAudioFrame Frame { get; init; }

    /// <summary>Gets the duration represented by the event.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets the associated RTP timestamp when known.</summary>
    public uint? RtpTimestamp { get; init; }

    /// <summary>Gets the associated RTP sequence number when known.</summary>
    public ushort? RtpSequenceNumber { get; init; }
}

/// <summary>
/// Receives RTP audio events without requiring collection allocation.
/// </summary>
public interface IRtpAudioAccessUnitSink
{
    /// <summary>Attempts to accept one RTP audio access-unit event.</summary>
    bool TryWrite(in RtpAudioAccessUnitEvent accessUnitEvent);
}

/// <summary>
/// Receives RTP packets without requiring collection allocation.
/// </summary>
public interface IRtpPacketSink
{
    /// <summary>Attempts to accept one RTP packet.</summary>
    bool TryWrite(in RtpPacket packet);
}

/// <summary>
/// Classifies RTP audio packetize/depacketize results without exceptions for normal media flow.
/// </summary>
public enum RtpAudioStatus
{
    Success = 0,
    UnknownPayloadType = 1,
    InvalidPacket = 2,
    SinkBackpressure = 3,
    UnsupportedFormat = 4,
    FormatMapChanged = 5
}

/// <summary>
/// Converts ordered RTP packet events into encoded audio access-unit events.
/// </summary>
public interface IRtpAudioDepacketizer
{
    /// <summary>Pushes one RTP packet or loss event and writes zero or more audio events to <paramref name="sink"/>.</summary>
    RtpAudioStatus Push(in RtpPacketEvent packetEvent, IRtpAudioAccessUnitSink sink);
}

/// <summary>
/// Converts encoded audio access units into RTP packets.
/// </summary>
public interface IRtpAudioPacketizer
{
    /// <summary>Packetizes one encoded audio frame for the supplied SSRC and payload type.</summary>
    RtpAudioStatus Packetize(in EncodedAudioFrame frame, uint ssrc, byte payloadType, IRtpPacketSink sink);
}

/// <summary>
/// Immutable RTP audio payload-type map backed by construction-time storage.
/// </summary>
public sealed class RtpAudioFormatMap : IRtpAudioFormatMap
{
    private readonly RtpAudioFormatBinding[] bindings;

    /// <summary>
    /// Initializes a new instance of the <see cref="RtpAudioFormatMap"/> class.
    /// </summary>
    public RtpAudioFormatMap(ulong version, ReadOnlySpan<RtpAudioFormatBinding> bindings)
    {
        Version = version;
        for (int i = 0; i < bindings.Length; i++)
        {
            RtpAudioFormatBinding binding = bindings[i];
            if (binding.PayloadType > 127)
            {
                throw new ArgumentOutOfRangeException(nameof(bindings), "RTP audio payload types must be in the RTP payload type range 0..127.");
            }

            if (!IsFormatUsable(binding.EncodedFormat))
            {
                throw new ArgumentException("RTP audio format bindings must use a positive sample rate, channel count, and RTP clock rate.", nameof(bindings));
            }

            if (binding.DefaultPacketTime is { } defaultPacketTime && defaultPacketTime <= TimeSpan.Zero)
            {
                throw new ArgumentException("Default RTP audio packet time must be positive when supplied.", nameof(bindings));
            }

            for (int j = 0; j < i; j++)
            {
                if (bindings[j].PayloadType == binding.PayloadType)
                {
                    throw new ArgumentException("RTP audio payload type bindings must not contain duplicate payload types.", nameof(bindings));
                }
            }
        }

        this.bindings = bindings.ToArray();
    }

    /// <inheritdoc />
    public ulong Version { get; }

    /// <inheritdoc />
    public bool TryGetFormat(byte payloadType, out RtpAudioFormatBinding binding)
    {
        foreach (RtpAudioFormatBinding candidate in bindings)
        {
            if (candidate.PayloadType == payloadType)
            {
                binding = candidate;
                return true;
            }
        }

        binding = default;
        return false;
    }

    private static bool IsFormatUsable(in EncodedAudioFormat format)
    {
        return format.SampleRate > 0 &&
            format.ChannelCount > 0 &&
            (format.RtpClockRate ?? format.SampleRate) > 0;
    }
}

/// <summary>
/// Converts ordered RTP packet events into encoded audio access units.
/// </summary>
public sealed class RtpAudioDepacketizer : IRtpAudioDepacketizer
{
    private IRtpAudioFormatMap formatMap;
    private bool hasPreviousPacket;
    private uint previousSsrc;
    private uint previousTimestamp;
    private RtpAudioFormatBinding previousBinding;
    private TimeSpan previousDuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="RtpAudioDepacketizer"/> class.
    /// </summary>
    public RtpAudioDepacketizer(IRtpAudioFormatMap formatMap)
    {
        ArgumentNullException.ThrowIfNull(formatMap);
        this.formatMap = formatMap;
    }

    /// <summary>
    /// Replaces the RTP audio payload-type map at a documented packet boundary.
    /// </summary>
    public void UpdateFormatMap(IRtpAudioFormatMap formatMap)
    {
        ArgumentNullException.ThrowIfNull(formatMap);
        this.formatMap = formatMap;
        hasPreviousPacket = false;
        previousSsrc = 0;
        previousTimestamp = 0;
        previousBinding = default;
        previousDuration = TimeSpan.Zero;
    }

    /// <inheritdoc />
    public RtpAudioStatus Push(in RtpPacketEvent packetEvent, IRtpAudioAccessUnitSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (packetEvent.Kind == RtpPacketEventKind.Loss)
        {
            if (packetEvent.LostPacketCount <= 0 ||
                !hasPreviousPacket ||
                packetEvent.Ssrc != previousSsrc)
            {
                return RtpAudioStatus.InvalidPacket;
            }

            TimeSpan lossDuration = previousDuration;
            if (lossDuration == TimeSpan.Zero && previousBinding.DefaultPacketTime is { } defaultPacketTime)
            {
                lossDuration = defaultPacketTime;
            }

            if (lossDuration == TimeSpan.Zero)
            {
                return RtpAudioStatus.InvalidPacket;
            }

            if (packetEvent.LostPacketCount > 1)
            {
                if (lossDuration.Ticks > TimeSpan.MaxValue.Ticks / packetEvent.LostPacketCount)
                {
                    return RtpAudioStatus.InvalidPacket;
                }

                lossDuration = TimeSpan.FromTicks(lossDuration.Ticks * packetEvent.LostPacketCount);
            }

            var lossEvent = new RtpAudioAccessUnitEvent
            {
                IsLoss = true,
                Duration = lossDuration,
                RtpTimestamp = packetEvent.ExpectedTimestamp,
                RtpSequenceNumber = packetEvent.SequenceNumber
            };

            return sink.TryWrite(lossEvent) ? RtpAudioStatus.Success : RtpAudioStatus.SinkBackpressure;
        }
        else if (packetEvent.Kind != RtpPacketEventKind.Packet)
        {
            return RtpAudioStatus.InvalidPacket;
        }

        RtpPacket packet = packetEvent.Packet;
        if (packetEvent.Ssrc != packet.Header.Ssrc || packetEvent.SequenceNumber != packet.Header.SequenceNumber)
        {
            return RtpAudioStatus.InvalidPacket;
        }

        if (packet.Header.PayloadType > 127)
        {
            return RtpAudioStatus.InvalidPacket;
        }

        if (!formatMap.TryGetFormat(packet.Header.PayloadType, out RtpAudioFormatBinding binding))
        {
            return RtpAudioStatus.UnknownPayloadType;
        }

        if (!IsFormatUsable(binding.EncodedFormat))
        {
            return RtpAudioStatus.UnsupportedFormat;
        }

        TimeSpan duration = binding.DefaultPacketTime ?? TimeSpan.Zero;
        if (hasPreviousPacket && packet.Header.Ssrc == previousSsrc && packet.Header.Timestamp != previousTimestamp)
        {
            int clockRate = binding.EncodedFormat.RtpClockRate ?? binding.EncodedFormat.SampleRate;
            uint timestampDelta = packet.Header.Timestamp - previousTimestamp;
            duration = TimeSpan.FromSeconds((double)timestampDelta / clockRate);
        }

        var frame = new EncodedAudioFrame
        {
            Format = binding.EncodedFormat,
            Data = packet.Payload,
            Duration = duration,
            RtpTimestamp = packet.Header.Timestamp,
            RtpSequenceNumber = packet.Header.SequenceNumber
        };

        var accessUnitEvent = new RtpAudioAccessUnitEvent
        {
            IsLoss = false,
            Frame = frame,
            Duration = duration,
            RtpTimestamp = packet.Header.Timestamp,
            RtpSequenceNumber = packet.Header.SequenceNumber
        };

        if (!sink.TryWrite(accessUnitEvent))
        {
            return RtpAudioStatus.SinkBackpressure;
        }

        hasPreviousPacket = true;
        previousSsrc = packet.Header.Ssrc;
        previousTimestamp = packet.Header.Timestamp;
        previousBinding = binding;
        previousDuration = duration;
        return RtpAudioStatus.Success;
    }

    private static bool IsFormatUsable(in EncodedAudioFormat format)
    {
        return format.SampleRate > 0 &&
            format.ChannelCount > 0 &&
            (format.RtpClockRate ?? format.SampleRate) > 0;
    }
}

/// <summary>
/// Converts encoded audio access units into one RTP packet per access unit.
/// </summary>
public sealed class RtpAudioPacketizer : IRtpAudioPacketizer
{
    private IRtpAudioFormatMap formatMap;
    private ushort nextSequenceNumber;
    private uint nextTimestamp;
    private bool hasTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="RtpAudioPacketizer"/> class.
    /// </summary>
    public RtpAudioPacketizer(IRtpAudioFormatMap formatMap, ushort initialSequenceNumber = 0, uint initialTimestamp = 0)
    {
        ArgumentNullException.ThrowIfNull(formatMap);
        this.formatMap = formatMap;
        nextSequenceNumber = initialSequenceNumber;
        nextTimestamp = initialTimestamp;
    }

    /// <summary>
    /// Replaces the RTP audio payload-type map at a documented packet boundary.
    /// </summary>
    public void UpdateFormatMap(IRtpAudioFormatMap formatMap)
    {
        ArgumentNullException.ThrowIfNull(formatMap);
        this.formatMap = formatMap;
    }

    /// <inheritdoc />
    public RtpAudioStatus Packetize(in EncodedAudioFrame frame, uint ssrc, byte payloadType, IRtpPacketSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (payloadType > 127)
        {
            return RtpAudioStatus.InvalidPacket;
        }

        if (!formatMap.TryGetFormat(payloadType, out RtpAudioFormatBinding binding))
        {
            return RtpAudioStatus.UnknownPayloadType;
        }

        if (!IsFormatUsable(binding.EncodedFormat))
        {
            return RtpAudioStatus.UnsupportedFormat;
        }

        if (!FormatsMatch(frame.Format, binding.EncodedFormat))
        {
            return RtpAudioStatus.UnsupportedFormat;
        }

        if (frame.Duration <= TimeSpan.Zero)
        {
            return RtpAudioStatus.InvalidPacket;
        }

        if (!TryConvertDurationToTimestampUnits(frame.Duration, binding.EncodedFormat, out uint timestampUnits))
        {
            return RtpAudioStatus.InvalidPacket;
        }

        uint timestamp = frame.RtpTimestamp ?? (hasTimestamp ? nextTimestamp : nextTimestamp);
        ushort sequenceNumber = frame.RtpSequenceNumber ?? nextSequenceNumber;
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = payloadType,
                SequenceNumber = sequenceNumber,
                Timestamp = timestamp,
                Ssrc = ssrc
            },
            Payload = frame.Data,
            ArrivalTime = DateTimeOffset.UtcNow
        };

        if (!sink.TryWrite(packet))
        {
            return RtpAudioStatus.SinkBackpressure;
        }

        nextSequenceNumber = (ushort)(sequenceNumber + 1);
        nextTimestamp = timestamp + timestampUnits;
        hasTimestamp = true;
        return RtpAudioStatus.Success;
    }

    private static bool FormatsMatch(in EncodedAudioFormat actual, in EncodedAudioFormat expected)
    {
        return actual.Encoding == expected.Encoding &&
            actual.SampleRate == expected.SampleRate &&
            actual.ChannelCount == expected.ChannelCount &&
            (actual.RtpClockRate ?? actual.SampleRate) == (expected.RtpClockRate ?? expected.SampleRate);
    }

    private static bool IsFormatUsable(in EncodedAudioFormat format)
    {
        return format.SampleRate > 0 &&
            format.ChannelCount > 0 &&
            (format.RtpClockRate ?? format.SampleRate) > 0;
    }

    private static bool TryConvertDurationToTimestampUnits(
        TimeSpan duration,
        in EncodedAudioFormat format,
        out uint timestampUnits)
    {
        timestampUnits = 0;
        int clockRate = format.RtpClockRate ?? format.SampleRate;
        if (duration <= TimeSpan.Zero || clockRate <= 0)
        {
            return false;
        }

        double units = duration.TotalSeconds * clockRate;
        if (units < 0.5 || units > uint.MaxValue)
        {
            return false;
        }

        timestampUnits = (uint)Math.Round(units);
        return timestampUnits != 0;
    }
}
