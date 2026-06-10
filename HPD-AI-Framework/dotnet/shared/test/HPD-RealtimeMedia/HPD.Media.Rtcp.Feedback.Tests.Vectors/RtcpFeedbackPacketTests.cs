#nullable enable

using System.Buffers;
using HPD.Media.Rtcp;
using HPD.Media.Rtcp.Feedback;

namespace HPD.Media.Rtcp.Feedback.Tests.Vectors;

public sealed class RtcpFeedbackPacketTests
{
    [Fact]
    public void TryParseGenericFeedback_ReadsUnknownPayloadFeedbackPacket()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCE, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParse(packet, out RtcpFeedbackPacketView view);
        ReadOnlySpan<byte> expectedFeedbackControlInformation = [0xAA, 0xBB, 0xCC, 0xDD];

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(RtcpPacketType.PayloadFeedback, view.PacketType);
        Assert.Equal(15, view.FeedbackMessageType);
        Assert.Equal(0x01020304u, view.SenderSsrc);
        Assert.Equal(0x05060708u, view.MediaSsrc);
        Assert.True(view.FeedbackControlInformation.SequenceEqual(expectedFeedbackControlInformation));
    }

    [Fact]
    public void TryWriteGenericFeedback_WritesPacketThatParsesBack()
    {
        byte[] feedbackControlInformation = [0xAA, 0xBB, 0xCC, 0xDD];
        var feedback = new RtcpFeedbackPacket
        {
            PacketType = RtcpPacketType.TransportFeedback,
            FeedbackMessageType = 15,
            SenderSsrc = 0x01020304,
            MediaSsrc = 0x05060708,
            FeedbackControlInformation = feedbackControlInformation
        };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus writeStatus = RtcpFeedbackPacketWriter.TryWrite(feedback, destination, out int bytesWritten);
        RtcpPacketStatus parseStatus = RtcpFeedbackPacketReader.TryParse(destination[..bytesWritten], out RtcpFeedbackPacketView parsed);

        Assert.Equal(RtcpPacketStatus.Success, writeStatus);
        Assert.Equal(16, bytesWritten);
        Assert.Equal(RtcpPacketStatus.Success, parseStatus);
        Assert.Equal(RtcpPacketType.TransportFeedback, parsed.PacketType);
        Assert.Equal(15, parsed.FeedbackMessageType);
        Assert.True(parsed.FeedbackControlInformation.SequenceEqual(feedbackControlInformation));
    }

    [Fact]
    public void TryWriteGenericFeedback_RejectsUnpaddedFeedbackControlInformation()
    {
        byte[] feedbackControlInformation = [0xAA, 0xBB];
        var feedback = new RtcpFeedbackPacket
        {
            PacketType = RtcpPacketType.PayloadFeedback,
            FeedbackMessageType = 15,
            SenderSsrc = 1,
            MediaSsrc = 2,
            FeedbackControlInformation = feedbackControlInformation
        };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWrite(feedback, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryParseGenericFeedback_RejectsReservedZeroFeedbackMessageType()
    {
        ReadOnlySpan<byte> packet =
        [
            0x80, 0xCE, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0xAA, 0xBB, 0xCC, 0xDD
        ];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParse(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWriteGenericFeedback_RejectsReservedZeroFeedbackMessageType()
    {
        byte[] feedbackControlInformation = [0xAA, 0xBB, 0xCC, 0xDD];
        var feedback = new RtcpFeedbackPacket
        {
            PacketType = RtcpPacketType.PayloadFeedback,
            FeedbackMessageType = 0,
            SenderSsrc = 1,
            MediaSsrc = 2,
            FeedbackControlInformation = feedbackControlInformation
        };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWrite(feedback, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWriteGenericFeedback_RejectsPacketLengthBeyondRtcpHeaderLimit()
    {
        byte[] feedbackControlInformation = new byte[(ushort.MaxValue + 1) * 4];
        var feedback = new RtcpFeedbackPacket
        {
            PacketType = RtcpPacketType.PayloadFeedback,
            FeedbackMessageType = 15,
            SenderSsrc = 1,
            MediaSsrc = 2,
            FeedbackControlInformation = feedbackControlInformation
        };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWrite(feedback, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryParseGenericFeedback_RejectsPaddingBit()
    {
        ReadOnlySpan<byte> packet =
        [
            0xAF, 0xCE, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0xAA, 0xBB, 0xCC, 0x04
        ];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParse(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseGenericNack_RejectsPaddingBit()
    {
        ReadOnlySpan<byte> packet =
        [
            0xA1, 0xCD, 0x00, 0x03,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88,
            0x12, 0x34, 0x00, 0x04
        ];
        RtcpNackEntry[] entries = new RtcpNackEntry[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseGenericNack(packet, entries, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void GenericFeedback_ParseAndWriteDoNotAllocate()
    {
        byte[] feedbackControlInformation = [0xAA, 0xBB, 0xCC, 0xDD];
        var feedback = new RtcpFeedbackPacket
        {
            PacketType = RtcpPacketType.PayloadFeedback,
            FeedbackMessageType = 15,
            SenderSsrc = 1,
            MediaSsrc = 2,
            FeedbackControlInformation = feedbackControlInformation
        };
        Span<byte> packet = stackalloc byte[16];
        Assert.Equal(RtcpPacketStatus.Success, RtcpFeedbackPacketWriter.TryWrite(feedback, packet, out int bytesWritten));

        for (int i = 0; i < 32; i++)
        {
            if (RtcpFeedbackPacketReader.TryParse(packet[..bytesWritten], out RtcpFeedbackPacketView warmup) != RtcpPacketStatus.Success ||
                warmup.FeedbackMessageType != 15)
            {
                throw new InvalidOperationException("RTCP generic feedback warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            Span<byte> destination = stackalloc byte[16];
            if (RtcpFeedbackPacketWriter.TryWrite(feedback, destination, out int written) != RtcpPacketStatus.Success ||
                RtcpFeedbackPacketReader.TryParse(destination[..written], out RtcpFeedbackPacketView parsed) != RtcpPacketStatus.Success ||
                parsed.FeedbackControlInformation.Length != 4)
            {
                throw new InvalidOperationException("RTCP generic feedback parse/write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TryParseGenericNack_ReadsHeaderAndEntries()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCD, 0x00, 0x03,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88,
            0x12, 0x34, 0x00, 0x05
        ];
        RtcpNackEntry[] entries = new RtcpNackEntry[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseGenericNack(packet, entries, out RtcpNackPacket nack);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(0x11223344u, nack.SenderSsrc);
        Assert.Equal(0x55667788u, nack.MediaSsrc);
        RtcpNackEntry entry = Assert.Single(nack.Entries.ToArray());
        Assert.Equal(0x1234, entry.PacketId);
        Assert.Equal(0x0005, entry.LostPacketBitmask);
        Assert.True(entry.Contains(0x1234));
        Assert.True(entry.Contains(0x1235));
        Assert.False(entry.Contains(0x1236));
        Assert.True(entry.Contains(0x1237));
    }

    [Fact]
    public void TryWriteGenericNack_WritesPacketThatParsesBack()
    {
        RtcpNackEntry[] entries =
        [
            new()
            {
                PacketId = 0x1200,
                LostPacketBitmask = 0x8001
            }
        ];
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 0x01020304,
            MediaSsrc = 0xAABBCCDD,
            Entries = entries
        };
        Span<byte> destination = stackalloc byte[16];
        RtcpNackEntry[] parsedEntries = new RtcpNackEntry[1];

        RtcpPacketStatus writeStatus = RtcpFeedbackPacketWriter.TryWriteGenericNack(nack, destination, out int bytesWritten);
        RtcpPacketStatus parseStatus = RtcpFeedbackPacketReader.TryParseGenericNack(destination[..bytesWritten], parsedEntries, out RtcpNackPacket parsed);

        Assert.Equal(RtcpPacketStatus.Success, writeStatus);
        Assert.Equal(16, bytesWritten);
        Assert.Equal(RtcpPacketStatus.Success, parseStatus);
        Assert.Equal(nack.SenderSsrc, parsed.SenderSsrc);
        Assert.Equal(nack.MediaSsrc, parsed.MediaSsrc);
        Assert.Equal(0x1200, parsed.Entries.Span[0].PacketId);
        Assert.Equal(0x8001, parsed.Entries.Span[0].LostPacketBitmask);
    }

    [Fact]
    public void TryParseGenericNack_ReturnsDestinationTooSmall()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCD, 0x00, 0x03,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88,
            0x12, 0x34, 0x00, 0x00
        ];
        RtcpNackEntry[] entries = [];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseGenericNack(packet, entries, out _);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
    }

    [Fact]
    public void TryParseGenericNack_RejectsEmptyFeedbackControlInformation()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCD, 0x00, 0x02,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88
        ];
        RtcpNackEntry[] entries = new RtcpNackEntry[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseGenericNack(packet, entries, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWriteGenericNack_RejectsEmptyFeedbackControlInformation()
    {
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            Entries = ReadOnlyMemory<RtcpNackEntry>.Empty
        };
        Span<byte> destination = stackalloc byte[12];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWriteGenericNack(nack, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWriteGenericNack_RejectsPacketLengthBeyondRtcpHeaderLimit()
    {
        RtcpNackEntry[] entries = new RtcpNackEntry[65_534];
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            Entries = entries
        };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWriteGenericNack(nack, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWritePictureLossIndication_WritesPacketThatParsesBack()
    {
        var pli = new RtcpPictureLossIndication
        {
            SenderSsrc = 0x01020304,
            MediaSsrc = 0xAABBCCDD
        };
        Span<byte> destination = stackalloc byte[12];

        RtcpPacketStatus writeStatus = RtcpFeedbackPacketWriter.TryWritePictureLossIndication(pli, destination, out int bytesWritten);
        RtcpPacketStatus parseStatus = RtcpFeedbackPacketReader.TryParsePictureLossIndication(
            destination[..bytesWritten],
            out RtcpPictureLossIndication parsed);

        Assert.Equal(RtcpPacketStatus.Success, writeStatus);
        Assert.Equal(12, bytesWritten);
        Assert.Equal(RtcpPacketStatus.Success, parseStatus);
        Assert.Equal(pli.SenderSsrc, parsed.SenderSsrc);
        Assert.Equal(pli.MediaSsrc, parsed.MediaSsrc);
        Assert.Equal(0x81, destination[0]);
        Assert.Equal(0xCE, destination[1]);
        Assert.Equal([0x00, 0x02], destination.Slice(2, 2).ToArray());
    }

    [Fact]
    public void TryParsePictureLossIndication_RejectsWrongPacketType()
    {
        ReadOnlySpan<byte> packet =
        [
            0x81, 0xCD, 0x00, 0x02,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08
        ];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParsePictureLossIndication(packet, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWritePictureLossIndication_ReturnsDestinationTooSmall()
    {
        var pli = new RtcpPictureLossIndication
        {
            SenderSsrc = 1,
            MediaSsrc = 2
        };
        Span<byte> destination = stackalloc byte[8];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWritePictureLossIndication(pli, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void WritePictureLossIndication_BufferWriterWritesExpectedBytes()
    {
        var pli = new RtcpPictureLossIndication
        {
            SenderSsrc = 1,
            MediaSsrc = 2
        };
        var writer = new ArrayBufferWriter<byte>();

        RtcpFeedbackPacketWriter.WritePictureLossIndication(pli, writer);

        Assert.Equal(12, writer.WrittenCount);
        Assert.Equal(0x81, writer.WrittenSpan[0]);
        Assert.Equal(0xCE, writer.WrittenSpan[1]);
    }

    [Fact]
    public void TryWriteFullIntraRequest_WritesPacketThatParsesBack()
    {
        RtcpFullIntraRequestEntry[] entries =
        [
            new()
            {
                Ssrc = 0x10203040,
                SequenceNumber = 7
            }
        ];
        var fir = new RtcpFullIntraRequest
        {
            SenderSsrc = 0x01020304,
            MediaSsrc = 0,
            Entries = entries
        };
        Span<byte> destination = stackalloc byte[20];
        RtcpFullIntraRequestEntry[] parsedEntries = new RtcpFullIntraRequestEntry[1];

        RtcpPacketStatus writeStatus = RtcpFeedbackPacketWriter.TryWriteFullIntraRequest(fir, destination, out int bytesWritten);
        RtcpPacketStatus parseStatus = RtcpFeedbackPacketReader.TryParseFullIntraRequest(
            destination[..bytesWritten],
            parsedEntries,
            out RtcpFullIntraRequest parsed);

        Assert.Equal(RtcpPacketStatus.Success, writeStatus);
        Assert.Equal(20, bytesWritten);
        Assert.Equal(RtcpPacketStatus.Success, parseStatus);
        Assert.Equal(fir.SenderSsrc, parsed.SenderSsrc);
        Assert.Equal(fir.MediaSsrc, parsed.MediaSsrc);
        Assert.Equal(0x84, destination[0]);
        Assert.Equal(0xCE, destination[1]);
        Assert.Equal(0x10203040u, parsed.Entries.Span[0].Ssrc);
        Assert.Equal(7, parsed.Entries.Span[0].SequenceNumber);
        Assert.Equal(0, destination[17]);
        Assert.Equal(0, destination[18]);
        Assert.Equal(0, destination[19]);
    }

    [Fact]
    public void TryParseFullIntraRequest_ReturnsDestinationTooSmall()
    {
        ReadOnlySpan<byte> packet =
        [
            0x84, 0xCE, 0x00, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x10, 0x20, 0x30, 0x40,
            0x07, 0x00, 0x00, 0x00
        ];
        RtcpFullIntraRequestEntry[] entries = [];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseFullIntraRequest(packet, entries, out _);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
    }

    [Fact]
    public void TryParseFullIntraRequest_RejectsMalformedEntryLength()
    {
        ReadOnlySpan<byte> packet =
        [
            0x84, 0xCE, 0x00, 0x03,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x10, 0x20, 0x30, 0x40
        ];
        RtcpFullIntraRequestEntry[] entries = new RtcpFullIntraRequestEntry[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseFullIntraRequest(packet, entries, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseFullIntraRequest_RejectsEmptyFeedbackControlInformation()
    {
        ReadOnlySpan<byte> packet =
        [
            0x84, 0xCE, 0x00, 0x02,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00
        ];
        RtcpFullIntraRequestEntry[] entries = new RtcpFullIntraRequestEntry[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseFullIntraRequest(packet, entries, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWriteFullIntraRequest_RejectsEmptyFeedbackControlInformation()
    {
        var fir = new RtcpFullIntraRequest
        {
            SenderSsrc = 1,
            MediaSsrc = 0,
            Entries = ReadOnlyMemory<RtcpFullIntraRequestEntry>.Empty
        };
        Span<byte> destination = stackalloc byte[12];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWriteFullIntraRequest(fir, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryParseFullIntraRequest_RejectsNonZeroReservedEntryBytes()
    {
        ReadOnlySpan<byte> packet =
        [
            0x84, 0xCE, 0x00, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x10, 0x20, 0x30, 0x40,
            0x07, 0x00, 0x01, 0x00
        ];
        RtcpFullIntraRequestEntry[] entries = new RtcpFullIntraRequestEntry[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseFullIntraRequest(packet, entries, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWriteFullIntraRequest_RejectsPacketLengthBeyondRtcpHeaderLimit()
    {
        RtcpFullIntraRequestEntry[] entries = new RtcpFullIntraRequestEntry[32_767];
        var fir = new RtcpFullIntraRequest
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            Entries = entries
        };
        Span<byte> destination = stackalloc byte[16];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWriteFullIntraRequest(fir, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryParseReceiverEstimatedMaximumBitrate_ReadsHeaderBitrateAndSsrcs()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCE, 0x00, 0x05,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x52, 0x45, 0x4D, 0x42,
            0x01, 0x0B, 0xD0, 0x90,
            0x10, 0x20, 0x30, 0x40
        ];
        uint[] ssrcs = new uint[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(
            packet,
            ssrcs,
            out RtcpReceiverEstimatedMaximumBitrate remb);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(0x01020304u, remb.SenderSsrc);
        Assert.Equal(0u, remb.MediaSsrc);
        Assert.Equal(1_000_000ul, remb.BitrateBitsPerSecond);
        Assert.Equal(0x10203040u, Assert.Single(remb.Ssrcs.ToArray()));
    }

    [Fact]
    public void TryWriteReceiverEstimatedMaximumBitrate_WritesPacketThatParsesBack()
    {
        uint[] ssrcs = [0x10203040, 0x50607080];
        var remb = new RtcpReceiverEstimatedMaximumBitrate
        {
            SenderSsrc = 0x01020304,
            MediaSsrc = 0,
            BitrateBitsPerSecond = 1_000_000,
            Ssrcs = ssrcs
        };
        Span<byte> destination = stackalloc byte[28];
        uint[] parsedSsrcs = new uint[2];

        RtcpPacketStatus writeStatus = RtcpFeedbackPacketWriter.TryWriteReceiverEstimatedMaximumBitrate(
            remb,
            destination,
            out int bytesWritten);
        RtcpPacketStatus parseStatus = RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(
            destination[..bytesWritten],
            parsedSsrcs,
            out RtcpReceiverEstimatedMaximumBitrate parsed);

        Assert.Equal(RtcpPacketStatus.Success, writeStatus);
        Assert.Equal(28, bytesWritten);
        Assert.Equal(RtcpPacketStatus.Success, parseStatus);
        Assert.Equal(0x8F, destination[0]);
        Assert.Equal(0xCE, destination[1]);
        Assert.Equal([0x00, 0x06], destination.Slice(2, 2).ToArray());
        Assert.Equal(1_000_000ul, parsed.BitrateBitsPerSecond);
        Assert.Equal(ssrcs, parsed.Ssrcs.ToArray());
    }

    [Fact]
    public void TryParseReceiverEstimatedMaximumBitrate_ReturnsDestinationTooSmall()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCE, 0x00, 0x05,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x52, 0x45, 0x4D, 0x42,
            0x01, 0x0B, 0xD0, 0x90,
            0x10, 0x20, 0x30, 0x40
        ];
        uint[] ssrcs = [];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(packet, ssrcs, out _);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
    }

    [Fact]
    public void TryParseReceiverEstimatedMaximumBitrate_RejectsMismatchedSsrcCount()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCE, 0x00, 0x05,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x52, 0x45, 0x4D, 0x42,
            0x02, 0x0B, 0xD0, 0x90,
            0x10, 0x20, 0x30, 0x40
        ];
        uint[] ssrcs = new uint[2];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(packet, ssrcs, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseReceiverEstimatedMaximumBitrate_RejectsZeroSsrcCount()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCE, 0x00, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x52, 0x45, 0x4D, 0x42,
            0x00, 0x0B, 0xD0, 0x90
        ];
        uint[] ssrcs = [];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(packet, ssrcs, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParseReceiverEstimatedMaximumBitrate_RejectsOverflowingBitrate()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCE, 0x00, 0x05,
            0x01, 0x02, 0x03, 0x04,
            0x00, 0x00, 0x00, 0x00,
            0x52, 0x45, 0x4D, 0x42,
            0x01, 0xFF, 0xFF, 0xFF,
            0x10, 0x20, 0x30, 0x40
        ];
        uint[] ssrcs = new uint[1];

        RtcpPacketStatus status = RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(packet, ssrcs, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWriteReceiverEstimatedMaximumBitrate_RejectsZeroSsrcCount()
    {
        var remb = new RtcpReceiverEstimatedMaximumBitrate
        {
            SenderSsrc = 1,
            MediaSsrc = 0,
            BitrateBitsPerSecond = 1_000_000,
            Ssrcs = Array.Empty<uint>()
        };
        Span<byte> destination = stackalloc byte[20];

        RtcpPacketStatus status = RtcpFeedbackPacketWriter.TryWriteReceiverEstimatedMaximumBitrate(
            remb,
            destination,
            out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void WriteReceiverEstimatedMaximumBitrate_BufferWriterWritesExpectedBytes()
    {
        uint[] ssrcs = [0x10203040];
        var remb = new RtcpReceiverEstimatedMaximumBitrate
        {
            SenderSsrc = 1,
            MediaSsrc = 0,
            BitrateBitsPerSecond = 1_000_000,
            Ssrcs = ssrcs
        };
        var writer = new ArrayBufferWriter<byte>();

        RtcpFeedbackPacketWriter.WriteReceiverEstimatedMaximumBitrate(remb, writer);

        Assert.Equal(24, writer.WrittenCount);
        Assert.Equal(0x8F, writer.WrittenSpan[0]);
        Assert.Equal(0xCE, writer.WrittenSpan[1]);
        Assert.Equal((byte)'R', writer.WrittenSpan[12]);
        Assert.Equal((byte)'E', writer.WrittenSpan[13]);
        Assert.Equal((byte)'M', writer.WrittenSpan[14]);
        Assert.Equal((byte)'B', writer.WrittenSpan[15]);
    }

    [Fact]
    public void WriteGenericFeedback_ValidatesBeforeRequestingDestinationStorage()
    {
        byte[] feedbackControlInformation = [0xAA, 0xBB];
        var feedback = new RtcpFeedbackPacket
        {
            PacketType = RtcpPacketType.PayloadFeedback,
            FeedbackMessageType = 15,
            SenderSsrc = 1,
            MediaSsrc = 2,
            FeedbackControlInformation = feedbackControlInformation
        };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtcpFeedbackPacketWriter.Write(feedback, writer));

        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void WriteGenericNack_ValidatesBeforeRequestingDestinationStorage()
    {
        RtcpNackEntry[] entries = new RtcpNackEntry[65_534];
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            Entries = entries
        };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtcpFeedbackPacketWriter.WriteGenericNack(nack, writer));

        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void WriteFullIntraRequest_ValidatesBeforeRequestingDestinationStorage()
    {
        RtcpFullIntraRequestEntry[] entries = new RtcpFullIntraRequestEntry[32_767];
        var fir = new RtcpFullIntraRequest
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            Entries = entries
        };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtcpFeedbackPacketWriter.WriteFullIntraRequest(fir, writer));

        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void WriteReceiverEstimatedMaximumBitrate_ValidatesBeforeRequestingDestinationStorage()
    {
        var remb = new RtcpReceiverEstimatedMaximumBitrate
        {
            SenderSsrc = 1,
            MediaSsrc = 0,
            BitrateBitsPerSecond = 1_000_000,
            Ssrcs = Array.Empty<uint>()
        };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => RtcpFeedbackPacketWriter.WriteReceiverEstimatedMaximumBitrate(remb, writer));

        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void GenericNack_ParseAndWriteDoNotAllocate()
    {
        RtcpNackEntry[] entries =
        [
            new()
            {
                PacketId = 0x1200,
                LostPacketBitmask = 0x0001
            }
        ];
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            Entries = entries
        };
        Span<byte> packet = stackalloc byte[16];
        RtcpNackEntry[] parsedEntries = new RtcpNackEntry[1];
        Assert.Equal(RtcpPacketStatus.Success, RtcpFeedbackPacketWriter.TryWriteGenericNack(nack, packet, out int bytesWritten));

        for (int i = 0; i < 32; i++)
        {
            if (RtcpFeedbackPacketReader.TryParseGenericNack(packet[..bytesWritten], parsedEntries, out RtcpNackPacket warmup) != RtcpPacketStatus.Success ||
                warmup.Entries.Length != 1)
            {
                throw new InvalidOperationException("RTCP Generic NACK warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            Span<byte> destination = stackalloc byte[16];
            if (RtcpFeedbackPacketWriter.TryWriteGenericNack(nack, destination, out int written) != RtcpPacketStatus.Success ||
                RtcpFeedbackPacketReader.TryParseGenericNack(destination[..written], parsedEntries, out RtcpNackPacket parsed) != RtcpPacketStatus.Success ||
                parsed.Entries.Span[0].PacketId != 0x1200)
            {
                throw new InvalidOperationException("RTCP Generic NACK parse/write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FullIntraRequest_ParseAndWriteDoNotAllocate()
    {
        RtcpFullIntraRequestEntry[] entries =
        [
            new()
            {
                Ssrc = 0x10203040,
                SequenceNumber = 1
            }
        ];
        var fir = new RtcpFullIntraRequest
        {
            SenderSsrc = 1,
            MediaSsrc = 0,
            Entries = entries
        };
        Span<byte> packet = stackalloc byte[20];
        RtcpFullIntraRequestEntry[] parsedEntries = new RtcpFullIntraRequestEntry[1];
        Assert.Equal(RtcpPacketStatus.Success, RtcpFeedbackPacketWriter.TryWriteFullIntraRequest(fir, packet, out int bytesWritten));

        for (int i = 0; i < 32; i++)
        {
            if (RtcpFeedbackPacketReader.TryParseFullIntraRequest(packet[..bytesWritten], parsedEntries, out RtcpFullIntraRequest warmup) != RtcpPacketStatus.Success ||
                warmup.Entries.Length != 1)
            {
                throw new InvalidOperationException("RTCP FIR warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            Span<byte> destination = stackalloc byte[20];
            if (RtcpFeedbackPacketWriter.TryWriteFullIntraRequest(fir, destination, out int written) != RtcpPacketStatus.Success ||
                RtcpFeedbackPacketReader.TryParseFullIntraRequest(destination[..written], parsedEntries, out RtcpFullIntraRequest parsed) != RtcpPacketStatus.Success ||
                parsed.Entries.Span[0].Ssrc != 0x10203040)
            {
                throw new InvalidOperationException("RTCP FIR parse/write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PictureLossIndication_ParseAndWriteDoNotAllocate()
    {
        var pli = new RtcpPictureLossIndication
        {
            SenderSsrc = 1,
            MediaSsrc = 2
        };
        Span<byte> packet = stackalloc byte[12];
        Assert.Equal(RtcpPacketStatus.Success, RtcpFeedbackPacketWriter.TryWritePictureLossIndication(pli, packet, out int bytesWritten));

        for (int i = 0; i < 32; i++)
        {
            if (RtcpFeedbackPacketReader.TryParsePictureLossIndication(packet[..bytesWritten], out RtcpPictureLossIndication warmup) != RtcpPacketStatus.Success ||
                warmup.MediaSsrc != 2)
            {
                throw new InvalidOperationException("RTCP PLI warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            Span<byte> destination = stackalloc byte[12];
            if (RtcpFeedbackPacketWriter.TryWritePictureLossIndication(pli, destination, out int written) != RtcpPacketStatus.Success ||
                RtcpFeedbackPacketReader.TryParsePictureLossIndication(destination[..written], out RtcpPictureLossIndication parsed) != RtcpPacketStatus.Success ||
                parsed.SenderSsrc != 1)
            {
                throw new InvalidOperationException("RTCP PLI parse/write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReceiverEstimatedMaximumBitrate_ParseAndWriteDoNotAllocate()
    {
        uint[] ssrcs = [0x10203040];
        var remb = new RtcpReceiverEstimatedMaximumBitrate
        {
            SenderSsrc = 1,
            MediaSsrc = 0,
            BitrateBitsPerSecond = 1_000_000,
            Ssrcs = ssrcs
        };
        Span<byte> packet = stackalloc byte[24];
        uint[] parsedSsrcs = new uint[1];
        Assert.Equal(
            RtcpPacketStatus.Success,
            RtcpFeedbackPacketWriter.TryWriteReceiverEstimatedMaximumBitrate(remb, packet, out int bytesWritten));

        for (int i = 0; i < 32; i++)
        {
            if (RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(
                    packet[..bytesWritten],
                    parsedSsrcs,
                    out RtcpReceiverEstimatedMaximumBitrate warmup) != RtcpPacketStatus.Success ||
                warmup.BitrateBitsPerSecond != 1_000_000)
            {
                throw new InvalidOperationException("RTCP REMB warmup failed.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            Span<byte> destination = stackalloc byte[24];
            if (RtcpFeedbackPacketWriter.TryWriteReceiverEstimatedMaximumBitrate(remb, destination, out int written) != RtcpPacketStatus.Success ||
                RtcpFeedbackPacketReader.TryParseReceiverEstimatedMaximumBitrate(
                    destination[..written],
                    parsedSsrcs,
                    out RtcpReceiverEstimatedMaximumBitrate parsed) != RtcpPacketStatus.Success ||
                parsed.Ssrcs.Span[0] != 0x10203040)
            {
                throw new InvalidOperationException("RTCP REMB parse/write failed during allocation measurement.");
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WriteGenericNack_BufferWriterWritesExpectedBytes()
    {
        RtcpNackEntry[] entries =
        [
            new()
            {
                PacketId = 10,
                LostPacketBitmask = 1
            }
        ];
        var nack = new RtcpNackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            Entries = entries
        };
        var writer = new ArrayBufferWriter<byte>();

        RtcpFeedbackPacketWriter.WriteGenericNack(nack, writer);

        Assert.Equal(16, writer.WrittenCount);
        Assert.Equal(0x81, writer.WrittenSpan[0]);
        Assert.Equal(0xCD, writer.WrittenSpan[1]);
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
