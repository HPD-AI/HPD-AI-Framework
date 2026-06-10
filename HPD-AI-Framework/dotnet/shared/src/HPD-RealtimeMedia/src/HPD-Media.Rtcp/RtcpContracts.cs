#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using HPD.Media.Diagnostics;
using HPD.Media.Rtp;

namespace HPD.Media.Rtcp;

/// <summary>
/// Identifies an RTCP packet type.
/// </summary>
public enum RtcpPacketType
{
    SenderReport = 200,
    ReceiverReport = 201,
    SourceDescription = 202,
    TransportFeedback = 205,
    PayloadFeedback = 206
}

/// <summary>
/// Classifies RTCP packet parse and write results without using exceptions for normal packet flow.
/// </summary>
public enum RtcpPacketStatus
{
    Success = 0,
    InvalidPacket = 1,
    DestinationTooSmall = 2,
    UnsupportedPacketType = 3,
    MalformedCompoundPacket = 4
}

/// <summary>
/// Represents one packet inside an RTCP compound packet.
/// </summary>
public readonly ref struct RtcpPacketView
{
    /// <summary>Initializes a new instance of the <see cref="RtcpPacketView"/> struct.</summary>
    public RtcpPacketView(RtcpPacketType packetType, ReadOnlySpan<byte> packet)
    {
        PacketType = packetType;
        Packet = packet;
    }

    /// <summary>Gets the RTCP packet type.</summary>
    public RtcpPacketType PacketType { get; }

    /// <summary>Gets the complete RTCP packet bytes.</summary>
    public ReadOnlySpan<byte> Packet { get; }
}

/// <summary>
/// Iterates RTCP compound packets without allocating.
/// </summary>
public ref struct RtcpCompoundPacketEnumerator
{
    private const int HeaderLength = 4;
    private const byte RtcpVersion = 2;

    private ReadOnlySpan<byte> remaining;
    private bool malformed;

    /// <summary>Initializes a new instance of the <see cref="RtcpCompoundPacketEnumerator"/> struct.</summary>
    public RtcpCompoundPacketEnumerator(ReadOnlySpan<byte> compoundPacket)
    {
        remaining = compoundPacket;
        malformed = false;
        Current = default;
    }

    /// <summary>Gets the current RTCP packet view.</summary>
    public RtcpPacketView Current { get; private set; }

    /// <summary>Advances to the next RTCP packet.</summary>
    public bool MoveNext()
    {
        Current = default;

        if (malformed || remaining.IsEmpty)
        {
            return false;
        }

        if (remaining.Length < HeaderLength || (remaining[0] >> 6) != RtcpVersion)
        {
            malformed = true;
            remaining = ReadOnlySpan<byte>.Empty;
            return false;
        }

        int packetLength = (BinaryPrimitives.ReadUInt16BigEndian(remaining.Slice(2, 2)) + 1) * 4;
        if (packetLength < HeaderLength || packetLength > remaining.Length)
        {
            malformed = true;
            remaining = ReadOnlySpan<byte>.Empty;
            return false;
        }

        if (!IsPaddingValidForCompoundPacket(remaining[..packetLength], isLastPacket: packetLength == remaining.Length))
        {
            malformed = true;
            remaining = ReadOnlySpan<byte>.Empty;
            return false;
        }

        var packetType = (RtcpPacketType)remaining[1];
        Current = new RtcpPacketView(packetType, remaining[..packetLength]);
        remaining = remaining[packetLength..];
        return true;
    }

    private static bool IsPaddingValidForCompoundPacket(ReadOnlySpan<byte> packet, bool isLastPacket)
    {
        bool hasPadding = (packet[0] & 0x20) != 0;
        if (!hasPadding)
        {
            return true;
        }

        if (!isLastPacket)
        {
            return false;
        }

        int paddingBytes = packet[^1];
        return paddingBytes > 0 && paddingBytes <= packet.Length - HeaderLength;
    }
}

/// <summary>
/// Represents an RTCP reception report block.
/// </summary>
public readonly struct RtcpReceptionReportBlock
{
    /// <summary>Gets the reported source SSRC.</summary>
    public required uint Ssrc { get; init; }

    /// <summary>Gets the fraction lost since the previous report.</summary>
    public required byte FractionLost { get; init; }

    /// <summary>Gets cumulative packets lost.</summary>
    public required int CumulativePacketsLost { get; init; }

    /// <summary>Gets the extended highest sequence number received.</summary>
    public required uint ExtendedHighestSequenceNumberReceived { get; init; }

    /// <summary>Gets interarrival jitter.</summary>
    public required uint InterarrivalJitter { get; init; }

    /// <summary>Gets the compact NTP timestamp from the last sender report.</summary>
    public required uint LastSenderReport { get; init; }

    /// <summary>Gets delay since the last sender report.</summary>
    public required uint DelaySinceLastSenderReport { get; init; }
}

/// <summary>
/// Represents an RTCP Sender Report.
/// </summary>
public readonly struct RtcpSenderReport
{
    /// <summary>Gets the sender SSRC.</summary>
    public required uint SenderSsrc { get; init; }

    /// <summary>Gets the NTP timestamp.</summary>
    public required ulong NtpTimestamp { get; init; }

    /// <summary>Gets the RTP timestamp.</summary>
    public required uint RtpTimestamp { get; init; }

    /// <summary>Gets the sender packet count.</summary>
    public required uint SenderPacketCount { get; init; }

    /// <summary>Gets the sender octet count.</summary>
    public required uint SenderOctetCount { get; init; }
}

/// <summary>
/// Represents an RTCP Receiver Report.
/// </summary>
public readonly struct RtcpReceiverReport
{
    /// <summary>Gets the reporter SSRC.</summary>
    public required uint ReporterSsrc { get; init; }

    /// <summary>Gets the report blocks.</summary>
    public required ReadOnlyMemory<RtcpReceptionReportBlock> Reports { get; init; }
}

/// <summary>
/// Represents an RTCP SDES item.
/// </summary>
public readonly struct RtcpSdesItem
{
    /// <summary>Gets the item type.</summary>
    public required byte Type { get; init; }

    /// <summary>Gets the UTF-8 item value.</summary>
    public required ReadOnlyMemory<byte> Utf8Value { get; init; }
}

/// <summary>
/// Represents an RTCP SDES chunk.
/// </summary>
public readonly struct RtcpSdesChunk
{
    /// <summary>Gets the chunk SSRC.</summary>
    public required uint Ssrc { get; init; }

    /// <summary>Gets the SDES items.</summary>
    public required ReadOnlyMemory<RtcpSdesItem> Items { get; init; }
}

/// <summary>
/// Represents an RTCP Source Description packet.
/// </summary>
public readonly struct RtcpSourceDescription
{
    /// <summary>Gets the source-description chunks.</summary>
    public required ReadOnlyMemory<RtcpSdesChunk> Chunks { get; init; }
}

/// <summary>
/// Represents one RTCP SDES item view over caller-owned bytes.
/// </summary>
public readonly ref struct RtcpSdesItemView
{
    /// <summary>Initializes a new instance of the <see cref="RtcpSdesItemView"/> struct.</summary>
    public RtcpSdesItemView(byte type, ReadOnlySpan<byte> utf8Value)
    {
        Type = type;
        Utf8Value = utf8Value;
    }

    /// <summary>Gets the item type.</summary>
    public byte Type { get; }

    /// <summary>Gets the UTF-8 item value.</summary>
    public ReadOnlySpan<byte> Utf8Value { get; }
}

/// <summary>
/// Iterates RTCP SDES items without allocating.
/// </summary>
public ref struct RtcpSdesItemEnumerator
{
    private ReadOnlySpan<byte> remaining;
    private bool completed;
    private bool malformed;

    /// <summary>Initializes a new instance of the <see cref="RtcpSdesItemEnumerator"/> struct.</summary>
    public RtcpSdesItemEnumerator(ReadOnlySpan<byte> itemData)
    {
        remaining = itemData;
        completed = false;
        malformed = false;
        Current = default;
    }

    /// <summary>Gets the current SDES item view.</summary>
    public RtcpSdesItemView Current { get; private set; }

    /// <summary>Gets a value indicating whether malformed item data was encountered.</summary>
    public bool IsMalformed => malformed;

    /// <summary>Advances to the next SDES item.</summary>
    public bool MoveNext()
    {
        Current = default;
        if (completed || malformed || remaining.IsEmpty)
        {
            return false;
        }

        byte itemType = remaining[0];
        remaining = remaining[1..];
        if (itemType == 0)
        {
            completed = true;
            remaining = ReadOnlySpan<byte>.Empty;
            return false;
        }

        if (remaining.IsEmpty)
        {
            malformed = true;
            return false;
        }

        int itemLength = remaining[0];
        remaining = remaining[1..];
        if (remaining.Length < itemLength)
        {
            malformed = true;
            remaining = ReadOnlySpan<byte>.Empty;
            return false;
        }

        Current = new RtcpSdesItemView(itemType, remaining[..itemLength]);
        remaining = remaining[itemLength..];
        return true;
    }
}

/// <summary>
/// Represents one RTCP SDES chunk view over caller-owned bytes.
/// </summary>
public readonly ref struct RtcpSdesChunkView
{
    private readonly ReadOnlySpan<byte> itemData;

    /// <summary>Initializes a new instance of the <see cref="RtcpSdesChunkView"/> struct.</summary>
    public RtcpSdesChunkView(uint ssrc, ReadOnlySpan<byte> itemData)
    {
        Ssrc = ssrc;
        this.itemData = itemData;
    }

    /// <summary>Gets the chunk SSRC.</summary>
    public uint Ssrc { get; }

    /// <summary>Gets a zero-allocation enumerator over SDES items.</summary>
    public RtcpSdesItemEnumerator GetItems() => new(itemData);
}

/// <summary>
/// Iterates RTCP SDES chunks without allocating.
/// </summary>
public ref struct RtcpSdesChunkEnumerator
{
    private ReadOnlySpan<byte> remaining;
    private int chunksRemaining;
    private bool malformed;

    /// <summary>Initializes a new instance of the <see cref="RtcpSdesChunkEnumerator"/> struct.</summary>
    public RtcpSdesChunkEnumerator(ReadOnlySpan<byte> packet)
    {
        remaining = packet[4..];
        chunksRemaining = packet[0] & 0x1F;
        malformed = false;
        Current = default;
    }

    /// <summary>Gets the current SDES chunk view.</summary>
    public RtcpSdesChunkView Current { get; private set; }

    /// <summary>Gets a value indicating whether malformed chunk data was encountered.</summary>
    public bool IsMalformed => malformed;

    /// <summary>Gets a value indicating whether all declared chunks consumed the packet.</summary>
    public bool IsComplete => !malformed && chunksRemaining == 0 && remaining.IsEmpty;

    /// <summary>Advances to the next SDES chunk.</summary>
    public bool MoveNext()
    {
        Current = default;
        if (malformed || chunksRemaining == 0)
        {
            return false;
        }

        if (remaining.Length < 4)
        {
            malformed = true;
            remaining = ReadOnlySpan<byte>.Empty;
            return false;
        }

        uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(remaining[..4]);
        int cursor = 4;
        int itemStart = cursor;
        while (true)
        {
            if (cursor >= remaining.Length)
            {
                malformed = true;
                remaining = ReadOnlySpan<byte>.Empty;
                return false;
            }

            byte itemType = remaining[cursor++];
            if (itemType == 0)
            {
                break;
            }

            if (cursor >= remaining.Length)
            {
                malformed = true;
                remaining = ReadOnlySpan<byte>.Empty;
                return false;
            }

            int itemLength = remaining[cursor++];
            if (cursor + itemLength > remaining.Length)
            {
                malformed = true;
                remaining = ReadOnlySpan<byte>.Empty;
                return false;
            }

            cursor += itemLength;
        }

        int paddedLength = RoundUpToWord(cursor);
        if (paddedLength > remaining.Length)
        {
            malformed = true;
            remaining = ReadOnlySpan<byte>.Empty;
            return false;
        }

        for (int i = cursor; i < paddedLength; i++)
        {
            if (remaining[i] != 0)
            {
                malformed = true;
                remaining = ReadOnlySpan<byte>.Empty;
                return false;
            }
        }

        Current = new RtcpSdesChunkView(ssrc, remaining.Slice(itemStart, cursor - itemStart));
        remaining = remaining[paddedLength..];
        chunksRemaining--;
        return true;
    }

    private static int RoundUpToWord(int value)
    {
        return (value + 3) & ~3;
    }
}

/// <summary>
/// Parses RTCP packets from caller-owned spans.
/// </summary>
public static class RtcpPacketReader
{
    private const int HeaderLength = 4;
    private const int SenderReportSenderInfoLength = 24;
    private const int ReceptionReportBlockLength = 24;
    private const byte RtcpVersion = 2;

    /// <summary>Attempts to create a compound-packet enumerator over caller-owned bytes.</summary>
    public static RtcpPacketStatus TryReadCompound(ReadOnlySpan<byte> packet, out RtcpCompoundPacketEnumerator enumerator)
    {
        enumerator = default;
        if (packet.IsEmpty)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        int cursor = 0;
        while (cursor < packet.Length)
        {
            if (packet.Length - cursor < HeaderLength || (packet[cursor] >> 6) != RtcpVersion)
            {
                return RtcpPacketStatus.MalformedCompoundPacket;
            }

            int packetLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(cursor + 2, 2)) + 1) * 4;
            if (packetLength < HeaderLength || packetLength > packet.Length - cursor)
            {
                return RtcpPacketStatus.MalformedCompoundPacket;
            }

            bool isLastPacket = cursor + packetLength == packet.Length;
            if (!IsPaddingValidForCompoundPacket(packet.Slice(cursor, packetLength), isLastPacket))
            {
                return RtcpPacketStatus.MalformedCompoundPacket;
            }

            cursor += packetLength;
        }

        enumerator = new RtcpCompoundPacketEnumerator(packet);
        return RtcpPacketStatus.Success;
    }

    /// <summary>Attempts to parse an RTCP sender report.</summary>
    public static RtcpPacketStatus TryParseSenderReport(ReadOnlySpan<byte> packet, out RtcpSenderReport senderReport)
    {
        senderReport = default;
        if (packet.Length < HeaderLength + SenderReportSenderInfoLength)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] >> 6) != RtcpVersion)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] & 0x20) != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (packet[1] != (byte)RtcpPacketType.SenderReport)
        {
            return RtcpPacketStatus.UnsupportedPacketType;
        }

        int reportCount = packet[0] & 0x1F;
        int expectedLength = HeaderLength + SenderReportSenderInfoLength + (reportCount * ReceptionReportBlockLength);
        int encodedLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)) + 1) * 4;
        if (encodedLength != packet.Length || packet.Length != expectedLength)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        senderReport = new RtcpSenderReport
        {
            SenderSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4)),
            NtpTimestamp = BinaryPrimitives.ReadUInt64BigEndian(packet.Slice(8, 8)),
            RtpTimestamp = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(16, 4)),
            SenderPacketCount = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(20, 4)),
            SenderOctetCount = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(24, 4))
        };
        return RtcpPacketStatus.Success;
    }

    /// <summary>Attempts to parse an RTCP receiver report into caller-provided report-block storage.</summary>
    public static RtcpPacketStatus TryParseReceiverReport(
        ReadOnlySpan<byte> packet,
        Memory<RtcpReceptionReportBlock> reportBuffer,
        out RtcpReceiverReport receiverReport)
    {
        receiverReport = default;
        if (packet.Length < HeaderLength + 4)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] >> 6) != RtcpVersion)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] & 0x20) != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (packet[1] != (byte)RtcpPacketType.ReceiverReport)
        {
            return RtcpPacketStatus.UnsupportedPacketType;
        }

        int reportCount = packet[0] & 0x1F;
        int expectedLength = HeaderLength + 4 + (reportCount * ReceptionReportBlockLength);
        int encodedLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)) + 1) * 4;
        if (encodedLength != packet.Length || packet.Length != expectedLength)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (reportBuffer.Length < reportCount)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        uint reporterSsrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4));
        int cursor = HeaderLength + 4;
        Span<RtcpReceptionReportBlock> reports = reportBuffer.Span;
        for (int i = 0; i < reportCount; i++)
        {
            reports[i] = new RtcpReceptionReportBlock
            {
                Ssrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(cursor, 4)),
                FractionLost = packet[cursor + 4],
                CumulativePacketsLost = ReadInt24BigEndian(packet.Slice(cursor + 5, 3)),
                ExtendedHighestSequenceNumberReceived = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(cursor + 8, 4)),
                InterarrivalJitter = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(cursor + 12, 4)),
                LastSenderReport = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(cursor + 16, 4)),
                DelaySinceLastSenderReport = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(cursor + 20, 4))
            };
            cursor += ReceptionReportBlockLength;
        }

        receiverReport = new RtcpReceiverReport
        {
            ReporterSsrc = reporterSsrc,
            Reports = reportBuffer[..reportCount]
        };
        return RtcpPacketStatus.Success;
    }

    /// <summary>Attempts to parse an RTCP source-description packet.</summary>
    public static RtcpPacketStatus TryReadSourceDescription(
        ReadOnlySpan<byte> packet,
        out RtcpSdesChunkEnumerator enumerator)
    {
        enumerator = default;
        if (packet.Length < HeaderLength || (packet[0] >> 6) != RtcpVersion)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] & 0x20) != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (packet[1] != (byte)RtcpPacketType.SourceDescription)
        {
            return RtcpPacketStatus.UnsupportedPacketType;
        }

        if ((packet[0] & 0x1F) == 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        int encodedLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)) + 1) * 4;
        if (encodedLength != packet.Length)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        var validator = new RtcpSdesChunkEnumerator(packet);
        while (validator.MoveNext())
        {
        }

        if (validator.IsMalformed || !validator.IsComplete)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        enumerator = new RtcpSdesChunkEnumerator(packet);
        return RtcpPacketStatus.Success;
    }

    /// <summary>Attempts to parse an RTCP source-description packet into retained values.</summary>
    public static RtcpPacketStatus TryParseSourceDescription(
        ReadOnlySpan<byte> packet,
        Span<RtcpSdesChunk> chunkBuffer,
        Span<RtcpSdesItem> itemBuffer,
        out RtcpSourceDescription sourceDescription)
    {
        sourceDescription = default;
        if (packet.Length < HeaderLength || (packet[0] >> 6) != RtcpVersion)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if ((packet[0] & 0x20) != 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (packet[1] != (byte)RtcpPacketType.SourceDescription)
        {
            return RtcpPacketStatus.UnsupportedPacketType;
        }

        int chunkCount = packet[0] & 0x1F;
        if (chunkCount == 0)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        int encodedLength = (BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)) + 1) * 4;
        if (encodedLength != packet.Length || chunkBuffer.Length < chunkCount)
        {
            return encodedLength == packet.Length ? RtcpPacketStatus.DestinationTooSmall : RtcpPacketStatus.InvalidPacket;
        }

        int cursor = HeaderLength;
        int itemCount = 0;
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            if (cursor + 4 > packet.Length)
            {
                return RtcpPacketStatus.InvalidPacket;
            }

            int chunkStart = cursor;
            uint ssrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(cursor, 4));
            cursor += 4;
            int firstItemIndex = itemCount;

            while (true)
            {
                if (cursor >= packet.Length)
                {
                    return RtcpPacketStatus.InvalidPacket;
                }

                byte itemType = packet[cursor++];
                if (itemType == 0)
                {
                    break;
                }

                if (cursor >= packet.Length)
                {
                    return RtcpPacketStatus.InvalidPacket;
                }

                int itemLength = packet[cursor++];
                if (cursor + itemLength > packet.Length)
                {
                    return RtcpPacketStatus.InvalidPacket;
                }

                if (itemCount >= itemBuffer.Length)
                {
                    return RtcpPacketStatus.DestinationTooSmall;
                }

                itemBuffer[itemCount++] = new RtcpSdesItem
                {
                    Type = itemType,
                    Utf8Value = packet.Slice(cursor, itemLength).ToArray()
                };
                cursor += itemLength;
            }

            int paddedChunkLength = RoundUpToWord(cursor - chunkStart);
            int paddedChunkEnd = chunkStart + paddedChunkLength;
            if (paddedChunkEnd > packet.Length)
            {
                return RtcpPacketStatus.InvalidPacket;
            }

            while (cursor < paddedChunkEnd)
            {
                if (packet[cursor++] != 0)
                {
                    return RtcpPacketStatus.InvalidPacket;
                }
            }

            chunkBuffer[chunkIndex] = new RtcpSdesChunk
            {
                Ssrc = ssrc,
                Items = itemBuffer[firstItemIndex..itemCount].ToArray()
            };
        }

        if (cursor != packet.Length)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        sourceDescription = new RtcpSourceDescription
        {
            Chunks = chunkBuffer[..chunkCount].ToArray()
        };
        return RtcpPacketStatus.Success;
    }

    private static int RoundUpToWord(int value)
    {
        return (value + 3) & ~3;
    }

    private static bool IsPaddingValidForCompoundPacket(ReadOnlySpan<byte> packet, bool isLastPacket)
    {
        bool hasPadding = (packet[0] & 0x20) != 0;
        if (!hasPadding)
        {
            return true;
        }

        if (!isLastPacket)
        {
            return false;
        }

        int paddingBytes = packet[^1];
        return paddingBytes > 0 && paddingBytes <= packet.Length - HeaderLength;
    }

    private static int ReadInt24BigEndian(ReadOnlySpan<byte> source)
    {
        int value = (source[0] << 16) | (source[1] << 8) | source[2];
        return (value & 0x80_0000) == 0 ? value : value | unchecked((int)0xFF00_0000);
    }
}

/// <summary>
/// Writes RTCP packets to caller-owned storage.
/// </summary>
public static class RtcpPacketWriter
{
    private const int HeaderLength = 4;
    private const int MaximumPacketLength = (ushort.MaxValue + 1) * 4;
    private const int SenderReportFixedLength = 28;
    private const int ReceiverReportFixedLength = 8;
    private const int ReceptionReportBlockLength = 24;
    private const byte RtcpVersion = 2;

    /// <summary>Attempts to write an RTCP sender report without reception report blocks.</summary>
    public static RtcpPacketStatus TryWriteSenderReport(
        in RtcpSenderReport senderReport,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (destination.Length < SenderReportFixedLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        destination[0] = RtcpVersion << 6;
        destination[1] = (byte)RtcpPacketType.SenderReport;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), checked((ushort)((SenderReportFixedLength / 4) - 1)));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), senderReport.SenderSsrc);
        BinaryPrimitives.WriteUInt64BigEndian(destination.Slice(8, 8), senderReport.NtpTimestamp);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(16, 4), senderReport.RtpTimestamp);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(20, 4), senderReport.SenderPacketCount);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(24, 4), senderReport.SenderOctetCount);
        bytesWritten = SenderReportFixedLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>Writes an RTCP sender report without reception report blocks to a buffer writer.</summary>
    public static void WriteSenderReport(in RtcpSenderReport senderReport, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        Span<byte> span = destination.GetSpan(SenderReportFixedLength);
        RtcpPacketStatus status = TryWriteSenderReport(senderReport, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP sender report write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>Attempts to write an RTCP receiver report.</summary>
    public static RtcpPacketStatus TryWriteReceiverReport(
        in RtcpReceiverReport receiverReport,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;

        RtcpPacketStatus validationStatus = ValidateReceiverReport(receiverReport, out int requiredLength);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            return validationStatus;
        }

        if (destination.Length < requiredLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        destination[0] = (byte)((RtcpVersion << 6) | receiverReport.Reports.Length);
        destination[1] = (byte)RtcpPacketType.ReceiverReport;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), checked((ushort)((requiredLength / 4) - 1)));
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), receiverReport.ReporterSsrc);

        int cursor = ReceiverReportFixedLength;
        foreach (RtcpReceptionReportBlock report in receiverReport.Reports.Span)
        {
            if (report.CumulativePacketsLost is < -8_388_608 or > 8_388_607)
            {
                bytesWritten = 0;
                return RtcpPacketStatus.InvalidPacket;
            }

            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor, 4), report.Ssrc);
            destination[cursor + 4] = report.FractionLost;
            WriteInt24BigEndian(report.CumulativePacketsLost, destination.Slice(cursor + 5, 3));
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor + 8, 4), report.ExtendedHighestSequenceNumberReceived);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor + 12, 4), report.InterarrivalJitter);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor + 16, 4), report.LastSenderReport);
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor + 20, 4), report.DelaySinceLastSenderReport);
            cursor += ReceptionReportBlockLength;
        }

        bytesWritten = requiredLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>Writes an RTCP receiver report to a buffer writer.</summary>
    public static void WriteReceiverReport(in RtcpReceiverReport receiverReport, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        RtcpPacketStatus validationStatus = ValidateReceiverReport(receiverReport, out int requiredLength);
        if (validationStatus != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP receiver report write failed with status {validationStatus}.");
        }

        Span<byte> span = destination.GetSpan(requiredLength);
        RtcpPacketStatus status = TryWriteReceiverReport(receiverReport, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP receiver report write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>Attempts to write an RTCP source-description packet.</summary>
    public static RtcpPacketStatus TryWriteSourceDescription(
        in RtcpSourceDescription sourceDescription,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (sourceDescription.Chunks.Length is 0 or > 31)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (!TryCalculateSourceDescriptionLength(sourceDescription.Chunks.Span, out int requiredLength))
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        if (destination.Length < requiredLength)
        {
            return RtcpPacketStatus.DestinationTooSmall;
        }

        destination[..requiredLength].Clear();
        destination[0] = (byte)((RtcpVersion << 6) | sourceDescription.Chunks.Length);
        destination[1] = (byte)RtcpPacketType.SourceDescription;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), checked((ushort)((requiredLength / 4) - 1)));

        int cursor = HeaderLength;
        foreach (RtcpSdesChunk chunk in sourceDescription.Chunks.Span)
        {
            int chunkStart = cursor;
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor, 4), chunk.Ssrc);
            cursor += 4;

            foreach (RtcpSdesItem item in chunk.Items.Span)
            {
                if (item.Type == 0 || item.Utf8Value.Length > byte.MaxValue)
                {
                    bytesWritten = 0;
                    return RtcpPacketStatus.InvalidPacket;
                }

                destination[cursor++] = item.Type;
                destination[cursor++] = checked((byte)item.Utf8Value.Length);
                item.Utf8Value.Span.CopyTo(destination[cursor..]);
                cursor += item.Utf8Value.Length;
            }

            destination[cursor++] = 0;
            cursor = chunkStart + RoundUpToWord(cursor - chunkStart);
        }

        bytesWritten = requiredLength;
        return RtcpPacketStatus.Success;
    }

    /// <summary>Writes an RTCP source-description packet to a buffer writer.</summary>
    public static void WriteSourceDescription(in RtcpSourceDescription sourceDescription, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (sourceDescription.Chunks.Length is 0 or > 31)
        {
            throw new InvalidOperationException($"RTCP source-description write failed with status {RtcpPacketStatus.InvalidPacket}.");
        }

        if (!TryCalculateSourceDescriptionLength(sourceDescription.Chunks.Span, out int requiredLength))
        {
            throw new InvalidOperationException("RTCP source-description length calculation failed.");
        }

        Span<byte> span = destination.GetSpan(requiredLength);
        RtcpPacketStatus status = TryWriteSourceDescription(sourceDescription, span, out int bytesWritten);
        if (status != RtcpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTCP source-description write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    private static RtcpPacketStatus ValidateReceiverReport(in RtcpReceiverReport receiverReport, out int requiredLength)
    {
        requiredLength = 0;
        if (receiverReport.Reports.Length > 31)
        {
            return RtcpPacketStatus.InvalidPacket;
        }

        foreach (RtcpReceptionReportBlock report in receiverReport.Reports.Span)
        {
            if (report.CumulativePacketsLost is < -8_388_608 or > 8_388_607)
            {
                return RtcpPacketStatus.InvalidPacket;
            }
        }

        requiredLength = ReceiverReportFixedLength + (receiverReport.Reports.Length * ReceptionReportBlockLength);
        return RtcpPacketStatus.Success;
    }

    private static void WriteInt24BigEndian(int value, Span<byte> destination)
    {
        destination[0] = (byte)((value >> 16) & 0xFF);
        destination[1] = (byte)((value >> 8) & 0xFF);
        destination[2] = (byte)(value & 0xFF);
    }

    private static bool TryCalculateSourceDescriptionLength(ReadOnlySpan<RtcpSdesChunk> chunks, out int length)
    {
        length = HeaderLength;
        if (chunks.IsEmpty)
        {
            length = 0;
            return false;
        }

        foreach (RtcpSdesChunk chunk in chunks)
        {
            int chunkLength = 4 + 1;
            foreach (RtcpSdesItem item in chunk.Items.Span)
            {
                if (item.Type == 0 || item.Utf8Value.Length > byte.MaxValue)
                {
                    length = 0;
                    return false;
                }

                chunkLength = checked(chunkLength + 2 + item.Utf8Value.Length);
                if (chunkLength > MaximumPacketLength)
                {
                    length = 0;
                    return false;
                }
            }

            length = checked(length + RoundUpToWord(chunkLength));
            if (length > MaximumPacketLength)
            {
                length = 0;
                return false;
            }
        }

        return length % 4 == 0;
    }

    private static int RoundUpToWord(int value)
    {
        return (value + 3) & ~3;
    }
}

/// <summary>
/// Tracks RTP reception state and creates RTCP receiver reports.
/// </summary>
public interface IRtcpReceptionReporter
{
    /// <summary>Observes a received RTP packet.</summary>
    void OnRtpPacket(in RtpPacket packet, uint arrivalTimestampInRtpClock);

    /// <summary>Observes a received RTP packet without requiring retained packet memory.</summary>
    void OnRtpPacket(in RtpPacketView packet, uint arrivalTimestampInRtpClock);

    /// <summary>Observes a received RTCP sender report.</summary>
    void OnSenderReport(in RtcpSenderReport senderReport, DateTimeOffset receivedAt);

    /// <summary>Creates a receiver report for a remote RTP source.</summary>
    bool TryCreateReceiverReport(
        uint localSsrc,
        uint remoteSsrc,
        DateTimeOffset now,
        Memory<RtcpReceptionReportBlock> reportBuffer,
        out RtcpReceiverReport receiverReport);
}

/// <summary>
/// Tracks reception statistics for one remote RTP source.
/// </summary>
public sealed class RtcpReceptionReporter : IRtcpReceptionReporter
{
    private const int SequenceModulus = 1 << 16;
    private const double NtpFractionUnitsPerSecond = 65536.0;

    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;
    private bool initialized;
    private uint remoteSsrc;
    private ushort baseSequenceNumber;
    private ushort maxSequenceNumber;
    private uint sequenceCycles;
    private ulong receivedSequenceBitmap;
    private uint packetsReceived;
    private uint expectedAtLastReport;
    private uint receivedAtLastReport;
    private bool hasTransit;
    private long previousTransit;
    private double jitter;
    private bool hasLastSenderReport;
    private uint lastSenderReportSsrc;
    private uint lastSenderReport;
    private DateTimeOffset lastSenderReportReceivedAt;

    /// <summary>
    /// Initializes a new instance of the <see cref="RtcpReceptionReporter"/> class.
    /// </summary>
    public RtcpReceptionReporter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RtcpReceptionReporter"/> class with cached telemetry emitters.
    /// </summary>
    public RtcpReceptionReporter(RealtimeMediaTelemetryEmitters telemetry)
    {
        this.telemetry = telemetry;
        hasTelemetry = true;
    }

    /// <inheritdoc />
    public void OnRtpPacket(in RtpPacket packet, uint arrivalTimestampInRtpClock)
    {
        OnRtpPacket(packet.Header.Ssrc, packet.Header.SequenceNumber, packet.Header.Timestamp, arrivalTimestampInRtpClock);
    }

    /// <inheritdoc />
    public void OnRtpPacket(in RtpPacketView packet, uint arrivalTimestampInRtpClock)
    {
        OnRtpPacket(packet.Header.Ssrc, packet.Header.SequenceNumber, packet.Header.Timestamp, arrivalTimestampInRtpClock);
    }

    /// <inheritdoc />
    public void OnSenderReport(in RtcpSenderReport senderReport, DateTimeOffset receivedAt)
    {
        if (initialized && senderReport.SenderSsrc != remoteSsrc)
        {
            return;
        }

        hasLastSenderReport = true;
        lastSenderReportSsrc = senderReport.SenderSsrc;
        lastSenderReport = CompactNtp(senderReport.NtpTimestamp);
        lastSenderReportReceivedAt = receivedAt;
    }

    /// <inheritdoc />
    public bool TryCreateReceiverReport(
        uint localSsrc,
        uint remoteSsrc,
        DateTimeOffset now,
        Memory<RtcpReceptionReportBlock> reportBuffer,
        out RtcpReceiverReport receiverReport)
    {
        receiverReport = default;
        if (!initialized || remoteSsrc != this.remoteSsrc || reportBuffer.IsEmpty)
        {
            return false;
        }

        uint extendedHighest = sequenceCycles + maxSequenceNumber;
        uint expected = extendedHighest - baseSequenceNumber + 1;
        uint expectedInterval = expected - expectedAtLastReport;
        uint receivedInterval = packetsReceived - receivedAtLastReport;
        int lostInterval = checked((int)expectedInterval - (int)receivedInterval);
        int cumulativeLost = checked((int)expected - (int)packetsReceived);
        byte fractionLost = 0;
        if (expectedInterval != 0 && lostInterval > 0)
        {
            fractionLost = (byte)Math.Min(255, (lostInterval << 8) / expectedInterval);
        }

        var block = new RtcpReceptionReportBlock
        {
            Ssrc = remoteSsrc,
            FractionLost = fractionLost,
            CumulativePacketsLost = cumulativeLost,
            ExtendedHighestSequenceNumberReceived = extendedHighest,
            InterarrivalJitter = (uint)jitter,
            LastSenderReport = GetLastSenderReport(remoteSsrc),
            DelaySinceLastSenderReport = CalculateDelaySinceLastSenderReport(remoteSsrc, now)
        };

        reportBuffer.Span[0] = block;
        receiverReport = new RtcpReceiverReport
        {
            ReporterSsrc = localSsrc,
            Reports = reportBuffer[..1]
        };

        expectedAtLastReport = expected;
        receivedAtLastReport = packetsReceived;
        EmitJitter(localSsrc, remoteSsrc, block.InterarrivalJitter);
        return true;
    }

    private void OnRtpPacket(uint ssrc, ushort sequenceNumber, uint rtpTimestamp, uint arrivalTimestampInRtpClock)
    {
        if (!initialized)
        {
            initialized = true;
            remoteSsrc = ssrc;
            baseSequenceNumber = sequenceNumber;
            maxSequenceNumber = sequenceNumber;
            receivedSequenceBitmap = 1;
        }
        else if (ssrc != remoteSsrc)
        {
            return;
        }
        else if (!TryMarkSequenceReceived(sequenceNumber))
        {
            return;
        }

        packetsReceived++;
        long transit = (long)arrivalTimestampInRtpClock - rtpTimestamp;
        if (hasTransit)
        {
            long delta = Math.Abs(transit - previousTransit);
            jitter += (delta - jitter) / 16.0;
        }

        previousTransit = transit;
        hasTransit = true;
    }

    private bool TryMarkSequenceReceived(ushort sequenceNumber)
    {
        if (IsNewer(sequenceNumber, maxSequenceNumber))
        {
            int advance = (ushort)(sequenceNumber - maxSequenceNumber);
            if (sequenceNumber < maxSequenceNumber)
            {
                sequenceCycles += SequenceModulus;
            }

            receivedSequenceBitmap = advance >= 64
                ? 1
                : (receivedSequenceBitmap << advance) | 1;
            maxSequenceNumber = sequenceNumber;
            return true;
        }

        int age = (ushort)(maxSequenceNumber - sequenceNumber);
        if (age >= 64)
        {
            return false;
        }

        ulong mask = 1UL << age;
        if ((receivedSequenceBitmap & mask) != 0)
        {
            return false;
        }

        receivedSequenceBitmap |= mask;
        return true;
    }

    private uint GetLastSenderReport(uint reportRemoteSsrc)
    {
        return hasLastSenderReport && lastSenderReportSsrc == reportRemoteSsrc
            ? lastSenderReport
            : 0;
    }

    private uint CalculateDelaySinceLastSenderReport(uint reportRemoteSsrc, DateTimeOffset now)
    {
        if (!hasLastSenderReport || lastSenderReportSsrc != reportRemoteSsrc || lastSenderReport == 0)
        {
            return 0;
        }

        TimeSpan delay = now - lastSenderReportReceivedAt;
        if (delay <= TimeSpan.Zero)
        {
            return 0;
        }

        double units = delay.TotalSeconds * NtpFractionUnitsPerSecond;
        return units >= uint.MaxValue ? uint.MaxValue : (uint)units;
    }

    private static uint CompactNtp(ulong ntpTimestamp)
    {
        return (uint)((ntpTimestamp >> 16) & 0xFFFFFFFF);
    }

    private void EmitJitter(uint reporterSsrc, uint remoteSsrc, uint interarrivalJitter)
    {
        if (!hasTelemetry)
        {
            return;
        }

        _ = telemetry.RtcpJitter.Emit(new RtcpJitterSample
        {
            ReporterSsrc = reporterSsrc,
            RemoteSsrc = remoteSsrc,
            InterarrivalJitter = interarrivalJitter
        });
    }

    private static bool IsNewer(ushort sequenceNumber, ushort reference)
    {
        return sequenceNumber != reference && (ushort)(sequenceNumber - reference) < 0x8000;
    }
}
