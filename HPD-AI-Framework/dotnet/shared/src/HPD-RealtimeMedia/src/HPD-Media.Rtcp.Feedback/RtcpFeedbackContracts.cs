#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using HPD.Media.Rtcp;

namespace HPD.Media.Rtcp.Feedback;

/// <summary>
/// Represents one RTCP Generic NACK feedback control information entry.
/// </summary>
public readonly struct RtcpNackEntry
{
    /// <summary>Gets the packet identifier for the first missing packet.</summary>
    public required ushort PacketId { get; init; }

    /// <summary>Gets the bitmask of the following 16 sequence numbers.</summary>
    public ushort LostPacketBitmask { get; init; }

    /// <summary>Gets a value indicating whether the given sequence number is marked lost.</summary>
    public bool Contains(ushort sequenceNumber)
    {
        if (sequenceNumber == PacketId)
        {
            return true;
        }

        int delta = (ushort)(sequenceNumber - PacketId);
        return delta is >= 1 and <= 16 && ((LostPacketBitmask >> (delta - 1)) & 1) != 0;
    }
}

/// <summary>
/// Represents an RTCP Generic NACK packet.
/// </summary>
public readonly struct RtcpNackPacket
{
    /// <summary>Gets the SSRC of the packet sender.</summary>
    public required uint SenderSsrc { get; init; }

    /// <summary>Gets the SSRC of the media source.</summary>
    public required uint MediaSsrc { get; init; }

    /// <summary>Gets NACK feedback entries.</summary>
    public required ReadOnlyMemory<RtcpNackEntry> Entries { get; init; }
}

/// <summary>
/// Represents an RTCP Picture Loss Indication packet.
/// </summary>
public readonly struct RtcpPictureLossIndication
{
    /// <summary>Gets the SSRC of the packet sender.</summary>
    public required uint SenderSsrc { get; init; }

    /// <summary>Gets the SSRC of the media source.</summary>
    public required uint MediaSsrc { get; init; }
}

/// <summary>
/// Represents one RTCP Full Intra Request feedback control information entry.
/// </summary>
public readonly struct RtcpFullIntraRequestEntry
{
    /// <summary>Gets the SSRC of the media sender that should send a decoder refresh point.</summary>
    public required uint Ssrc { get; init; }

    /// <summary>Gets the FIR command sequence number.</summary>
    public required byte SequenceNumber { get; init; }
}

/// <summary>
/// Represents an RTCP Full Intra Request packet.
/// </summary>
public readonly struct RtcpFullIntraRequest
{
    /// <summary>Gets the SSRC of the packet sender.</summary>
    public required uint SenderSsrc { get; init; }

    /// <summary>Gets the SSRC of the media source, or zero when entries name target sources.</summary>
    public required uint MediaSsrc { get; init; }

    /// <summary>Gets FIR feedback entries.</summary>
    public required ReadOnlyMemory<RtcpFullIntraRequestEntry> Entries { get; init; }
}

/// <summary>
/// Represents an RTCP Receiver Estimated Maximum Bitrate packet.
/// </summary>
public readonly struct RtcpReceiverEstimatedMaximumBitrate
{
    /// <summary>Gets the SSRC of the packet sender.</summary>
    public required uint SenderSsrc { get; init; }

    /// <summary>Gets the SSRC of the media source, usually zero for REMB.</summary>
    public required uint MediaSsrc { get; init; }

    /// <summary>Gets the estimated maximum bitrate in bits per second.</summary>
    public required ulong BitrateBitsPerSecond { get; init; }

    /// <summary>Gets SSRCs covered by this bitrate estimate.</summary>
    public required ReadOnlyMemory<uint> Ssrcs { get; init; }
}

/// <summary>
/// Represents a stack-only RTCP feedback packet view over caller-owned bytes.
/// </summary>
public readonly ref struct RtcpFeedbackPacketView
{
    /// <summary>Initializes a new instance of the <see cref="RtcpFeedbackPacketView"/> struct.</summary>
    public RtcpFeedbackPacketView(
        RtcpPacketType packetType,
        byte feedbackMessageType,
        uint senderSsrc,
        uint mediaSsrc,
        ReadOnlySpan<byte> feedbackControlInformation)
    {
        PacketType = packetType;
        FeedbackMessageType = feedbackMessageType;
        SenderSsrc = senderSsrc;
        MediaSsrc = mediaSsrc;
        FeedbackControlInformation = feedbackControlInformation;
    }

    /// <summary>Gets the RTCP feedback packet type.</summary>
    public RtcpPacketType PacketType { get; }

    /// <summary>Gets the feedback message type from the RTCP count/FMT field.</summary>
    public byte FeedbackMessageType { get; }

    /// <summary>Gets the SSRC of the packet sender.</summary>
    public uint SenderSsrc { get; }

    /// <summary>Gets the SSRC of the media source.</summary>
    public uint MediaSsrc { get; }

    /// <summary>Gets the raw feedback control information bytes.</summary>
    public ReadOnlySpan<byte> FeedbackControlInformation { get; }
}

/// <summary>
/// Represents a retained RTCP feedback packet with raw feedback control information.
/// </summary>
public readonly struct RtcpFeedbackPacket
{
    /// <summary>Gets the RTCP feedback packet type.</summary>
    public required RtcpPacketType PacketType { get; init; }

    /// <summary>Gets the feedback message type from the RTCP count/FMT field.</summary>
    public required byte FeedbackMessageType { get; init; }

    /// <summary>Gets the SSRC of the packet sender.</summary>
    public required uint SenderSsrc { get; init; }

    /// <summary>Gets the SSRC of the media source.</summary>
    public required uint MediaSsrc { get; init; }

    /// <summary>Gets the raw feedback control information bytes.</summary>
    public ReadOnlyMemory<byte> FeedbackControlInformation { get; init; }
}

/// <summary>
/// Parses RTCP feedback packets from caller-owned spans.
/// </summary>
public static class RtcpFeedbackPacketReader
{
    private const int FeedbackFixedLength = 12;
    private const int NackEntryLength = 4;
    private const int FirEntryLength = 8;
    private const int RembFixedFciLength = 8;
    private const byte RtcpVersion = 2;
    private const byte GenericNackFormat = 1;
    private const byte PictureLossIndicationFormat = 1;
    private const byte FullIntraRequestFormat = 4;
    private const byte ApplicationLayerFeedbackFormat = 15;
    private const uint RembUniqueIdentifier = 0x52454D42;

    /// <summary>
    /// Attempts to parse a generic RTCP feedback packet view.
    /// </summary>
    public static RtcpPacketStatus TryParse(ReadOnlySpan<byte> packet, out RtcpFeedbackPacketView view)
    {
        view = default;
        if (!TryReadFeedbackHeader(
            packet,
            expectedPacketType: null,
            expectedFeedbackMessageType: null,
            out RtcpPacketType packetType,
            out byte feedbackMessageType,
            out uint senderSsrc,
            out uint mediaSsrc))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        view = new RtcpFeedbackPacketView(
            packetType,
            feedbackMessageType,
            senderSsrc,
            mediaSsrc,
            packet[FeedbackFixedLength..]);
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Attempts to parse an RTCP Generic NACK packet.
    /// </summary>
    public static RtcpPacketStatus TryParseGenericNack(
        ReadOnlySpan<byte> packet,
        Memory<RtcpNackEntry> entryBuffer,
        out RtcpNackPacket nack)
    {
        nack = default;
        if (!TryReadFeedbackHeader(
            packet,
            RtcpPacketType.TransportFeedback,
            GenericNackFormat,
            out _,
            out _,
            out uint senderSsrc,
            out uint mediaSsrc))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        int entryCount = (packet.Length - FeedbackFixedLength) / NackEntryLength;
        if (entryCount == 0 || (packet.Length - FeedbackFixedLength) % NackEntryLength != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (entryBuffer.Length < entryCount)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        Span<RtcpNackEntry> entries = entryBuffer.Span;
        for (int i = 0; i < entryCount; i++)
        {
            int cursor = FeedbackFixedLength + (i * NackEntryLength);
            entries[i] = new RtcpNackEntry
            {
                PacketId = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(cursor, 2)),
                LostPacketBitmask = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(cursor + 2, 2))
            };
        }

        nack = new RtcpNackPacket
        {
            SenderSsrc = senderSsrc,
            MediaSsrc = mediaSsrc,
            Entries = entryBuffer[..entryCount]
        };
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Attempts to parse an RTCP Picture Loss Indication packet.
    /// </summary>
    public static RtcpPacketStatus TryParsePictureLossIndication(
        ReadOnlySpan<byte> packet,
        out RtcpPictureLossIndication pictureLossIndication)
    {
        pictureLossIndication = default;
        if (!TryReadFeedbackHeader(
            packet,
            RtcpPacketType.PayloadFeedback,
            PictureLossIndicationFormat,
            out _,
            out _,
            out uint senderSsrc,
            out uint mediaSsrc))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (packet.Length != FeedbackFixedLength)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        pictureLossIndication = new RtcpPictureLossIndication
        {
            SenderSsrc = senderSsrc,
            MediaSsrc = mediaSsrc
        };
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Attempts to parse an RTCP Full Intra Request packet.
    /// </summary>
    public static RtcpPacketStatus TryParseFullIntraRequest(
        ReadOnlySpan<byte> packet,
        Memory<RtcpFullIntraRequestEntry> entryBuffer,
        out RtcpFullIntraRequest fullIntraRequest)
    {
        fullIntraRequest = default;
        if (!TryReadFeedbackHeader(
            packet,
            RtcpPacketType.PayloadFeedback,
            FullIntraRequestFormat,
            out _,
            out _,
            out uint senderSsrc,
            out uint mediaSsrc))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        int entryCount = (packet.Length - FeedbackFixedLength) / FirEntryLength;
        if (entryCount == 0 || (packet.Length - FeedbackFixedLength) % FirEntryLength != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (entryBuffer.Length < entryCount)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        Span<RtcpFullIntraRequestEntry> entries = entryBuffer.Span;
        for (int i = 0; i < entryCount; i++)
        {
            int cursor = FeedbackFixedLength + (i * FirEntryLength);
            if (packet[cursor + 5] != 0 || packet[cursor + 6] != 0 || packet[cursor + 7] != 0)
            {
                return RtcpPacketStatus.InvalidPacket;
            }

            entries[i] = new RtcpFullIntraRequestEntry
            {
                Ssrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(cursor, 4)),
                SequenceNumber = packet[cursor + 4]
            };
        }

        fullIntraRequest = new RtcpFullIntraRequest
        {
            SenderSsrc = senderSsrc,
            MediaSsrc = mediaSsrc,
            Entries = entryBuffer[..entryCount]
        };
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Attempts to parse an RTCP Receiver Estimated Maximum Bitrate packet.
    /// </summary>
    public static RtcpPacketStatus TryParseReceiverEstimatedMaximumBitrate(
        ReadOnlySpan<byte> packet,
        Memory<uint> ssrcBuffer,
        out RtcpReceiverEstimatedMaximumBitrate receiverEstimatedMaximumBitrate)
    {
        receiverEstimatedMaximumBitrate = default;
        if (!TryReadFeedbackHeader(
            packet,
            RtcpPacketType.PayloadFeedback,
            ApplicationLayerFeedbackFormat,
            out _,
            out _,
            out uint senderSsrc,
            out uint mediaSsrc))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        ReadOnlySpan<byte> fci = packet[FeedbackFixedLength..];
        if (fci.Length < RembFixedFciLength ||
            BinaryPrimitives.ReadUInt32BigEndian(fci[..4]) != RembUniqueIdentifier)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        int ssrcCount = fci[4];
        int expectedFciLength = RembFixedFciLength + (ssrcCount * sizeof(uint));
        if (ssrcCount == 0 || fci.Length != expectedFciLength)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (ssrcBuffer.Length < ssrcCount)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        ulong exponent = (uint)(fci[5] >> 2);
        ulong mantissa = (uint)(((fci[5] & 0x03) << 16) |
            BinaryPrimitives.ReadUInt16BigEndian(fci.Slice(6, 2)));
        if (exponent >= 64 || mantissa > (ulong.MaxValue >> checked((int)exponent)))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        Span<uint> ssrcs = ssrcBuffer.Span;
        for (int i = 0; i < ssrcCount; i++)
        {
            ssrcs[i] = BinaryPrimitives.ReadUInt32BigEndian(fci.Slice(RembFixedFciLength + (i * sizeof(uint)), sizeof(uint)));
        }

        receiverEstimatedMaximumBitrate = new RtcpReceiverEstimatedMaximumBitrate
        {
            SenderSsrc = senderSsrc,
            MediaSsrc = mediaSsrc,
            BitrateBitsPerSecond = mantissa << checked((int)exponent),
            Ssrcs = ssrcBuffer[..ssrcCount]
        };
        return RtcpPacketStatus.Success;
    }

    private static bool TryReadFeedbackHeader(
        ReadOnlySpan<byte> packet,
        RtcpPacketType? expectedPacketType,
        byte? expectedFeedbackMessageType,
        out RtcpPacketType packetType,
        out byte feedbackMessageType,
        out uint senderSsrc,
        out uint mediaSsrc)
    {
        packetType = default;
        feedbackMessageType = 0;
        senderSsrc = 0;
        mediaSsrc = 0;
        if (packet.Length < FeedbackFixedLength || (packet[0] >> 6) != RtcpVersion)
        {
            return false;
        }

        if ((packet[0] & 0x20) != 0)
        {
            return false;
        }

        feedbackMessageType = (byte)(packet[0] & 0x1F);
        packetType = (RtcpPacketType)packet[1];
        if (feedbackMessageType == 0 ||
            packetType is not RtcpPacketType.TransportFeedback and not RtcpPacketType.PayloadFeedback)
        {
            return false;
        }

        if ((expectedFeedbackMessageType.HasValue && feedbackMessageType != expectedFeedbackMessageType.Value) ||
            (expectedPacketType.HasValue && packetType != expectedPacketType.Value))
        {
            return false;
        }

        int encodedLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)) + 1) * 4;
        if (encodedLength != packet.Length)
        {
            return false;
        }

        senderSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4));
        mediaSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(8, 4));
        return true;
    }
}

/// <summary>
/// Writes RTCP feedback packets to caller-provided storage.
/// </summary>
public static class RtcpFeedbackPacketWriter
{
    private const int FeedbackFixedLength = 12;
    private const int MaximumPacketLength = (ushort.MaxValue + 1) * 4;
    private const int NackEntryLength = 4;
    private const int FirEntryLength = 8;
    private const int RembFixedFciLength = 8;
    private const byte RtcpVersion = 2;
    private const byte GenericNackFormat = 1;
    private const byte PictureLossIndicationFormat = 1;
    private const byte FullIntraRequestFormat = 4;
    private const byte ApplicationLayerFeedbackFormat = 15;
    private const uint RembUniqueIdentifier = 0x52454D42;

    /// <summary>
    /// Attempts to write a generic RTCP feedback packet.
    /// </summary>
    public static RtcpPacketStatus TryWrite(
        in RtcpFeedbackPacket feedback,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        RtcpPacketStatus validationStatus = ValidateGenericFeedback(feedback, out int requiredLength);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            return validationStatus;
        }

        if (destination.Length < requiredLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        WriteFeedbackHeader(
            destination,
            feedback.PacketType,
            feedback.FeedbackMessageType,
            requiredLength,
            feedback.SenderSsrc,
            feedback.MediaSsrc);
        feedback.FeedbackControlInformation.Span.CopyTo(destination[FeedbackFixedLength..]);
        bytesWritten = requiredLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Writes a generic RTCP feedback packet to a caller-provided buffer writer.
    /// </summary>
    public static void Write(in RtcpFeedbackPacket feedback, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        RtcpPacketStatus validationStatus = ValidateGenericFeedback(feedback, out int requiredLength);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP feedback write failed with status {validationStatus}.");
        }

        Span<byte> span = destination.GetSpan(requiredLength);
        RtcpPacketStatus status = TryWrite(feedback, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP feedback write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>
    /// Attempts to write an RTCP Generic NACK packet.
    /// </summary>
    public static RtcpPacketStatus TryWriteGenericNack(in RtcpNackPacket nack, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (nack.Entries.IsEmpty ||
            !TryCalculateRepeatedLength(nack.Entries.Length, NackEntryLength, out int requiredLength))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (destination.Length < requiredLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        WriteFeedbackHeader(
            destination,
            RtcpPacketType.TransportFeedback,
            GenericNackFormat,
            requiredLength,
            nack.SenderSsrc,
            nack.MediaSsrc);

        int cursor = FeedbackFixedLength;
        foreach (RtcpNackEntry entry in nack.Entries.Span)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(cursor, 2), entry.PacketId);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(cursor + 2, 2), entry.LostPacketBitmask);
            cursor += NackEntryLength;
        }

        bytesWritten = requiredLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Writes an RTCP Generic NACK packet to a caller-provided buffer writer.
    /// </summary>
    public static void WriteGenericNack(in RtcpNackPacket nack, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (nack.Entries.IsEmpty ||
            !TryCalculateRepeatedLength(nack.Entries.Length, NackEntryLength, out int requiredLength))
        {
            throw new InvalidOperationException($"RTCP Generic NACK write failed with status {RtcpPacketStatus.InvalidPacket}.");
        }

        Span<byte> span = destination.GetSpan(requiredLength);
        RtcpPacketStatus status = TryWriteGenericNack(nack, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP Generic NACK write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>
    /// Attempts to write an RTCP Picture Loss Indication packet.
    /// </summary>
    public static RtcpPacketStatus TryWritePictureLossIndication(
        in RtcpPictureLossIndication pictureLossIndication,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (destination.Length < FeedbackFixedLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        WriteFeedbackHeader(
            destination,
            RtcpPacketType.PayloadFeedback,
            PictureLossIndicationFormat,
            FeedbackFixedLength,
            pictureLossIndication.SenderSsrc,
            pictureLossIndication.MediaSsrc);

        bytesWritten = FeedbackFixedLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Writes an RTCP Picture Loss Indication packet to a caller-provided buffer writer.
    /// </summary>
    public static void WritePictureLossIndication(
        in RtcpPictureLossIndication pictureLossIndication,
        IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        Span<byte> span = destination.GetSpan(FeedbackFixedLength);
        RtcpPacketStatus status = TryWritePictureLossIndication(pictureLossIndication, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP Picture Loss Indication write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>
    /// Attempts to write an RTCP Full Intra Request packet.
    /// </summary>
    public static RtcpPacketStatus TryWriteFullIntraRequest(
        in RtcpFullIntraRequest fullIntraRequest,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (fullIntraRequest.Entries.IsEmpty ||
            !TryCalculateRepeatedLength(fullIntraRequest.Entries.Length, FirEntryLength, out int requiredLength))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (destination.Length < requiredLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        WriteFeedbackHeader(
            destination,
            RtcpPacketType.PayloadFeedback,
            FullIntraRequestFormat,
            requiredLength,
            fullIntraRequest.SenderSsrc,
            fullIntraRequest.MediaSsrc);

        int cursor = FeedbackFixedLength;
        foreach (RtcpFullIntraRequestEntry entry in fullIntraRequest.Entries.Span)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor, 4), entry.Ssrc);
            destination[cursor + 4] = entry.SequenceNumber;
            destination.Slice(cursor + 5, 3).Clear();
            cursor += FirEntryLength;
        }

        bytesWritten = requiredLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Writes an RTCP Full Intra Request packet to a caller-provided buffer writer.
    /// </summary>
    public static void WriteFullIntraRequest(in RtcpFullIntraRequest fullIntraRequest, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (fullIntraRequest.Entries.IsEmpty ||
            !TryCalculateRepeatedLength(fullIntraRequest.Entries.Length, FirEntryLength, out int requiredLength))
        {
            throw new InvalidOperationException($"RTCP Full Intra Request write failed with status {RtcpPacketStatus.InvalidPacket}.");
        }

        Span<byte> span = destination.GetSpan(requiredLength);
        RtcpPacketStatus status = TryWriteFullIntraRequest(fullIntraRequest, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP Full Intra Request write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>
    /// Attempts to write an RTCP Receiver Estimated Maximum Bitrate packet.
    /// </summary>
    public static RtcpPacketStatus TryWriteReceiverEstimatedMaximumBitrate(
        in RtcpReceiverEstimatedMaximumBitrate receiverEstimatedMaximumBitrate,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        RtcpPacketStatus validationStatus = ValidateReceiverEstimatedMaximumBitrate(
            receiverEstimatedMaximumBitrate,
            out int requiredLength,
            out byte exponent,
            out uint mantissa);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            return validationStatus;
        }

        if (destination.Length < requiredLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        WriteFeedbackHeader(
            destination,
            RtcpPacketType.PayloadFeedback,
            ApplicationLayerFeedbackFormat,
            requiredLength,
            receiverEstimatedMaximumBitrate.SenderSsrc,
            receiverEstimatedMaximumBitrate.MediaSsrc);

        Span<byte> fci = destination[FeedbackFixedLength..requiredLength];
        BinaryPrimitives.WriteUInt32BigEndian(fci[..4], RembUniqueIdentifier);
        fci[4] = checked((byte)receiverEstimatedMaximumBitrate.Ssrcs.Length);
        fci[5] = (byte)((exponent << 2) | ((mantissa >> 16) & 0x03));
        BinaryPrimitives.WriteUInt16BigEndian(fci.Slice(6, 2), (ushort)(mantissa & 0xFFFF));
        int cursor = RembFixedFciLength;
        foreach (uint ssrc in receiverEstimatedMaximumBitrate.Ssrcs.Span)
        {
            BinaryPrimitives.WriteUInt32BigEndian(fci.Slice(cursor, sizeof(uint)), ssrc);
            cursor += sizeof(uint);
        }

        bytesWritten = requiredLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Writes an RTCP Receiver Estimated Maximum Bitrate packet to a caller-provided buffer writer.
    /// </summary>
    public static void WriteReceiverEstimatedMaximumBitrate(
        in RtcpReceiverEstimatedMaximumBitrate receiverEstimatedMaximumBitrate,
        IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        RtcpPacketStatus validationStatus = ValidateReceiverEstimatedMaximumBitrate(
            receiverEstimatedMaximumBitrate,
            out int requiredLength,
            out _,
            out _);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP REMB write failed with status {validationStatus}.");
        }

        Span<byte> span = destination.GetSpan(requiredLength);
        RtcpPacketStatus status = TryWriteReceiverEstimatedMaximumBitrate(
            receiverEstimatedMaximumBitrate,
            span,
            out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP REMB write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    private static void WriteFeedbackHeader(
        Span<byte> destination,
        RtcpPacketType packetType,
        byte feedbackMessageType,
        int packetLength,
        uint senderSsrc,
        uint mediaSsrc)
    {
        destination[0] = (byte)((RtcpVersion << 6) | feedbackMessageType);
        destination[1] = (byte)packetType;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), checked((ushort)((packetLength / 4) - 1)));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), senderSsrc);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), mediaSsrc);
    }

    private static bool TryCalculateRepeatedLength(int count, int itemLength, out int requiredLength)
    {
        return TryCalculateRepeatedLength(count, itemLength, FeedbackFixedLength, out requiredLength);
    }

    private static bool TryCalculateRepeatedLength(int count, int itemLength, int fixedLength, out int requiredLength)
    {
        requiredLength = 0;
        if (count < 0 || count > (MaximumPacketLength - fixedLength) / itemLength)
        {
            return false;
        }

        requiredLength = fixedLength + (count * itemLength);
        return true;
    }

    private static bool TryAddLength(int fixedLength, int variableLength, out int requiredLength)
    {
        requiredLength = 0;
        if (variableLength < 0 || variableLength > MaximumPacketLength - fixedLength)
        {
            return false;
        }

        requiredLength = fixedLength + variableLength;
        return true;
    }

    private static RtcpPacketStatus ValidateGenericFeedback(in RtcpFeedbackPacket feedback, out int requiredLength)
    {
        requiredLength = 0;
        if (feedback.PacketType is not RtcpPacketType.TransportFeedback and not RtcpPacketType.PayloadFeedback ||
            feedback.FeedbackMessageType is 0 or > 31 ||
            (feedback.FeedbackControlInformation.Length & 3) != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        return TryAddLength(FeedbackFixedLength, feedback.FeedbackControlInformation.Length, out requiredLength)
            ? RtcpPacketStatus.Success
            : RtcpPacketStatus.InvalidPacket;
    }

    private static RtcpPacketStatus ValidateReceiverEstimatedMaximumBitrate(
        in RtcpReceiverEstimatedMaximumBitrate receiverEstimatedMaximumBitrate,
        out int requiredLength,
        out byte exponent,
        out uint mantissa)
    {
        requiredLength = 0;
        exponent = 0;
        mantissa = 0;
        if (receiverEstimatedMaximumBitrate.Ssrcs.Length == 0 ||
            receiverEstimatedMaximumBitrate.Ssrcs.Length > byte.MaxValue ||
            !TryEncodeBitrate(receiverEstimatedMaximumBitrate.BitrateBitsPerSecond, out exponent, out mantissa))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        return TryCalculateRepeatedLength(
            receiverEstimatedMaximumBitrate.Ssrcs.Length,
            sizeof(uint),
            FeedbackFixedLength + RembFixedFciLength,
            out requiredLength)
            ? RtcpPacketStatus.Success
            : RtcpPacketStatus.InvalidPacket;
    }

    private static bool TryEncodeBitrate(ulong bitrateBitsPerSecond, out byte exponent, out uint mantissa)
    {
        exponent = 0;
        mantissa = 0;
        ulong value = bitrateBitsPerSecond;
        while (value > 0x3FFFF)
        {
            value >>= 1;
            exponent++;
            if (exponent > 63)
            {
                return false;
            }
        }

        mantissa = (uint)value;
        return true;
    }
}
