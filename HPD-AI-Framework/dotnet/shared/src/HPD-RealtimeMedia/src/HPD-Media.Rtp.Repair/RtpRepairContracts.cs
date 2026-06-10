#nullable enable

using System.Buffers.Binary;
using HPD.Media.Rtcp.Feedback;
using HPD.Media.Rtp;

namespace HPD.Media.Rtp.Repair;

/// <summary>
/// Classifies RTP repair operations without exceptions for normal packet flow.
/// </summary>
public enum RtpRepairStatus
{
    /// <summary>The repair operation succeeded.</summary>
    Success = 0,

    /// <summary>The supplied packet or payload is malformed.</summary>
    InvalidPacket = 1,

    /// <summary>The destination buffer is too small.</summary>
    DestinationTooSmall = 2,

    /// <summary>The packet does not match the configured RTX payload type or SSRC.</summary>
    MappingMismatch = 3
}

/// <summary>
/// Describes how an RTX stream maps back to its protected media stream.
/// </summary>
public readonly struct RtpRtxRepairMapping
{
    /// <summary>Gets the media stream SSRC.</summary>
    public required uint MediaSsrc { get; init; }

    /// <summary>Gets the RTX stream SSRC.</summary>
    public required uint RtxSsrc { get; init; }

    /// <summary>Gets the media payload type restored after repair.</summary>
    public required byte MediaPayloadType { get; init; }

    /// <summary>Gets the RTX payload type received on the retransmission stream.</summary>
    public required byte RtxPayloadType { get; init; }
}

/// <summary>
/// Represents one RTP repair request derived from RTCP feedback.
/// </summary>
public readonly struct RtpRepairRequest
{
    /// <summary>Gets the media SSRC whose packet is requested.</summary>
    public required uint MediaSsrc { get; init; }

    /// <summary>Gets the missing media RTP sequence number.</summary>
    public required ushort SequenceNumber { get; init; }
}

/// <summary>
/// Receives RTP repair requests without requiring collection allocation.
/// </summary>
public interface IRtpRepairRequestSink
{
    /// <summary>Attempts to accept one RTP repair request.</summary>
    bool TryWrite(in RtpRepairRequest request);
}

/// <summary>
/// Expands RTCP NACK entries into individual RTP repair requests.
/// </summary>
public static class RtpRepairRequestReader
{
    /// <summary>
    /// Writes one repair request for each sequence number represented by a Generic NACK packet.
    /// </summary>
    public static bool TryWriteRequests(in RtcpNackPacket nack, IRtpRepairRequestSink sink)
    {
        foreach (RtcpNackEntry entry in nack.Entries.Span)
        {
            if (!sink.TryWrite(new RtpRepairRequest
                {
                    MediaSsrc = nack.MediaSsrc,
                    SequenceNumber = entry.PacketId
                }))
            {
                return false;
            }

            for (int bit = 0; bit < 16; bit++)
            {
                if (((entry.LostPacketBitmask >> bit) & 1) == 0)
                {
                    continue;
                }

                if (!sink.TryWrite(new RtpRepairRequest
                    {
                        MediaSsrc = nack.MediaSsrc,
                        SequenceNumber = (ushort)(entry.PacketId + bit + 1)
                    }))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

/// <summary>
/// Coalesces RTP repair requests into RTCP Generic NACK feedback entries.
/// </summary>
public static class RtpRepairRequestWriter
{
    /// <summary>
    /// Writes sequence-sorted repair requests into caller-provided Generic NACK entry storage.
    /// </summary>
    public static RtpRepairStatus TryWriteNackEntries(
        uint mediaSsrc,
        ReadOnlySpan<RtpRepairRequest> requests,
        Span<RtcpNackEntry> destination,
        out int entriesWritten)
    {
        entriesWritten = 0;
        if (requests.IsEmpty)
        {
            return RtpRepairStatus.Success;
        }

        RtpRepairStatus validationStatus = TryMeasureNackEntries(mediaSsrc, requests, out int requiredEntries);
        if (validationStatus != RtpRepairStatus.Success)
        {
            return validationStatus;
        }

        if (destination.Length < requiredEntries)
        {
            return RtpRepairStatus.DestinationTooSmall;
        }

        ushort packetId = requests[0].SequenceNumber;
        ushort bitmask = 0;
        for (int i = 1; i < requests.Length; i++)
        {
            RtpRepairRequest request = requests[i];
            if (request.MediaSsrc != mediaSsrc)
            {
                entriesWritten = 0;
                return RtpRepairStatus.MappingMismatch;
            }

            int delta = (ushort)(request.SequenceNumber - packetId);
            if (delta == 0 || (delta <= 16 && ((bitmask >> (delta - 1)) & 1) != 0))
            {
                continue;
            }

            if (delta is >= 1 and <= 16)
            {
                bitmask |= (ushort)(1 << (delta - 1));
                continue;
            }

            AppendEntry(packetId, bitmask, destination, ref entriesWritten);

            packetId = request.SequenceNumber;
            bitmask = 0;
        }

        AppendEntry(packetId, bitmask, destination, ref entriesWritten);
        return RtpRepairStatus.Success;
    }

    private static RtpRepairStatus TryMeasureNackEntries(
        uint mediaSsrc,
        ReadOnlySpan<RtpRepairRequest> requests,
        out int requiredEntries)
    {
        requiredEntries = 0;
        if (requests[0].MediaSsrc != mediaSsrc)
        {
            return RtpRepairStatus.MappingMismatch;
        }

        ushort packetId = requests[0].SequenceNumber;
        ushort bitmask = 0;
        for (int i = 1; i < requests.Length; i++)
        {
            RtpRepairRequest request = requests[i];
            if (request.MediaSsrc != mediaSsrc)
            {
                requiredEntries = 0;
                return RtpRepairStatus.MappingMismatch;
            }

            int delta = (ushort)(request.SequenceNumber - packetId);
            if (delta == 0 || (delta <= 16 && ((bitmask >> (delta - 1)) & 1) != 0))
            {
                continue;
            }

            if (delta is >= 1 and <= 16)
            {
                bitmask |= (ushort)(1 << (delta - 1));
                continue;
            }

            if (delta >= 0x8000)
            {
                requiredEntries = 0;
                return RtpRepairStatus.InvalidPacket;
            }

            requiredEntries++;
            packetId = request.SequenceNumber;
            bitmask = 0;
        }

        requiredEntries++;
        return RtpRepairStatus.Success;
    }

    private static void AppendEntry(
        ushort packetId,
        ushort bitmask,
        Span<RtcpNackEntry> destination,
        ref int entriesWritten)
    {
        destination[entriesWritten++] = new RtcpNackEntry
        {
            PacketId = packetId,
            LostPacketBitmask = bitmask
        };
    }
}

/// <summary>
/// Reads and writes RFC 4588 RTX payloads.
/// </summary>
public static class RtpRtxPayload
{
    /// <summary>
    /// Attempts to read the original RTP sequence number and original payload from an RTX payload.
    /// </summary>
    public static RtpRepairStatus TryRead(
        ReadOnlySpan<byte> rtxPayload,
        out ushort originalSequenceNumber,
        out ReadOnlySpan<byte> originalPayload)
    {
        originalSequenceNumber = 0;
        originalPayload = default;
        if (rtxPayload.Length < 2)
        {
            return RtpRepairStatus.InvalidPacket;
        }

        originalSequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(rtxPayload[..2]);
        originalPayload = rtxPayload[2..];
        return RtpRepairStatus.Success;
    }

    /// <summary>
    /// Attempts to write an RTX payload into caller-provided storage.
    /// </summary>
    public static RtpRepairStatus TryWrite(
        ushort originalSequenceNumber,
        ReadOnlySpan<byte> originalPayload,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;
        if (destination.Length < 2 || originalPayload.Length > destination.Length - 2)
        {
            return RtpRepairStatus.DestinationTooSmall;
        }

        int requiredLength = 2 + originalPayload.Length;
        BinaryPrimitives.WriteUInt16BigEndian(destination[..2], originalSequenceNumber);
        originalPayload.CopyTo(destination[2..requiredLength]);
        bytesWritten = requiredLength;
        return RtpRepairStatus.Success;
    }
}

/// <summary>
/// Creates RTX packets from original media RTP packets using caller-owned payload storage.
/// </summary>
public static class RtpRtxPacketizer
{
    private const byte MaximumRtpPayloadType = 127;
    private const int MaximumExtensionDataBytes = ushort.MaxValue * 4;

    /// <summary>
    /// Creates one RTX packet whose payload points at <paramref name="destinationPayload"/>.
    /// </summary>
    public static RtpRepairStatus TryPacketize(
        in RtpPacket originalPacket,
        in RtpRtxRepairMapping mapping,
        ushort rtxSequenceNumber,
        uint rtxTimestamp,
        Memory<byte> destinationPayload,
        out RtpPacket rtxPacket)
    {
        rtxPacket = default;
        if (!IsValidMapping(mapping))
        {
            return RtpRepairStatus.InvalidPacket;
        }

        if (!IsValidRetainedRtpPacket(originalPacket))
        {
            return RtpRepairStatus.InvalidPacket;
        }

        if (originalPacket.Header.PayloadType != mapping.MediaPayloadType || originalPacket.Header.Ssrc != mapping.MediaSsrc)
        {
            return RtpRepairStatus.MappingMismatch;
        }

        RtpRepairStatus status = RtpRtxPayload.TryWrite(
            originalPacket.Header.SequenceNumber,
            originalPacket.Payload.Span,
            destinationPayload.Span,
            out int bytesWritten);
        if (status != RtpRepairStatus.Success)
        {
            return status;
        }

        rtxPacket = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = mapping.RtxPayloadType,
                SequenceNumber = rtxSequenceNumber,
                Timestamp = rtxTimestamp,
                Ssrc = mapping.RtxSsrc,
                Marker = originalPacket.Header.Marker
            },
            Payload = destinationPayload[..bytesWritten],
            ArrivalTime = originalPacket.ArrivalTime
        };
        return RtpRepairStatus.Success;
    }

    private static bool IsValidMapping(in RtpRtxRepairMapping mapping)
    {
        return mapping.MediaPayloadType <= MaximumRtpPayloadType &&
            mapping.RtxPayloadType <= MaximumRtpPayloadType;
    }

    private static bool IsValidRetainedRtpPacket(in RtpPacket packet)
    {
        if (packet.Header.PayloadType > MaximumRtpPayloadType || packet.Header.Padding)
        {
            return false;
        }

        int csrcCount = packet.Csrcs.Length;
        if (csrcCount > 15 || packet.Header.CsrcCount != csrcCount)
        {
            return false;
        }

        bool hasExtension = packet.Header.ExtensionProfile != 0 || !packet.ExtensionData.IsEmpty;
        if (!hasExtension)
        {
            return true;
        }

        return packet.Header.ExtensionProfile != 0 &&
            packet.ExtensionData.Length % 4 == 0 &&
            packet.ExtensionData.Length <= MaximumExtensionDataBytes &&
            (packet.Header.ExtensionProfile != RtpHeaderExtensionEnumerator.OneByteHeaderProfile ||
             IsValidOneByteHeaderExtensionBlock(packet.ExtensionData.Span));
    }

    private static bool IsValidOneByteHeaderExtensionBlock(ReadOnlySpan<byte> extensionData)
    {
        while (!extensionData.IsEmpty)
        {
            byte descriptor = extensionData[0];
            extensionData = extensionData[1..];

            if (descriptor == 0)
            {
                continue;
            }

            int id = descriptor >> 4;
            if (id == 15)
            {
                return false;
            }

            int length = (descriptor & 0x0F) + 1;
            if (extensionData.Length < length)
            {
                return false;
            }

            extensionData = extensionData[length..];
        }

        return true;
    }
}

/// <summary>
/// Repairs RFC 4588 RTX packets back into media RTP packets.
/// </summary>
public static class RtpRtxRepairer
{
    private const byte MaximumRtpPayloadType = 127;
    private const int MaximumExtensionDataBytes = ushort.MaxValue * 4;

    /// <summary>
    /// Restores one RTX packet to its media RTP shape.
    /// </summary>
    public static RtpRepairStatus TryRepair(
        in RtpPacket rtxPacket,
        in RtpRtxRepairMapping mapping,
        uint originalTimestamp,
        out RtpPacket repairedPacket)
    {
        repairedPacket = default;
        if (!IsValidMapping(mapping))
        {
            return RtpRepairStatus.InvalidPacket;
        }

        if (!IsValidRetainedRtpPacket(rtxPacket))
        {
            return RtpRepairStatus.InvalidPacket;
        }

        if (rtxPacket.Header.PayloadType != mapping.RtxPayloadType || rtxPacket.Header.Ssrc != mapping.RtxSsrc)
        {
            return RtpRepairStatus.MappingMismatch;
        }

        RtpRepairStatus status = RtpRtxPayload.TryRead(
            rtxPacket.Payload.Span,
            out ushort originalSequenceNumber,
            out ReadOnlySpan<byte> originalPayload);
        if (status != RtpRepairStatus.Success)
        {
            return status;
        }

        repairedPacket = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = mapping.MediaPayloadType,
                SequenceNumber = originalSequenceNumber,
                Timestamp = originalTimestamp,
                Ssrc = mapping.MediaSsrc,
                Marker = rtxPacket.Header.Marker
            },
            Payload = rtxPacket.Payload.Slice(2, originalPayload.Length),
            ArrivalTime = rtxPacket.ArrivalTime
        };
        return RtpRepairStatus.Success;
    }

    private static bool IsValidMapping(in RtpRtxRepairMapping mapping)
    {
        return mapping.MediaPayloadType <= MaximumRtpPayloadType &&
            mapping.RtxPayloadType <= MaximumRtpPayloadType;
    }

    private static bool IsValidRetainedRtpPacket(in RtpPacket packet)
    {
        if (packet.Header.PayloadType > MaximumRtpPayloadType || packet.Header.Padding)
        {
            return false;
        }

        int csrcCount = packet.Csrcs.Length;
        if (csrcCount > 15 || packet.Header.CsrcCount != csrcCount)
        {
            return false;
        }

        bool hasExtension = packet.Header.ExtensionProfile != 0 || !packet.ExtensionData.IsEmpty;
        if (!hasExtension)
        {
            return true;
        }

        return packet.Header.ExtensionProfile != 0 &&
            packet.ExtensionData.Length % 4 == 0 &&
            packet.ExtensionData.Length <= MaximumExtensionDataBytes &&
            (packet.Header.ExtensionProfile != RtpHeaderExtensionEnumerator.OneByteHeaderProfile ||
             IsValidOneByteHeaderExtensionBlock(packet.ExtensionData.Span));
    }

    private static bool IsValidOneByteHeaderExtensionBlock(ReadOnlySpan<byte> extensionData)
    {
        while (!extensionData.IsEmpty)
        {
            byte descriptor = extensionData[0];
            extensionData = extensionData[1..];

            if (descriptor == 0)
            {
                continue;
            }

            int id = descriptor >> 4;
            if (id == 15)
            {
                return false;
            }

            int length = (descriptor & 0x0F) + 1;
            if (extensionData.Length < length)
            {
                return false;
            }

            extensionData = extensionData[length..];
        }

        return true;
    }
}
