#nullable enable

using System.Buffers.Binary;
using System.Buffers;
using HPD.Events.Struct;
using HPD.Media.Diagnostics;
using HPD.Media.Rtcp;
using HPD.Media.Rtp;

namespace HPD.Media.Rtcp.Tests.Vectors;

public sealed class RtcpPacketReaderWriterTests
{
    [Fact]
    public void TryParseSenderReport_ReadsSenderInfo()
    {
        ReadOnlySpan<byte> packet =
        [
            0x80, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x54
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSenderReport(packet, out RtcpSenderReport report);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(0x01020304u, report.SenderSsrc);
        Assert.Equal(0x1112131421222324ul, report.NtpTimestamp);
        Assert.Equal(0x31323334u, report.RtpTimestamp);
        Assert.Equal(0x41424344u, report.SenderPacketCount);
        Assert.Equal(0x51525354u, report.SenderOctetCount);
    }

    [Fact]
    public void TryReadCompound_IteratesMultiplePackets()
    {
        ReadOnlySpan<byte> compound =
        [
            0x80, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x54,
            0x80, 0xC9, 0x00, 0x01,
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadCompound(compound, out RtcpCompoundPacketEnumerator enumerator);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(RtcpPacketType.SenderReport, enumerator.Current.PacketType);
        Assert.Equal(28, enumerator.Current.Packet.Length);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(RtcpPacketType.ReceiverReport, enumerator.Current.PacketType);
        Assert.Equal(8, enumerator.Current.Packet.Length);
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void TryReadCompound_RejectsTruncatedPacket()
    {
        ReadOnlySpan<byte> compound =
        [
            0x80, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadCompound(compound, out _);

        Assert.Equal(RtcpPacketStatus.MalformedCompoundPacket, status);
    }

    [Fact]
    public void TryReadCompound_AcceptsPaddingOnlyOnLastPacket()
    {
        ReadOnlySpan<byte> compound =
        [
            0x80, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x54,
            0xA0, 0xC9, 0x00, 0x02,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x00, 0x00, 0x00, 0x04
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadCompound(compound, out RtcpCompoundPacketEnumerator enumerator);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(RtcpPacketType.SenderReport, enumerator.Current.PacketType);
        Assert.True(enumerator.MoveNext());
        Assert.Equal(RtcpPacketType.ReceiverReport, enumerator.Current.PacketType);
        Assert.Equal(12, enumerator.Current.Packet.Length);
        Assert.False(enumerator.MoveNext());
    }

    [Fact]
    public void TryReadCompound_RejectsPaddingOnNonLastPacket()
    {
        ReadOnlySpan<byte> compound =
        [
            0xA0, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x04,
            0x80, 0xC9, 0x00, 0x01,
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadCompound(compound, out _);

        Assert.Equal(RtcpPacketStatus.MalformedCompoundPacket, status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void TryReadCompound_RejectsInvalidPaddingCountOnLastPacket(byte paddingBytes)
    {
        ReadOnlySpan<byte> compound =
        [
            0xA0, 0xC9, 0x00, 0x01,
            0xAA, 0xBB, 0xCC, paddingBytes
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadCompound(compound, out _);

        Assert.Equal(RtcpPacketStatus.MalformedCompoundPacket, status);
    }

    [Fact]
    public void TryWriteReceiverReport_WritesReportBlock()
    {
        RtcpReceptionReportBlock[] blocks =
        [
            new()
            {
                Ssrc = 0x01020304,
                FractionLost = 7,
                CumulativePacketsLost = -2,
                ExtendedHighestSequenceNumberReceived = 0x11121314,
                InterarrivalJitter = 0x21222324,
                LastSenderReport = 0x31323334,
                DelaySinceLastSenderReport = 0x41424344
            }
        ];
        var report = new RtcpReceiverReport
        {
            ReporterSsrc = 0xAABBCCDD,
            Reports = blocks
        };
        Span<byte> destination = stackalloc byte[32];

        RtcpPacketStatus status = RtcpPacketWriter.TryWriteReceiverReport(report, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(32, bytesWritten);
        Assert.Equal(0x81, destination[0]);
        Assert.Equal((byte)RtcpPacketType.ReceiverReport, destination[1]);
        Assert.Equal(7, BinaryPrimitives.ReadUInt16BigEndian(destination.Slice(2, 2)));
        Assert.Equal(0xAABBCCDDu, BinaryPrimitives.ReadUInt32BigEndian(destination.Slice(4, 4)));
        Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32BigEndian(destination.Slice(8, 4)));
        Assert.Equal(7, destination[12]);
        Assert.Equal([0xFF, 0xFF, 0xFE], destination.Slice(13, 3).ToArray());
        Assert.Equal(0x11121314u, BinaryPrimitives.ReadUInt32BigEndian(destination.Slice(16, 4)));
        Assert.Equal(0x21222324u, BinaryPrimitives.ReadUInt32BigEndian(destination.Slice(20, 4)));
        Assert.Equal(0x31323334u, BinaryPrimitives.ReadUInt32BigEndian(destination.Slice(24, 4)));
        Assert.Equal(0x41424344u, BinaryPrimitives.ReadUInt32BigEndian(destination.Slice(28, 4)));
    }

    [Fact]
    public void TryWriteSenderReport_WritesPacketThatParsesBack()
    {
        var report = new RtcpSenderReport
        {
            SenderSsrc = 0x01020304,
            NtpTimestamp = 0x1112131421222324,
            RtpTimestamp = 0x31323334,
            SenderPacketCount = 0x41424344,
            SenderOctetCount = 0x51525354
        };
        Span<byte> destination = stackalloc byte[28];

        RtcpPacketStatus status = RtcpPacketWriter.TryWriteSenderReport(report, destination, out int bytesWritten);
        RtcpPacketStatus parseStatus = RtcpPacketReader.TryParseSenderReport(destination[..bytesWritten], out RtcpSenderReport parsed);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(28, bytesWritten);
        Assert.Equal(RtcpPacketStatus.Success, parseStatus);
        Assert.Equal(report.SenderSsrc, parsed.SenderSsrc);
        Assert.Equal(report.NtpTimestamp, parsed.NtpTimestamp);
        Assert.Equal(report.RtpTimestamp, parsed.RtpTimestamp);
        Assert.Equal(report.SenderPacketCount, parsed.SenderPacketCount);
        Assert.Equal(report.SenderOctetCount, parsed.SenderOctetCount);
    }

    [Fact]
    public void TryWriteSenderReport_ReturnsDestinationTooSmall()
    {
        var report = new RtcpSenderReport
        {
            SenderSsrc = 1,
            NtpTimestamp = 2,
            RtpTimestamp = 3,
            SenderPacketCount = 4,
            SenderOctetCount = 5
        };
        Span<byte> destination = stackalloc byte[27];

        RtcpPacketStatus status = RtcpPacketWriter.TryWriteSenderReport(report, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryParseSenderReport_RejectsLengthThatDoesNotMatchReportCount()
    {
        ReadOnlySpan<byte> packet =
        [
            0x80, 0xC8, 0x00, 0x07,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x54,
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSenderReport(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseSenderReport_RejectsPaddingBit()
    {
        ReadOnlySpan<byte> packet =
        [
            0xA0, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x54
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSenderReport(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void WriteSenderReport_BufferWriterWritesPacket()
    {
        var report = new RtcpSenderReport
        {
            SenderSsrc = 1,
            NtpTimestamp = 2,
            RtpTimestamp = 3,
            SenderPacketCount = 4,
            SenderOctetCount = 5
        };
        var writer = new ArrayBufferWriter<byte>();

        RtcpPacketWriter.WriteSenderReport(report, writer);

        Assert.Equal(28, writer.WrittenCount);
        Assert.Equal(0x80, writer.WrittenSpan[0]);
        Assert.Equal((byte)RtcpPacketType.SenderReport, writer.WrittenSpan[1]);
    }

    [Fact]
    public void TryParseReceiverReport_ReadsReportBlock()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xC9, 0x00, 0x07,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x01, 0x02, 0x03, 0x04,
            0x07, 0xFF, 0xFF, 0xFE,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44
        ];
        RtcpReceptionReportBlock[] reports = new RtcpReceptionReportBlock[1];

        RtcpPacketStatus status = RtcpPacketReader.TryParseReceiverReport(packet, reports, out RtcpReceiverReport receiverReport);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(0xAABBCCDDu, receiverReport.ReporterSsrc);
        Assert.Equal(1, receiverReport.Reports.Length);
        Assert.Equal(0x01020304u, receiverReport.Reports.Span[0].Ssrc);
        Assert.Equal(7, receiverReport.Reports.Span[0].FractionLost);
        Assert.Equal(-2, receiverReport.Reports.Span[0].CumulativePacketsLost);
        Assert.Equal(0x11121314u, receiverReport.Reports.Span[0].ExtendedHighestSequenceNumberReceived);
        Assert.Equal(0x21222324u, receiverReport.Reports.Span[0].InterarrivalJitter);
        Assert.Equal(0x31323334u, receiverReport.Reports.Span[0].LastSenderReport);
        Assert.Equal(0x41424344u, receiverReport.Reports.Span[0].DelaySinceLastSenderReport);
        Assert.Equal(receiverReport.Reports.Span[0], reports[0]);
    }

    [Fact]
    public void TryParseReceiverReport_ReturnsDestinationTooSmall()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xC9, 0x00, 0x07,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x01, 0x02, 0x03, 0x04,
            0x07, 0xFF, 0xFF, 0xFE,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryParseReceiverReport(
            packet,
            Array.Empty<RtcpReceptionReportBlock>(),
            out _);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
    }

    [Fact]
    public void TryParseReceiverReport_RejectsMalformedLength()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xC9, 0x00, 0x07,
            0xAA, 0xBB, 0xCC, 0xDD
        ];
        RtcpReceptionReportBlock[] reports = new RtcpReceptionReportBlock[1];

        RtcpPacketStatus status = RtcpPacketReader.TryParseReceiverReport(packet, reports, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseReceiverReport_RejectsPaddingBit()
    {
        ReadOnlySpan<byte> packet =
        [
            0xA1, 0xC9, 0x00, 0x07,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x01, 0x02, 0x03, 0x04,
            0x07, 0xFF, 0xFF, 0xFE,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44
        ];
        RtcpReceptionReportBlock[] reports = new RtcpReceptionReportBlock[1];

        RtcpPacketStatus status = RtcpPacketReader.TryParseReceiverReport(packet, reports, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWriteReceiverReport_ReturnsDestinationTooSmall()
    {
        var report = new RtcpReceiverReport
        {
            ReporterSsrc = 1,
            Reports = Array.Empty<RtcpReceptionReportBlock>()
        };
        Span<byte> destination = stackalloc byte[4];

        RtcpPacketStatus status = RtcpPacketWriter.TryWriteReceiverReport(report, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void WriteReceiverReport_ValidatesBeforeRequestingDestinationStorage()
    {
        RtcpReceptionReportBlock[] blocks =
        [
            new()
            {
                Ssrc = 1,
                FractionLost = 0,
                CumulativePacketsLost = 8_388_608,
                ExtendedHighestSequenceNumberReceived = 0,
                InterarrivalJitter = 0,
                LastSenderReport = 0,
                DelaySinceLastSenderReport = 0
            }
        ];
        var report = new RtcpReceiverReport
        {
            ReporterSsrc = 1,
            Reports = blocks
        };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtcpPacketWriter.WriteReceiverReport(report, writer));

        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void TryParseSourceDescription_ReadsCnameItem()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00
        ];
        RtcpSdesChunk[] chunks = new RtcpSdesChunk[1];
        RtcpSdesItem[] items = new RtcpSdesItem[1];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSourceDescription(packet, chunks, items, out RtcpSourceDescription sdes);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(1, sdes.Chunks.Length);
        Assert.Equal(0x01020304u, sdes.Chunks.Span[0].Ssrc);
        Assert.Equal(1, sdes.Chunks.Span[0].Items.Length);
        Assert.Equal(1, sdes.Chunks.Span[0].Items.Span[0].Type);
        Assert.Equal("test"u8.ToArray(), sdes.Chunks.Span[0].Items.Span[0].Utf8Value.ToArray());
    }

    [Fact]
    public void TryReadSourceDescription_TraversesChunksAndItemsWithoutRetainedCopies()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00
        ];
        ReadOnlySpan<byte> expectedValue = "test"u8;

        RtcpPacketStatus status = RtcpPacketReader.TryReadSourceDescription(packet, out RtcpSdesChunkEnumerator chunks);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.True(chunks.MoveNext());
        Assert.Equal(0x01020304u, chunks.Current.Ssrc);
        RtcpSdesItemEnumerator items = chunks.Current.GetItems();
        Assert.True(items.MoveNext());
        Assert.Equal(1, items.Current.Type);
        Assert.True(items.Current.Utf8Value.SequenceEqual(expectedValue));
        Assert.False(items.MoveNext());
        Assert.False(chunks.MoveNext());
    }

    [Fact]
    public void TryWriteSourceDescription_WritesWordPaddedChunk()
    {
        RtcpSdesItem[] items =
        [
            new() { Type = 1, Utf8Value = "test"u8.ToArray() }
        ];
        RtcpSdesChunk[] chunks =
        [
            new() { Ssrc = 0x01020304, Items = items }
        ];
        var sdes = new RtcpSourceDescription { Chunks = chunks };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus status = RtcpPacketWriter.TryWriteSourceDescription(sdes, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(16, bytesWritten);
        Assert.Equal([0x81, 0xCA, 0x00, 0x03], destination[..4].ToArray());
        Assert.Equal(0x01020304u, BinaryPrimitives.ReadUInt32BigEndian(destination.Slice(4, 4)));
        Assert.Equal(1, destination[8]);
        Assert.Equal(4, destination[9]);
        Assert.Equal("test"u8.ToArray(), destination.Slice(10, 4).ToArray());
        Assert.Equal(0, destination[14]);
        Assert.Equal(0, destination[15]);
    }

    [Fact]
    public void TryWriteSourceDescription_RejectsPacketLengthBeyondRtcpHeaderLimit()
    {
        byte[] value = new byte[255];
        RtcpSdesItem[] items = new RtcpSdesItem[1_030];
        Array.Fill(items, new RtcpSdesItem { Type = 1, Utf8Value = value });
        RtcpSdesChunk[] chunks =
        [
            new() { Ssrc = 0x01020304, Items = items }
        ];
        var sdes = new RtcpSourceDescription { Chunks = chunks };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus status = RtcpPacketWriter.TryWriteSourceDescription(sdes, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void SourceDescription_RejectsEmptyChunkSet()
    {
        ReadOnlySpan<byte> packet =
        [
            0x80, 0xCA, 0x00, 0x00
        ];
        var sdes = new RtcpSourceDescription { Chunks = ReadOnlyMemory<RtcpSdesChunk>.Empty };
        Span<byte> destination = stackalloc byte[4];
        var writer = new ThrowingBufferWriter();

        RtcpPacketStatus readStatus = RtcpPacketReader.TryReadSourceDescription(packet, out _);
        RtcpPacketStatus parseStatus = RtcpPacketReader.TryParseSourceDescription(packet, [], [], out _);
        RtcpPacketStatus writeStatus = RtcpPacketWriter.TryWriteSourceDescription(sdes, destination, out int bytesWritten);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtcpPacketWriter.WriteSourceDescription(sdes, writer));

        Assert.Equal(RtcpPacketStatus.InvalidPacket, readStatus);
        Assert.Equal(RtcpPacketStatus.InvalidPacket, parseStatus);
        Assert.Equal(RtcpPacketStatus.InvalidPacket, writeStatus);
        Assert.Equal(0, bytesWritten);
        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void WriteSourceDescription_ValidatesChunkCountBeforeRequestingDestinationStorage()
    {
        RtcpSdesChunk[] chunks = Enumerable.Range(0, 32)
            .Select(index => new RtcpSdesChunk { Ssrc = (uint)index, Items = Array.Empty<RtcpSdesItem>() })
            .ToArray();
        var sdes = new RtcpSourceDescription { Chunks = chunks };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtcpPacketWriter.WriteSourceDescription(sdes, writer));

        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void TryParseSourceDescription_ReturnsDestinationTooSmall()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00
        ];
        RtcpSdesChunk[] chunks = new RtcpSdesChunk[1];
        RtcpSdesItem[] items = [];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSourceDescription(packet, chunks, items, out _);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
    }

    [Fact]
    public void TryParseSourceDescription_RejectsNonZeroPadding()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0xFF
        ];
        RtcpSdesChunk[] chunks = new RtcpSdesChunk[1];
        RtcpSdesItem[] items = new RtcpSdesItem[1];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSourceDescription(packet, chunks, items, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryReadSourceDescription_RejectsNonZeroPadding()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0xFF
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadSourceDescription(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryReadSourceDescription_RejectsTruncatedItem()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x08,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadSourceDescription(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryReadSourceDescription_RejectsPaddingBit()
    {
        ReadOnlySpan<byte> packet =
        [
            0xA1, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadSourceDescription(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseSourceDescription_RejectsPaddingBit()
    {
        ReadOnlySpan<byte> packet =
        [
            0xA1, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00
        ];
        RtcpSdesChunk[] chunks = new RtcpSdesChunk[1];
        RtcpSdesItem[] items = new RtcpSdesItem[1];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSourceDescription(packet, chunks, items, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseSourceDescription_RejectsTrailingBytesAfterDeclaredChunks()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00,
            0xFF, 0x00, 0x00, 0x00
        ];
        RtcpSdesChunk[] chunks = new RtcpSdesChunk[1];
        RtcpSdesItem[] items = new RtcpSdesItem[1];

        RtcpPacketStatus status = RtcpPacketReader.TryParseSourceDescription(packet, chunks, items, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryReadSourceDescription_RejectsTrailingBytesAfterDeclaredChunks()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCA, 0x00, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00,
            0xFF, 0x00, 0x00, 0x00
        ];

        RtcpPacketStatus status = RtcpPacketReader.TryReadSourceDescription(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void WriteSourceDescription_BufferWriterWritesPacket()
    {
        RtcpSdesItem[] items =
        [
            new() { Type = 1, Utf8Value = "test"u8.ToArray() }
        ];
        RtcpSdesChunk[] chunks =
        [
            new() { Ssrc = 0x01020304, Items = items }
        ];
        var sdes = new RtcpSourceDescription { Chunks = chunks };
        var writer = new ArrayBufferWriter<byte>();

        RtcpPacketWriter.WriteSourceDescription(sdes, writer);

        Assert.Equal(16, writer.WrittenCount);
        Assert.Equal(0x81, writer.WrittenSpan[0]);
        Assert.Equal(0xCA, writer.WrittenSpan[1]);
    }

    [Fact]
    public void TryReadCompound_DoesNotAllocate()
    {
        byte[] compound =
        [
            0x80, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x54,
            0x80, 0xC9, 0x00, 0x01,
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryReadCompound(compound, out RtcpCompoundPacketEnumerator enumerator) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP compound read failed during warmup.");
            }

            while (enumerator.MoveNext())
            {
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryReadCompound(compound, out RtcpCompoundPacketEnumerator enumerator) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP compound read failed during allocation measurement.");
            }

            while (enumerator.MoveNext())
            {
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TryReadSourceDescription_DoesNotAllocate()
    {
        byte[] packet =
        [
            0x81, 0xCA, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x01, 0x04,
            (byte)'t', (byte)'e', (byte)'s', (byte)'t',
            0x00, 0x00
        ];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryReadSourceDescription(packet, out RtcpSdesChunkEnumerator chunks) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP SDES read failed during warmup.");
            }

            while (chunks.MoveNext())
            {
                RtcpSdesItemEnumerator items = chunks.Current.GetItems();
                while (items.MoveNext())
                {
                }
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryReadSourceDescription(packet, out RtcpSdesChunkEnumerator chunks) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP SDES read failed during allocation measurement.");
            }

            while (chunks.MoveNext())
            {
                RtcpSdesItemEnumerator items = chunks.Current.GetItems();
                while (items.MoveNext())
                {
                }
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TryParseReceiverReport_DoesNotAllocate()
    {
        byte[] packet =
        [
            0x81, 0xC9, 0x00, 0x07,
            0xAA, 0xBB, 0xCC, 0xDD,
            0x01, 0x02, 0x03, 0x04,
            0x07, 0xFF, 0xFF, 0xFE,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44
        ];
        RtcpReceptionReportBlock[] reports = new RtcpReceptionReportBlock[1];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryParseReceiverReport(packet, reports, out _) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP receiver report parse failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryParseReceiverReport(packet, reports, out _) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP receiver report parse failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TryParseSenderReport_DoesNotAllocate()
    {
        byte[] packet =
        [
            0x80, 0xC8, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x11, 0x12, 0x13, 0x14,
            0x21, 0x22, 0x23, 0x24,
            0x31, 0x32, 0x33, 0x34,
            0x41, 0x42, 0x43, 0x44,
            0x51, 0x52, 0x53, 0x54
        ];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryParseSenderReport(packet, out _) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP sender report parse failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketReader.TryParseSenderReport(packet, out _) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP sender report parse failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TryWriteSenderReport_DoesNotAllocate()
    {
        var report = new RtcpSenderReport
        {
            SenderSsrc = 0x01020304,
            NtpTimestamp = 0x1112131421222324,
            RtpTimestamp = 0x31323334,
            SenderPacketCount = 0x41424344,
            SenderOctetCount = 0x51525354
        };
        Span<byte> destination = stackalloc byte[28];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketWriter.TryWriteSenderReport(report, destination, out _) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP sender report write failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketWriter.TryWriteSenderReport(report, destination, out int bytesWritten) != RtcpPacketStatus.Success
                || bytesWritten != 28)
            {
                throw new InvalidOperationException("RTCP sender report write failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TryWriteReceiverReport_DoesNotAllocate()
    {
        RtcpReceptionReportBlock[] blocks =
        [
            new()
            {
                Ssrc = 0x01020304,
                FractionLost = 7,
                CumulativePacketsLost = -2,
                ExtendedHighestSequenceNumberReceived = 0x11121314,
                InterarrivalJitter = 0x21222324,
                LastSenderReport = 0x31323334,
                DelaySinceLastSenderReport = 0x41424344
            }
        ];
        var report = new RtcpReceiverReport
        {
            ReporterSsrc = 0xAABBCCDD,
            Reports = blocks
        };
        Span<byte> destination = stackalloc byte[32];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketWriter.TryWriteReceiverReport(report, destination, out _) != RtcpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTCP receiver report write failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpPacketWriter.TryWriteReceiverReport(report, destination, out int bytesWritten) != RtcpPacketStatus.Success
                || bytesWritten != 32)
            {
                throw new InvalidOperationException("RTCP receiver report write failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void ReceptionReporter_CreatesLossAndJitterReport()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 10, timestamp: 1_000), arrivalTimestampInRtpClock: 1_000);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 11, timestamp: 1_160), arrivalTimestampInRtpClock: 1_170);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 13, timestamp: 1_480), arrivalTimestampInRtpClock: 1_530);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 0xAABBCCDD,
            remoteSsrc: 0x01020304,
            now: DateTimeOffset.UnixEpoch,
            blocks,
            out RtcpReceiverReport report);

        Assert.True(created);
        Assert.Equal(0xAABBCCDDu, report.ReporterSsrc);
        Assert.Equal(1, report.Reports.Length);
        Assert.Equal(0x01020304u, blocks[0].Ssrc);
        Assert.Equal(64, blocks[0].FractionLost);
        Assert.Equal(1, blocks[0].CumulativePacketsLost);
        Assert.Equal(13u, blocks[0].ExtendedHighestSequenceNumberReceived);
        Assert.True(blocks[0].InterarrivalJitter > 0);
        Assert.Equal(blocks[0].InterarrivalJitter, report.Reports.Span[0].InterarrivalJitter);

        blocks[0] = blocks[0] with { InterarrivalJitter = 1234 };
        Assert.Equal(1234u, report.Reports.Span[0].InterarrivalJitter);
    }

    [Fact]
    public void ReceptionReporter_EmitsStructTelemetryForJitterReport()
    {
        using var hub = new StructEventHub();
        using StructEventInbox<RtcpJitterSample> inbox = hub
            .Route<RtcpJitterSample>(RealtimeMediaTelemetry.RouteOptions)
            .CreateInbox(new StructEventInboxOptions { Capacity = 4 });
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var reporter = new RtcpReceptionReporter(emitters);
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 10, timestamp: 1_000), arrivalTimestampInRtpClock: 1_000);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 11, timestamp: 1_160), arrivalTimestampInRtpClock: 1_170);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 13, timestamp: 1_480), arrivalTimestampInRtpClock: 1_530);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 0xAABBCCDD,
            remoteSsrc: 0x01020304,
            now: DateTimeOffset.UnixEpoch,
            blocks,
            out _);

        Assert.True(created);
        Assert.True(inbox.TryRead(out RtcpJitterSample sample));
        Assert.Equal(0xAABBCCDDu, sample.ReporterSsrc);
        Assert.Equal(0x01020304u, sample.RemoteSsrc);
        Assert.Equal(blocks[0].InterarrivalJitter, sample.InterarrivalJitter);
    }

    [Fact]
    public void ReceptionReporter_IgnoresDuplicatePacketsWhenComputingLoss()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 10, timestamp: 1_000), arrivalTimestampInRtpClock: 1_000);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 11, timestamp: 1_160), arrivalTimestampInRtpClock: 1_160);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 11, timestamp: 1_160), arrivalTimestampInRtpClock: 1_160);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 13, timestamp: 1_480), arrivalTimestampInRtpClock: 1_480);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 0xAABBCCDD,
            remoteSsrc: 0x01020304,
            now: DateTimeOffset.UnixEpoch,
            blocks,
            out _);

        Assert.True(created);
        Assert.Equal(1, blocks[0].CumulativePacketsLost);
        Assert.Equal(64, blocks[0].FractionLost);
    }

    [Fact]
    public void ReceptionReporter_CountsLatePacketThatFillsRecentGap()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 10, timestamp: 1_000), arrivalTimestampInRtpClock: 1_000);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 12, timestamp: 1_320), arrivalTimestampInRtpClock: 1_320);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 11, timestamp: 1_160), arrivalTimestampInRtpClock: 1_160);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 0xAABBCCDD,
            remoteSsrc: 0x01020304,
            now: DateTimeOffset.UnixEpoch,
            blocks,
            out _);

        Assert.True(created);
        Assert.Equal(0, blocks[0].CumulativePacketsLost);
        Assert.Equal(0, blocks[0].FractionLost);
    }

    [Fact]
    public void ReceptionReporter_HandlesSequenceWrap()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 65534, timestamp: 1), arrivalTimestampInRtpClock: 1);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 65535, timestamp: 2), arrivalTimestampInRtpClock: 2);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 0, timestamp: 3), arrivalTimestampInRtpClock: 3);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 99,
            remoteSsrc: 0x01020304,
            now: DateTimeOffset.UnixEpoch,
            blocks,
            out _);

        Assert.True(created);
        Assert.Equal(0, blocks[0].CumulativePacketsLost);
        Assert.Equal(65_536u, blocks[0].ExtendedHighestSequenceNumberReceived);
    }

    [Fact]
    public void ReceptionReporter_ReportsLastSenderReportAndDelay()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];
        DateTimeOffset receivedAt = DateTimeOffset.UnixEpoch.AddSeconds(10);

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 1, timestamp: 1), arrivalTimestampInRtpClock: 1);
        reporter.OnSenderReport(
            new RtcpSenderReport
            {
                SenderSsrc = 0x01020304,
                NtpTimestamp = 0x1122334455667788,
                RtpTimestamp = 1,
                SenderPacketCount = 1,
                SenderOctetCount = 2
            },
            receivedAt);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 99,
            remoteSsrc: 0x01020304,
            now: receivedAt.AddSeconds(2),
            blocks,
            out _);

        Assert.True(created);
        Assert.Equal(0x33445566u, blocks[0].LastSenderReport);
        Assert.Equal(131_072u, blocks[0].DelaySinceLastSenderReport);
    }

    [Fact]
    public void ReceptionReporter_PreRtpSenderReportAppliesOnlyToMatchingSsrc()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];
        DateTimeOffset receivedAt = DateTimeOffset.UnixEpoch.AddSeconds(10);

        reporter.OnSenderReport(
            new RtcpSenderReport
            {
                SenderSsrc = 0x11111111,
                NtpTimestamp = 0x1122334455667788,
                RtpTimestamp = 1,
                SenderPacketCount = 1,
                SenderOctetCount = 2
            },
            receivedAt);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 1, timestamp: 1, ssrc: 0x01020304), arrivalTimestampInRtpClock: 1);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 99,
            remoteSsrc: 0x01020304,
            now: receivedAt.AddSeconds(2),
            blocks,
            out _);

        Assert.True(created);
        Assert.Equal(0u, blocks[0].LastSenderReport);
        Assert.Equal(0u, blocks[0].DelaySinceLastSenderReport);
    }

    [Fact]
    public void ReceptionReporter_PreRtpSenderReportIsRetainedForMatchingSsrc()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];
        DateTimeOffset receivedAt = DateTimeOffset.UnixEpoch.AddSeconds(10);

        reporter.OnSenderReport(
            new RtcpSenderReport
            {
                SenderSsrc = 0x01020304,
                NtpTimestamp = 0x1122334455667788,
                RtpTimestamp = 1,
                SenderPacketCount = 1,
                SenderOctetCount = 2
            },
            receivedAt);
        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 1, timestamp: 1, ssrc: 0x01020304), arrivalTimestampInRtpClock: 1);

        bool created = reporter.TryCreateReceiverReport(
            localSsrc: 99,
            remoteSsrc: 0x01020304,
            now: receivedAt.AddSeconds(2),
            blocks,
            out _);

        Assert.True(created);
        Assert.Equal(0x33445566u, blocks[0].LastSenderReport);
        Assert.Equal(131_072u, blocks[0].DelaySinceLastSenderReport);
    }

    [Fact]
    public void ReceptionReporter_ReportCreationDoesNotAllocate()
    {
        var reporter = new RtcpReceptionReporter();
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 1, timestamp: 1), arrivalTimestampInRtpClock: 1);

        for (int i = 0; i < 1_000; i++)
        {
            Assert.True(reporter.TryCreateReceiverReport(99, 0x01020304, DateTimeOffset.UnixEpoch, blocks, out _));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (!reporter.TryCreateReceiverReport(99, 0x01020304, DateTimeOffset.UnixEpoch, blocks, out _))
            {
                throw new InvalidOperationException("RTCP reception report creation failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void ReceptionReporter_TelemetryWithNoSubscribersDoesNotAllocateAfterWarmup()
    {
        using var hub = new StructEventHub();
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var reporter = new RtcpReceptionReporter(emitters);
        RtcpReceptionReportBlock[] blocks = new RtcpReceptionReportBlock[1];

        reporter.OnRtpPacket(CreatePacket(sequenceNumber: 1, timestamp: 1), arrivalTimestampInRtpClock: 1);

        for (int i = 0; i < 1_000; i++)
        {
            Assert.True(reporter.TryCreateReceiverReport(99, 0x01020304, DateTimeOffset.UnixEpoch, blocks, out _));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (!reporter.TryCreateReceiverReport(99, 0x01020304, DateTimeOffset.UnixEpoch, blocks, out _))
            {
                throw new InvalidOperationException("RTCP telemetry report creation failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    private static RtpPacket CreatePacket(ushort sequenceNumber, uint timestamp, uint ssrc = 0x01020304)
    {
        return new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                SequenceNumber = sequenceNumber,
                Timestamp = timestamp,
                Ssrc = ssrc
            },
            Payload = ReadOnlyMemory<byte>.Empty,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class ThrowingBufferWriter : IBufferWriter<byte>
    {
        public int AdvanceCallCount { get; private set; }

        public int GetMemoryCallCount { get; private set; }

        public int GetSpanCallCount { get; private set; }

        public void Advance(int count)
        {
            AdvanceCallCount++;
            throw new InvalidOperationException("Advance should not be called.");
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            GetMemoryCallCount++;
            throw new InvalidOperationException("GetMemory should not be called.");
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            GetSpanCallCount++;
            throw new InvalidOperationException("GetSpan should not be called.");
        }
    }
}
