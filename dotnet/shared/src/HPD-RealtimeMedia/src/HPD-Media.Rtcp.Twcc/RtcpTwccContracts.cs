#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using HPD.Media.Rtcp;

namespace HPD.Media.Rtcp.Twcc;

/// <summary>
/// Identifies a TWCC packet status symbol.
/// </summary>
public enum RtcpTwccPacketStatusSymbol
{
    /// <summary>The packet was not received.</summary>
    NotReceived = 0,

    /// <summary>The packet was received with a small positive delta.</summary>
    SmallDelta = 1,

    /// <summary>The packet was received with a large or negative delta.</summary>
    LargeOrNegativeDelta = 2
}

/// <summary>
/// Represents one decoded TWCC packet status.
/// </summary>
public readonly struct RtcpTwccPacketStatus
{
    /// <summary>Gets the packet status symbol.</summary>
    public required RtcpTwccPacketStatusSymbol Symbol { get; init; }

    /// <summary>Gets the received delta in 250 microsecond units, or zero when not received.</summary>
    public short Delta250Microseconds { get; init; }
}

/// <summary>
/// Represents an RTCP transport-wide congestion-control feedback packet.
/// </summary>
public readonly struct RtcpTwccFeedbackPacket
{
    /// <summary>Gets the SSRC of the feedback packet sender.</summary>
    public required uint SenderSsrc { get; init; }

    /// <summary>Gets the SSRC of the media source.</summary>
    public required uint MediaSsrc { get; init; }

    /// <summary>Gets the first RTP transport-wide sequence number represented by this packet.</summary>
    public required ushort BaseSequenceNumber { get; init; }

    /// <summary>Gets the 24-bit reference time value in 64 millisecond units.</summary>
    public required uint ReferenceTime64Milliseconds { get; init; }

    /// <summary>Gets the feedback packet count.</summary>
    public required byte FeedbackPacketCount { get; init; }

    /// <summary>Gets packet statuses in sequence-number order.</summary>
    public required ReadOnlyMemory<RtcpTwccPacketStatus> PacketStatuses { get; init; }
}

/// <summary>
/// Represents one received RTP packet for transport-wide congestion-control feedback construction.
/// </summary>
public readonly struct RtcpTwccReceivedPacket
{
    /// <summary>Gets the transport-wide sequence number.</summary>
    public required ushort SequenceNumber { get; init; }

    /// <summary>Gets the arrival time in 250 microsecond units.</summary>
    public required long ArrivalTime250Microseconds { get; init; }
}

/// <summary>
/// Builds TWCC feedback packets from caller-supplied reception observations without allocating.
/// </summary>
public static class RtcpTwccFeedbackBuilder
{
    private const int ReferenceTimeUnit250Microseconds = 256;

    /// <summary>
    /// Attempts to build a TWCC feedback packet from sequence-sorted received packet observations.
    /// </summary>
    public static RtcpPacketStatus TryBuild(
        uint senderSsrc,
        uint mediaSsrc,
        byte feedbackPacketCount,
        ReadOnlySpan<RtcpTwccReceivedPacket> receivedPackets,
        Memory<RtcpTwccPacketStatus> statusBuffer,
        out RtcpTwccFeedbackPacket feedback)
    {
        feedback = default;
        if (receivedPackets.IsEmpty)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        ushort baseSequenceNumber = receivedPackets[0].SequenceNumber;
        int packetStatusCount = GetSequenceDistanceInclusive(baseSequenceNumber, receivedPackets[^1].SequenceNumber);
        if (packetStatusCount <= 0 || packetStatusCount > ushort.MaxValue || statusBuffer.Length < packetStatusCount)
        {
            return packetStatusCount is <= 0 or > ushort.MaxValue ? RtcpPacketStatus.InvalidPacket : RtcpPacketStatus.DestinationTooSmall;
        }

        Span<RtcpTwccPacketStatus> statuses = statusBuffer.Span[..packetStatusCount];
        statuses.Clear();

        long referenceTime250Microseconds = FloorToReferenceTime(receivedPackets[0].ArrivalTime250Microseconds);
        long previousReceivedArrival = referenceTime250Microseconds;
        int expectedOffset = 0;
        for (int i = 0; i < receivedPackets.Length; i++)
        {
            RtcpTwccReceivedPacket received = receivedPackets[i];
            int offset = GetSequenceDistanceInclusive(baseSequenceNumber, received.SequenceNumber) - 1;
            if (offset < expectedOffset || offset >= packetStatusCount)
            {
                return RtcpPacketStatus.InvalidPacket;
            }

            long delta = received.ArrivalTime250Microseconds - previousReceivedArrival;
            if (delta is < short.MinValue or > short.MaxValue)
            {
                return RtcpPacketStatus.InvalidPacket;
            }

            statuses[offset] = new RtcpTwccPacketStatus
            {
                Symbol = delta is >= 0 and <= byte.MaxValue
                    ? RtcpTwccPacketStatusSymbol.SmallDelta
                    : RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta,
                Delta250Microseconds = (short)delta
            };
            previousReceivedArrival = received.ArrivalTime250Microseconds;
            expectedOffset = offset + 1;
        }

        long referenceTime64Milliseconds = referenceTime250Microseconds / ReferenceTimeUnit250Microseconds;
        if (referenceTime64Milliseconds is < 0 or > 0x00FF_FFFF)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = senderSsrc,
            MediaSsrc = mediaSsrc,
            BaseSequenceNumber = baseSequenceNumber,
            ReferenceTime64Milliseconds = (uint)referenceTime64Milliseconds,
            FeedbackPacketCount = feedbackPacketCount,
            PacketStatuses = statusBuffer[..packetStatusCount]
        };
        return RtcpPacketStatus.Success;
    }

    private static int GetSequenceDistanceInclusive(ushort first, ushort last)
    {
        return ((last - first) & 0xFFFF) + 1;
    }

    private static long FloorToReferenceTime(long arrivalTime250Microseconds)
    {
        long remainder = arrivalTime250Microseconds % ReferenceTimeUnit250Microseconds;
        if (remainder < 0)
        {
            remainder += ReferenceTimeUnit250Microseconds;
        }

        return arrivalTime250Microseconds - remainder;
    }
}

/// <summary>
/// Parses RTCP TWCC feedback packets into caller-owned status memory.
/// </summary>
public static class RtcpTwccPacketReader
{
    private const int HeaderLength = 4;
    private const int FeedbackHeaderLength = 20;
    private const byte RtcpVersion = 2;
    private const byte TwccFeedbackMessageType = 15;

    /// <summary>
    /// Attempts to parse one TWCC feedback packet.
    /// </summary>
    public static RtcpPacketStatus TryParse(
        ReadOnlySpan<byte> packet,
        Memory<RtcpTwccPacketStatus> statusBuffer,
        out RtcpTwccFeedbackPacket feedback)
    {
        feedback = default;
        Span<RtcpTwccPacketStatus> statuses = statusBuffer.Span;
        if (packet.Length < FeedbackHeaderLength || (packet[0] >> 6) != RtcpVersion)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] & 0x20) != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] & 0x1F) != TwccFeedbackMessageType || packet[1] != (byte)RtcpPacketType.TransportFeedback)
        {
            return RtcpPacketStatus.UnsupportedPacketType;
        }

        int encodedLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)) + 1) * 4;
        if (encodedLength != packet.Length)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        ushort packetStatusCount = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(14, 2));
        if (packetStatusCount == 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (statuses.Length < packetStatusCount)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        int cursor = FeedbackHeaderLength;
        int statusCount = 0;
        while (statusCount < packetStatusCount)
        {
            if (cursor + 2 > packet.Length)
            {
                return RtcpPacketStatus.InvalidPacket;
            }

            ushort chunk = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(cursor, 2));
            cursor += 2;
            if ((chunk & 0x8000) == 0)
            {
                var symbol = (RtcpTwccPacketStatusSymbol)((chunk >> 13) & 0x03);
                int runLength = chunk & 0x1FFF;
                if (runLength == 0 || !IsKnownSymbol(symbol) || statusCount + runLength > packetStatusCount)
                {
                    return RtcpPacketStatus.InvalidPacket;
                }

                for (int i = 0; i < runLength; i++)
                {
                    statuses[statusCount++] = new RtcpTwccPacketStatus { Symbol = symbol };
                }
            }
            else
            {
                bool twoBit = (chunk & 0x4000) != 0;
                int symbolsPerChunk = twoBit ? 7 : 14;
                for (int i = 0; i < symbolsPerChunk; i++)
                {
                    int shift = twoBit ? 12 - (i * 2) : 13 - i;
                    var symbol = twoBit
                        ? (RtcpTwccPacketStatusSymbol)((chunk >> shift) & 0x03)
                        : ((chunk & (1 << shift)) == 0 ? RtcpTwccPacketStatusSymbol.NotReceived : RtcpTwccPacketStatusSymbol.SmallDelta);

                    if (!IsKnownSymbol(symbol))
                    {
                        return RtcpPacketStatus.InvalidPacket;
                    }

                    if (statusCount >= packetStatusCount)
                    {
                        if (symbol != RtcpTwccPacketStatusSymbol.NotReceived)
                        {
                            return RtcpPacketStatus.InvalidPacket;
                        }

                        continue;
                    }

                    statuses[statusCount++] = new RtcpTwccPacketStatus { Symbol = symbol };
                }
            }
        }

        for (int i = 0; i < packetStatusCount; i++)
        {
            RtcpTwccPacketStatus status = statuses[i];
            switch (status.Symbol)
            {
                case RtcpTwccPacketStatusSymbol.NotReceived:
                    continue;
                case RtcpTwccPacketStatusSymbol.SmallDelta:
                    if (cursor + 1 > packet.Length)
                    {
                        return RtcpPacketStatus.InvalidPacket;
                    }

                    statuses[i] = new RtcpTwccPacketStatus
                    {
                        Symbol = status.Symbol,
                        Delta250Microseconds = packet[cursor++]
                    };
                    break;
                case RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta:
                    if (cursor + 2 > packet.Length)
                    {
                        return RtcpPacketStatus.InvalidPacket;
                    }

                    statuses[i] = new RtcpTwccPacketStatus
                    {
                        Symbol = status.Symbol,
                        Delta250Microseconds = BinaryPrimitives.ReadInt16BigEndian(packet.Slice(cursor, 2))
                    };
                    cursor += 2;
                    break;
            }
        }

        if (packet.Length - cursor > 3)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        while (cursor < packet.Length)
        {
            if (packet[cursor++] != 0)
            {
                return RtcpPacketStatus.InvalidPacket;
            }
        }

        uint referenceTime = (uint)((packet[16] << 16) | (packet[17] << 8) | packet[18]);
        feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4)),
            MediaSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(8, 4)),
            BaseSequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(12, 2)),
            ReferenceTime64Milliseconds = referenceTime,
            FeedbackPacketCount = packet[19],
            PacketStatuses = statusBuffer[..packetStatusCount]
        };
        return RtcpPacketStatus.Success;
    }

    private static bool IsKnownSymbol(RtcpTwccPacketStatusSymbol symbol)
    {
        return symbol is RtcpTwccPacketStatusSymbol.NotReceived
            or RtcpTwccPacketStatusSymbol.SmallDelta
            or RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta;
    }
}

/// <summary>
/// Writes RTCP TWCC feedback packets to caller-owned storage.
/// </summary>
public static class RtcpTwccPacketWriter
{
    private const int FeedbackHeaderLength = 20;
    private const int MaximumRunLength = 0x1FFF;
    private const byte RtcpVersion = 2;
    private const byte TwccFeedbackMessageType = 15;

    /// <summary>
    /// Attempts to write one TWCC feedback packet using run-length status chunks.
    /// </summary>
    public static RtcpPacketStatus TryWrite(
        in RtcpTwccFeedbackPacket feedback,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        RtcpPacketStatus validationStatus = ValidateFeedback(feedback, out int paddedLength);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            return validationStatus;
        }

        if (destination.Length < paddedLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        destination[..paddedLength].Clear();
        destination[0] = (byte)((RtcpVersion << 6) | TwccFeedbackMessageType);
        destination[1] = (byte)RtcpPacketType.TransportFeedback;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), checked((ushort)((paddedLength / 4) - 1)));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), feedback.SenderSsrc);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), feedback.MediaSsrc);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(12, 2), feedback.BaseSequenceNumber);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(14, 2), checked((ushort)feedback.PacketStatuses.Length));
        destination[16] = (byte)((feedback.ReferenceTime64Milliseconds >> 16) & 0xFF);
        destination[17] = (byte)((feedback.ReferenceTime64Milliseconds >> 8) & 0xFF);
        destination[18] = (byte)(feedback.ReferenceTime64Milliseconds & 0xFF);
        destination[19] = feedback.FeedbackPacketCount;

        int cursor = FeedbackHeaderLength;
        ReadOnlySpan<RtcpTwccPacketStatus> statuses = feedback.PacketStatuses.Span;
        for (int i = 0; i < statuses.Length;)
        {
            RtcpTwccPacketStatusSymbol symbol = statuses[i].Symbol;
            int runLength = 1;
            while (i + runLength < statuses.Length && statuses[i + runLength].Symbol == symbol && runLength < MaximumRunLength)
            {
                runLength++;
            }

            ushort chunk = (ushort)(((int)symbol << 13) | runLength);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(cursor, 2), chunk);
            cursor += 2;
            i += runLength;
        }

        foreach (RtcpTwccPacketStatus status in statuses)
        {
            switch (status.Symbol)
            {
                case RtcpTwccPacketStatusSymbol.NotReceived:
                    break;
                case RtcpTwccPacketStatusSymbol.SmallDelta:
                    if (status.Delta250Microseconds is < 0 or > byte.MaxValue)
                    {
                        return RtcpPacketStatus.InvalidPacket;
                    }

                    destination[cursor++] = (byte)status.Delta250Microseconds;
                    break;
                case RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta:
                    BinaryPrimitives.WriteInt16BigEndian(destination.Slice(cursor, 2), status.Delta250Microseconds);
                    cursor += 2;
                    break;
            }
        }

        bytesWritten = paddedLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>
    /// Writes one TWCC feedback packet to a caller-provided buffer writer.
    /// </summary>
    public static void Write(in RtcpTwccFeedbackPacket feedback, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        RtcpPacketStatus validationStatus = ValidateFeedback(feedback, out int paddedLength);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP TWCC write failed with status {validationStatus}.");
        }

        Span<byte> span = destination.GetSpan(paddedLength);
        RtcpPacketStatus status = TryWrite(feedback, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP TWCC write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    private static RtcpPacketStatus ValidateFeedback(in RtcpTwccFeedbackPacket feedback, out int paddedLength)
    {
        paddedLength = 0;
        if (feedback.PacketStatuses.IsEmpty ||
            feedback.PacketStatuses.Length > ushort.MaxValue ||
            feedback.ReferenceTime64Milliseconds > 0x00FF_FFFF)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (!TryMeasure(feedback.PacketStatuses.Span, out int chunkCount, out int deltaLength))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        int unpaddedLength = checked(FeedbackHeaderLength + (chunkCount * 2) + deltaLength);
        paddedLength = RoundUpToWord(unpaddedLength);
        return RtcpPacketStatus.Success;
    }

    private static bool TryMeasure(ReadOnlySpan<RtcpTwccPacketStatus> statuses, out int chunkCount, out int deltaLength)
    {
        chunkCount = 0;
        deltaLength = 0;

        foreach (RtcpTwccPacketStatus status in statuses)
        {
            switch (status.Symbol)
            {
                case RtcpTwccPacketStatusSymbol.NotReceived:
                    break;
                case RtcpTwccPacketStatusSymbol.SmallDelta:
                    if (status.Delta250Microseconds is < 0 or > byte.MaxValue)
                    {
                        return false;
                    }

                    deltaLength++;
                    break;
                case RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta:
                    deltaLength += 2;
                    break;
                default:
                    return false;
            }
        }

        for (int i = 0; i < statuses.Length;)
        {
            RtcpTwccPacketStatusSymbol symbol = statuses[i].Symbol;
            int runLength = 1;
            while (i + runLength < statuses.Length && statuses[i + runLength].Symbol == symbol && runLength < MaximumRunLength)
            {
                runLength++;
            }

            chunkCount++;
            i += runLength;
        }

        return true;
    }

    private static int RoundUpToWord(int length)
    {
        return (length + 3) & ~3;
    }
}
