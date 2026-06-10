#nullable enable

using HPD.Media.Rtcp.Feedback;
using HPD.Media.Rtp;
using HPD.Media.Rtp.Repair;

namespace HPD.Media.Rtp.Repair.Tests.Vectors;

public sealed class RtpRepairTests
{
    [Fact]
    public void RtxPayload_WritesAndReadsOriginalSequenceNumber()
    {
        Span<byte> destination = stackalloc byte[8];
        ReadOnlySpan<byte> originalPayload = [0xAA, 0xBB, 0xCC];

        RtpRepairStatus writeStatus = RtpRtxPayload.TryWrite(0x1234, originalPayload, destination, out int bytesWritten);
        RtpRepairStatus readStatus = RtpRtxPayload.TryRead(destination[..bytesWritten], out ushort osn, out ReadOnlySpan<byte> restoredPayload);

        Assert.Equal(RtpRepairStatus.Success, writeStatus);
        Assert.Equal(5, bytesWritten);
        Assert.Equal(RtpRepairStatus.Success, readStatus);
        Assert.Equal(0x1234, osn);
        Assert.True(restoredPayload.SequenceEqual(originalPayload));
    }

    [Fact]
    public void RtxPayload_ReturnsDestinationTooSmall()
    {
        Span<byte> destination = stackalloc byte[3];

        RtpRepairStatus status = RtpRtxPayload.TryWrite(1, [1, 2], destination, out int bytesWritten);

        Assert.Equal(RtpRepairStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void RtxPayload_ReturnsDestinationTooSmallWhenHeaderDoesNotFit()
    {
        Span<byte> destination = stackalloc byte[1];

        RtpRepairStatus status = RtpRtxPayload.TryWrite(1, [], destination, out int bytesWritten);

        Assert.Equal(RtpRepairStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void RtxPacketizer_CreatesRetransmissionPacketWithOriginalSequenceNumber()
    {
        byte[] originalPayload = [0x10, 0x11, 0x12];
        byte[] rtxPayloadStorage = new byte[16];
        var original = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 111,
                SequenceNumber = 0x4567,
                Timestamp = 0x01020304,
                Ssrc = 0xAABBCCDD,
                Marker = true
            },
            Payload = originalPayload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxPacketizer.TryPacketize(
            original,
            mapping,
            rtxSequenceNumber: 0x9001,
            rtxTimestamp: 0x0A0B0C0D,
            rtxPayloadStorage,
            out RtpPacket rtx);

        Assert.Equal(RtpRepairStatus.Success, status);
        Assert.Equal(112, rtx.Header.PayloadType);
        Assert.Equal(0x9001, rtx.Header.SequenceNumber);
        Assert.Equal(0x0A0B0C0Du, rtx.Header.Timestamp);
        Assert.Equal(0x11223344u, rtx.Header.Ssrc);
        Assert.True(rtx.Header.Marker);
        Assert.Equal([0x45, 0x67, 0x10, 0x11, 0x12], rtx.Payload.ToArray());
    }

    [Fact]
    public void RtxRepairer_RestoresMediaPacketShape()
    {
        byte[] rtxPayload = [0x45, 0x67, 0x10, 0x11, 0x12];
        var rtx = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 112,
                SequenceNumber = 0x9001,
                Timestamp = 0x0A0B0C0D,
                Ssrc = 0x11223344,
                Marker = true
            },
            Payload = rtxPayload,
            ArrivalTime = DateTimeOffset.UnixEpoch.AddMilliseconds(1)
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxRepairer.TryRepair(rtx, mapping, 0x01020304, out RtpPacket repaired);

        Assert.Equal(RtpRepairStatus.Success, status);
        Assert.Equal(111, repaired.Header.PayloadType);
        Assert.Equal(0x4567, repaired.Header.SequenceNumber);
        Assert.Equal(0x01020304u, repaired.Header.Timestamp);
        Assert.Equal(0xAABBCCDDu, repaired.Header.Ssrc);
        Assert.True(repaired.Header.Marker);
        Assert.Equal([0x10, 0x11, 0x12], repaired.Payload.ToArray());
        Assert.Equal(rtx.ArrivalTime, repaired.ArrivalTime);
    }

    [Fact]
    public void RtxRepairer_RejectsMappingMismatch()
    {
        var rtx = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 113,
                SequenceNumber = 1,
                Timestamp = 2,
                Ssrc = 3
            },
            Payload = new byte[] { 0, 1 },
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 10,
            RtxSsrc = 11,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxRepairer.TryRepair(rtx, mapping, 4, out _);

        Assert.Equal(RtpRepairStatus.MappingMismatch, status);
    }

    [Fact]
    public void RtxRepairer_RejectsMalformedRtxPayload()
    {
        var rtx = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 112,
                SequenceNumber = 1,
                Timestamp = 2,
                Ssrc = 0x11223344
            },
            Payload = new byte[] { 0x45 },
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxRepairer.TryRepair(rtx, mapping, 0x01020304, out RtpPacket repaired);

        Assert.Equal(RtpRepairStatus.InvalidPacket, status);
        Assert.Equal(default, repaired);
    }

    [Fact]
    public void RtxPacketizer_RejectsMappingMismatch()
    {
        byte[] originalPayload = [0x10, 0x11, 0x12];
        byte[] rtxPayloadStorage = new byte[16];
        var original = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 113,
                SequenceNumber = 0x4567,
                Timestamp = 0x01020304,
                Ssrc = 0xAABBCCDD
            },
            Payload = originalPayload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxPacketizer.TryPacketize(
            original,
            mapping,
            rtxSequenceNumber: 0x9001,
            rtxTimestamp: 0x0A0B0C0D,
            rtxPayloadStorage,
            out _);

        Assert.Equal(RtpRepairStatus.MappingMismatch, status);
    }

    [Fact]
    public void RtxPacketizer_RejectsOutOfRangePayloadTypeMapping()
    {
        byte[] originalPayload = [0x10, 0x11, 0x12];
        byte[] rtxPayloadStorage = new byte[16];
        var original = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 200,
                SequenceNumber = 0x4567,
                Timestamp = 0x01020304,
                Ssrc = 0xAABBCCDD
            },
            Payload = originalPayload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 200,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxPacketizer.TryPacketize(
            original,
            mapping,
            rtxSequenceNumber: 0x9001,
            rtxTimestamp: 0x0A0B0C0D,
            rtxPayloadStorage,
            out _);

        Assert.Equal(RtpRepairStatus.InvalidPacket, status);
    }

    [Fact]
    public void RtxPacketizer_RejectsMalformedRetainedOriginalPacket()
    {
        byte[] rtxPayloadStorage = new byte[16];
        var original = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 111,
                SequenceNumber = 0x4567,
                Timestamp = 0x01020304,
                Ssrc = 0xAABBCCDD,
                CsrcCount = 2
            },
            Csrcs = new uint[] { 0xCAFE0001 },
            Payload = new byte[] { 0x10 },
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxPacketizer.TryPacketize(
            original,
            mapping,
            rtxSequenceNumber: 0x9001,
            rtxTimestamp: 0x0A0B0C0D,
            rtxPayloadStorage,
            out RtpPacket rtx);

        Assert.Equal(RtpRepairStatus.InvalidPacket, status);
        Assert.Equal(default, rtx);
    }

    [Fact]
    public void RtxPacketizer_RejectsMalformedOneByteHeaderExtensions()
    {
        byte[] rtxPayloadStorage = new byte[16];
        var original = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 111,
                SequenceNumber = 0x4567,
                Timestamp = 0x01020304,
                Ssrc = 0xAABBCCDD,
                ExtensionProfile = RtpHeaderExtensionEnumerator.OneByteHeaderProfile
            },
            ExtensionData = new byte[] { 0xF0, 0x00, 0x00, 0x00 },
            Payload = new byte[] { 0x10 },
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxPacketizer.TryPacketize(
            original,
            mapping,
            rtxSequenceNumber: 0x9001,
            rtxTimestamp: 0x0A0B0C0D,
            rtxPayloadStorage,
            out RtpPacket rtx);

        Assert.Equal(RtpRepairStatus.InvalidPacket, status);
        Assert.Equal(default, rtx);
    }

    [Fact]
    public void RtxRepairer_RejectsOutOfRangePayloadTypeMapping()
    {
        var rtx = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 200,
                SequenceNumber = 0x9001,
                Timestamp = 0x0A0B0C0D,
                Ssrc = 0x11223344
            },
            Payload = new byte[] { 0x45, 0x67, 0x10 },
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 200
        };

        RtpRepairStatus status = RtpRtxRepairer.TryRepair(rtx, mapping, 0x01020304, out _);

        Assert.Equal(RtpRepairStatus.InvalidPacket, status);
    }

    [Fact]
    public void RtxRepairer_RejectsMalformedRetainedRtxPacket()
    {
        var rtx = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 112,
                SequenceNumber = 0x9001,
                Timestamp = 0x0A0B0C0D,
                Ssrc = 0x11223344,
                Padding = true
            },
            Payload = new byte[] { 0x45, 0x67, 0x10 },
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        RtpRepairStatus status = RtpRtxRepairer.TryRepair(rtx, mapping, 0x01020304, out RtpPacket repaired);

        Assert.Equal(RtpRepairStatus.InvalidPacket, status);
        Assert.Equal(default, repaired);
    }

    [Fact]
    public void NackReader_ExpandsPacketIdsAndBitmask()
    {
        RtcpNackEntry[] entries =
        [
            new() { PacketId = 100, LostPacketBitmask = 0b0000_0000_0000_0101 }
        ];
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 0xAABBCCDD,
            Entries = entries
        };
        var sink = new CollectingRepairRequestSink();

        bool accepted = RtpRepairRequestReader.TryWriteRequests(nack, sink);

        Assert.True(accepted);
        Assert.Equal(3, sink.Count);
        Assert.Equal(100, sink.Requests[0].SequenceNumber);
        Assert.Equal(101, sink.Requests[1].SequenceNumber);
        Assert.Equal(103, sink.Requests[2].SequenceNumber);
        Assert.All(sink.Requests[..sink.Count], request => Assert.Equal(0xAABBCCDDu, request.MediaSsrc));
    }

    [Fact]
    public void NackWriter_CoalescesRepairRequestsIntoEntries()
    {
        RtpRepairRequest[] requests =
        [
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 100 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 101 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 103 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 117 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 118 }
        ];
        RtcpNackEntry[] entries = new RtcpNackEntry[2];

        RtpRepairStatus status = RtpRepairRequestWriter.TryWriteNackEntries(
            0xAABBCCDD,
            requests,
            entries,
            out int entriesWritten);

        Assert.Equal(RtpRepairStatus.Success, status);
        Assert.Equal(2, entriesWritten);
        Assert.Equal(100, entries[0].PacketId);
        Assert.Equal(0b0000_0000_0000_0101, entries[0].LostPacketBitmask);
        Assert.Equal(117, entries[1].PacketId);
        Assert.Equal(0b0000_0000_0000_0001, entries[1].LostPacketBitmask);
    }

    [Fact]
    public void NackWriter_HandlesSequenceWrapAndDuplicates()
    {
        RtpRepairRequest[] requests =
        [
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 65534 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 65535 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 65535 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 0 }
        ];
        RtcpNackEntry[] entries = new RtcpNackEntry[1];

        RtpRepairStatus status = RtpRepairRequestWriter.TryWriteNackEntries(
            0xAABBCCDD,
            requests,
            entries,
            out int entriesWritten);

        Assert.Equal(RtpRepairStatus.Success, status);
        Assert.Equal(1, entriesWritten);
        Assert.Equal(65534, entries[0].PacketId);
        Assert.True(entries[0].Contains(65535));
        Assert.True(entries[0].Contains(0));
    }

    [Fact]
    public void NackWriter_ReturnsStatusForInvalidInputs()
    {
        RtpRepairRequest[] twoEntries =
        [
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 100 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 117 }
        ];
        RtpRepairRequest[] mismatchedSsrc =
        [
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 100 },
            new() { MediaSsrc = 0x11223344, SequenceNumber = 101 }
        ];
        RtpRepairRequest[] unsorted =
        [
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 100 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 99 }
        ];
        RtcpNackEntry[] oneEntry =
        [
            new() { PacketId = 0xEEEE, LostPacketBitmask = 0xFFFF }
        ];
        RtcpNackEntry[] twoEntryBuffer =
        [
            new() { PacketId = 0xEEEE, LostPacketBitmask = 0xFFFF },
            new() { PacketId = 0xDDDD, LostPacketBitmask = 0xAAAA }
        ];

        RtpRepairStatus destinationStatus = RtpRepairRequestWriter.TryWriteNackEntries(
            0xAABBCCDD,
            twoEntries,
            oneEntry,
            out int destinationEntriesWritten);
        RtpRepairStatus mappingStatus = RtpRepairRequestWriter.TryWriteNackEntries(
            0xAABBCCDD,
            mismatchedSsrc,
            twoEntryBuffer,
            out int mappingEntriesWritten);
        RtpRepairStatus unsortedStatus = RtpRepairRequestWriter.TryWriteNackEntries(
            0xAABBCCDD,
            unsorted,
            twoEntryBuffer,
            out int unsortedEntriesWritten);

        Assert.Equal(RtpRepairStatus.DestinationTooSmall, destinationStatus);
        Assert.Equal(0, destinationEntriesWritten);
        Assert.Equal(0xEEEE, oneEntry[0].PacketId);
        Assert.Equal(0xFFFF, oneEntry[0].LostPacketBitmask);
        Assert.Equal(RtpRepairStatus.MappingMismatch, mappingStatus);
        Assert.Equal(0, mappingEntriesWritten);
        Assert.Equal(0xEEEE, twoEntryBuffer[0].PacketId);
        Assert.Equal(0xFFFF, twoEntryBuffer[0].LostPacketBitmask);
        Assert.Equal(0xDDDD, twoEntryBuffer[1].PacketId);
        Assert.Equal(0xAAAA, twoEntryBuffer[1].LostPacketBitmask);
        Assert.Equal(RtpRepairStatus.InvalidPacket, unsortedStatus);
        Assert.Equal(0, unsortedEntriesWritten);
        Assert.Equal(0xEEEE, twoEntryBuffer[0].PacketId);
        Assert.Equal(0xFFFF, twoEntryBuffer[0].LostPacketBitmask);
        Assert.Equal(0xDDDD, twoEntryBuffer[1].PacketId);
        Assert.Equal(0xAAAA, twoEntryBuffer[1].LostPacketBitmask);
    }

    [Fact]
    public void NackWriter_ReportsMappingMismatchBeforeDestinationPressure()
    {
        RtpRepairRequest[] requests =
        [
            new() { MediaSsrc = 0x11223344, SequenceNumber = 100 }
        ];

        RtpRepairStatus status = RtpRepairRequestWriter.TryWriteNackEntries(
            0xAABBCCDD,
            requests,
            [],
            out int entriesWritten);

        Assert.Equal(RtpRepairStatus.MappingMismatch, status);
        Assert.Equal(0, entriesWritten);
    }

    [Fact]
    public void RtxPacketizeAndRepair_DoNotAllocate()
    {
        byte[] originalPayload = [0x10, 0x11, 0x12];
        byte[] rtxPayloadStorage = new byte[16];
        var original = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 111,
                SequenceNumber = 0x4567,
                Timestamp = 0x01020304,
                Ssrc = 0xAABBCCDD,
                Marker = true
            },
            Payload = originalPayload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var mapping = new RtpRtxRepairMapping
        {
            MediaSsrc = 0xAABBCCDD,
            RtxSsrc = 0x11223344,
            MediaPayloadType = 111,
            RtxPayloadType = 112
        };

        for (int i = 0; i < 32; i++)
        {
            if (RtpRtxPacketizer.TryPacketize(original, mapping, (ushort)i, (uint)i, rtxPayloadStorage, out RtpPacket warmupRtx) != RtpRepairStatus.Success ||
                RtpRtxRepairer.TryRepair(warmupRtx, mapping, original.Header.Timestamp, out RtpPacket warmupRepaired) != RtpRepairStatus.Success ||
                warmupRepaired.Header.SequenceNumber != original.Header.SequenceNumber)
            {
                throw new InvalidOperationException("RTP RTX warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            if (RtpRtxPacketizer.TryPacketize(original, mapping, (ushort)i, (uint)i, rtxPayloadStorage, out RtpPacket rtx) != RtpRepairStatus.Success ||
                RtpRtxRepairer.TryRepair(rtx, mapping, original.Header.Timestamp, out RtpPacket repaired) != RtpRepairStatus.Success ||
                repaired.Payload.Length != original.Payload.Length)
            {
                throw new InvalidOperationException("RTP RTX packetize/repair failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NackReader_DoesNotAllocate()
    {
        RtcpNackEntry[] entries =
        [
            new() { PacketId = 100, LostPacketBitmask = 0b0000_0000_0000_0101 }
        ];
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 0xAABBCCDD,
            Entries = entries
        };
        var sink = new CountingRepairRequestSink();

        for (int i = 0; i < 32; i++)
        {
            sink.Reset();
            if (!RtpRepairRequestReader.TryWriteRequests(nack, sink) || sink.Count != 3)
            {
                throw new InvalidOperationException("RTP repair request warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            sink.Reset();
            if (!RtpRepairRequestReader.TryWriteRequests(nack, sink) || sink.Count != 3)
            {
                throw new InvalidOperationException("RTP repair request expansion failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void NackWriter_DoesNotAllocate()
    {
        RtpRepairRequest[] requests =
        [
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 100 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 101 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 103 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 117 },
            new() { MediaSsrc = 0xAABBCCDD, SequenceNumber = 118 }
        ];
        RtcpNackEntry[] entries = new RtcpNackEntry[2];

        for (int i = 0; i < 32; i++)
        {
            if (RtpRepairRequestWriter.TryWriteNackEntries(0xAABBCCDD, requests, entries, out int entriesWritten) != RtpRepairStatus.Success ||
                entriesWritten != 2)
            {
                throw new InvalidOperationException("RTP repair request write warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            if (RtpRepairRequestWriter.TryWriteNackEntries(0xAABBCCDD, requests, entries, out int entriesWritten) != RtpRepairStatus.Success ||
                entriesWritten != 2)
            {
                throw new InvalidOperationException("RTP repair request write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private sealed class CollectingRepairRequestSink : IRtpRepairRequestSink
    {
        public RtpRepairRequest[] Requests { get; } = new RtpRepairRequest[16];

        public int Count { get; private set; }

        public bool TryWrite(in RtpRepairRequest request)
        {
            Requests[Count++] = request;
            return true;
        }
    }

    private sealed class CountingRepairRequestSink : IRtpRepairRequestSink
    {
        public int Count { get; private set; }

        public void Reset()
        {
            Count = 0;
        }

        public bool TryWrite(in RtpRepairRequest request)
        {
            Count++;
            return true;
        }
    }
}
