#nullable enable

using System.Buffers;
using HPD.Media.Rtcp;
using HPD.Media.Rtcp.Twcc;

namespace HPD.Media.Rtcp.Twcc.Tests.Vectors;

public sealed class RtcpTwccPacketTests
{
    [Fact]
    public void FeedbackBuilder_BuildsLossAndDeltaStatuses()
    {
        RtcpTwccReceivedPacket[] received =
        [
            new() { SequenceNumber = 1000, ArrivalTime250Microseconds = 1024 + 4 },
            new() { SequenceNumber = 1002, ArrivalTime250Microseconds = 1024 + 9 },
            new() { SequenceNumber = 1003, ArrivalTime250Microseconds = 1024 - 3 }
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[4];

        RtcpPacketStatus status = RtcpTwccFeedbackBuilder.TryBuild(
            senderSsrc: 0x01020304,
            mediaSsrc: 0xAABBCCDD,
            feedbackPacketCount: 12,
            received,
            statuses,
            out RtcpTwccFeedbackPacket feedback);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(0x01020304u, feedback.SenderSsrc);
        Assert.Equal(0xAABBCCDDu, feedback.MediaSsrc);
        Assert.Equal(1000, feedback.BaseSequenceNumber);
        Assert.Equal(4, feedback.PacketStatuses.Length);
        Assert.Equal(4u, feedback.ReferenceTime64Milliseconds);
        Assert.Equal(12, feedback.FeedbackPacketCount);
        Assert.Equal(RtcpTwccPacketStatusSymbol.SmallDelta, feedback.PacketStatuses.Span[0].Symbol);
        Assert.Equal(4, feedback.PacketStatuses.Span[0].Delta250Microseconds);
        Assert.Equal(RtcpTwccPacketStatusSymbol.NotReceived, feedback.PacketStatuses.Span[1].Symbol);
        Assert.Equal(RtcpTwccPacketStatusSymbol.SmallDelta, feedback.PacketStatuses.Span[2].Symbol);
        Assert.Equal(5, feedback.PacketStatuses.Span[2].Delta250Microseconds);
        Assert.Equal(RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta, feedback.PacketStatuses.Span[3].Symbol);
        Assert.Equal(-12, feedback.PacketStatuses.Span[3].Delta250Microseconds);
    }

    [Fact]
    public void FeedbackBuilder_HandlesSequenceWrap()
    {
        RtcpTwccReceivedPacket[] received =
        [
            new() { SequenceNumber = 65534, ArrivalTime250Microseconds = 2048 + 1 },
            new() { SequenceNumber = 0, ArrivalTime250Microseconds = 2048 + 3 }
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[4];

        RtcpPacketStatus status = RtcpTwccFeedbackBuilder.TryBuild(
            senderSsrc: 1,
            mediaSsrc: 2,
            feedbackPacketCount: 3,
            received,
            statuses,
            out RtcpTwccFeedbackPacket feedback);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(65534, feedback.BaseSequenceNumber);
        Assert.Equal(3, feedback.PacketStatuses.Length);
        Assert.Equal(RtcpTwccPacketStatusSymbol.SmallDelta, feedback.PacketStatuses.Span[0].Symbol);
        Assert.Equal(RtcpTwccPacketStatusSymbol.NotReceived, feedback.PacketStatuses.Span[1].Symbol);
        Assert.Equal(RtcpTwccPacketStatusSymbol.SmallDelta, feedback.PacketStatuses.Span[2].Symbol);
    }

    [Fact]
    public void FeedbackBuilder_ReturnsStatusForInvalidInputs()
    {
        RtcpTwccReceivedPacket[] received =
        [
            new() { SequenceNumber = 10, ArrivalTime250Microseconds = 0 },
            new() { SequenceNumber = 11, ArrivalTime250Microseconds = short.MaxValue + 1L }
        ];
        RtcpTwccPacketStatus[] oneStatus = new RtcpTwccPacketStatus[1];
        RtcpTwccPacketStatus[] twoStatuses = new RtcpTwccPacketStatus[2];

        RtcpPacketStatus emptyStatus = RtcpTwccFeedbackBuilder.TryBuild(1, 2, 3, [], twoStatuses, out _);
        RtcpPacketStatus smallDestinationStatus = RtcpTwccFeedbackBuilder.TryBuild(1, 2, 3, received, oneStatus, out _);
        RtcpPacketStatus largeDeltaStatus = RtcpTwccFeedbackBuilder.TryBuild(1, 2, 3, received, twoStatuses, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, emptyStatus);
        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, smallDestinationStatus);
        Assert.Equal(RtcpPacketStatus.InvalidPacket, largeDeltaStatus);
    }

    [Fact]
    public void FeedbackBuilder_RejectsSequenceSpanBeyondWireStatusCount()
    {
        RtcpTwccReceivedPacket[] received =
        [
            new() { SequenceNumber = 0, ArrivalTime250Microseconds = 0 },
            new() { SequenceNumber = ushort.MaxValue, ArrivalTime250Microseconds = 1 }
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[ushort.MaxValue + 1];

        RtcpPacketStatus status = RtcpTwccFeedbackBuilder.TryBuild(
            senderSsrc: 1,
            mediaSsrc: 2,
            feedbackPacketCount: 3,
            received,
            statuses,
            out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWrite_WritesRunLengthChunksThatParseBack()
    {
        RtcpTwccPacketStatus[] statuses =
        [
            new() { Symbol = RtcpTwccPacketStatusSymbol.SmallDelta, Delta250Microseconds = 4 },
            new() { Symbol = RtcpTwccPacketStatusSymbol.SmallDelta, Delta250Microseconds = 8 },
            new() { Symbol = RtcpTwccPacketStatusSymbol.NotReceived },
            new() { Symbol = RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta, Delta250Microseconds = -12 }
        ];
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 0x01020304,
            MediaSsrc = 0xAABBCCDD,
            BaseSequenceNumber = 0x1234,
            ReferenceTime64Milliseconds = 0x00A1B2C3,
            FeedbackPacketCount = 9,
            PacketStatuses = statuses
        };
        Span<byte> destination = stackalloc byte[64];
        RtcpTwccPacketStatus[] parsedStatuses = new RtcpTwccPacketStatus[4];

        RtcpPacketStatus writeStatus = RtcpTwccPacketWriter.TryWrite(feedback, destination, out int bytesWritten);
        RtcpPacketStatus parseStatus = RtcpTwccPacketReader.TryParse(
            destination[..bytesWritten],
            parsedStatuses,
            out RtcpTwccFeedbackPacket parsed);

        Assert.Equal(RtcpPacketStatus.Success, writeStatus);
        Assert.Equal(32, bytesWritten);
        Assert.Equal(RtcpPacketStatus.Success, parseStatus);
        Assert.Equal(feedback.SenderSsrc, parsed.SenderSsrc);
        Assert.Equal(feedback.MediaSsrc, parsed.MediaSsrc);
        Assert.Equal(feedback.BaseSequenceNumber, parsed.BaseSequenceNumber);
        Assert.Equal(feedback.ReferenceTime64Milliseconds, parsed.ReferenceTime64Milliseconds);
        Assert.Equal(feedback.FeedbackPacketCount, parsed.FeedbackPacketCount);
        Assert.Equal(RtcpTwccPacketStatusSymbol.SmallDelta, parsed.PacketStatuses.Span[0].Symbol);
        Assert.Equal(4, parsed.PacketStatuses.Span[0].Delta250Microseconds);
        Assert.Equal(8, parsed.PacketStatuses.Span[1].Delta250Microseconds);
        Assert.Equal(RtcpTwccPacketStatusSymbol.NotReceived, parsed.PacketStatuses.Span[2].Symbol);
        Assert.Equal(-12, parsed.PacketStatuses.Span[3].Delta250Microseconds);
    }

    [Fact]
    public void TryParse_ReadsTwoBitStatusVectorChunk()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCD, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x10, 0x00, 0x00, 0x03,
            0x00, 0x01, 0x02, 0x03,
            0xD8, 0x00,
            0x05, 0xFF, 0xEC,
            0x00, 0x00, 0x00
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[3];

        RtcpPacketStatus status = RtcpTwccPacketReader.TryParse(packet, statuses, out RtcpTwccFeedbackPacket feedback);

        Assert.Equal(RtcpPacketStatus.Success, status);
        Assert.Equal(0x01020304u, feedback.SenderSsrc);
        Assert.Equal(0x05060708u, feedback.MediaSsrc);
        Assert.Equal(0x1000, feedback.BaseSequenceNumber);
        Assert.Equal(0x000102u, feedback.ReferenceTime64Milliseconds);
        Assert.Equal(3, feedback.FeedbackPacketCount);
        Assert.Equal(RtcpTwccPacketStatusSymbol.SmallDelta, feedback.PacketStatuses.Span[0].Symbol);
        Assert.Equal(5, feedback.PacketStatuses.Span[0].Delta250Microseconds);
        Assert.Equal(RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta, feedback.PacketStatuses.Span[1].Symbol);
        Assert.Equal(-20, feedback.PacketStatuses.Span[1].Delta250Microseconds);
        Assert.Equal(RtcpTwccPacketStatusSymbol.NotReceived, feedback.PacketStatuses.Span[2].Symbol);
    }

    [Fact]
    public void TryParse_ReturnsDestinationTooSmall()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCD, 0x00, 0x05,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x10, 0x00, 0x00, 0x02,
            0x00, 0x01, 0x02, 0x03,
            0x20, 0x02,
            0x05, 0x06
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[1];

        RtcpPacketStatus status = RtcpTwccPacketReader.TryParse(packet, statuses, out _);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
    }

    [Fact]
    public void TryParse_RejectsZeroLengthRunLengthChunk()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCD, 0x00, 0x05,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x10, 0x00, 0x00, 0x01,
            0x00, 0x01, 0x02, 0x03,
            0x20, 0x00,
            0x00, 0x00
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[1];

        RtcpPacketStatus status = RtcpTwccPacketReader.TryParse(packet, statuses, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParse_RejectsZeroPacketStatusCount()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCD, 0x00, 0x04,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x10, 0x00, 0x00, 0x00,
            0x00, 0x01, 0x02, 0x03
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[1];

        RtcpPacketStatus status = RtcpTwccPacketReader.TryParse(packet, statuses, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParse_RejectsPaddingBit()
    {
        ReadOnlySpan<byte> packet =
        [
            0xAF, 0xCD, 0x00, 0x05,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x10, 0x00, 0x00, 0x01,
            0x00, 0x01, 0x02, 0x03,
            0x20, 0x01,
            0x04, 0x00
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[1];

        RtcpPacketStatus status = RtcpTwccPacketReader.TryParse(packet, statuses, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParse_RejectsOverlongZeroTrailingPadding()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCD, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x10, 0x00, 0x00, 0x01,
            0x00, 0x01, 0x02, 0x03,
            0x20, 0x01,
            0x04,
            0x00, 0x00, 0x00, 0x00, 0x00
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[1];

        RtcpPacketStatus status = RtcpTwccPacketReader.TryParse(packet, statuses, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryParse_RejectsNonZeroUnusedStatusVectorSlots()
    {
        ReadOnlySpan<byte> packet =
        [
            0x8F, 0xCD, 0x00, 0x06,
            0x01, 0x02, 0x03, 0x04,
            0x05, 0x06, 0x07, 0x08,
            0x10, 0x00, 0x00, 0x03,
            0x00, 0x01, 0x02, 0x03,
            0xD8, 0x40,
            0x05, 0xFF, 0xEC,
            0x00, 0x00, 0x00
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[3];

        RtcpPacketStatus status = RtcpTwccPacketReader.TryParse(packet, statuses, out _);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
    }

    [Fact]
    public void TryWrite_ReturnsDestinationTooSmall()
    {
        RtcpTwccPacketStatus[] statuses =
        [
            new() { Symbol = RtcpTwccPacketStatusSymbol.SmallDelta, Delta250Microseconds = 1 }
        ];
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            BaseSequenceNumber = 3,
            ReferenceTime64Milliseconds = 4,
            FeedbackPacketCount = 5,
            PacketStatuses = statuses
        };
        Span<byte> destination = stackalloc byte[8];

        RtcpPacketStatus status = RtcpTwccPacketWriter.TryWrite(feedback, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWrite_RejectsEmptyPacketStatuses()
    {
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            BaseSequenceNumber = 3,
            ReferenceTime64Milliseconds = 4,
            FeedbackPacketCount = 5,
            PacketStatuses = ReadOnlyMemory<RtcpTwccPacketStatus>.Empty
        };
        Span<byte> destination = stackalloc byte[32];

        RtcpPacketStatus status = RtcpTwccPacketWriter.TryWrite(feedback, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWrite_RejectsInvalidSmallDeltaBeforeDestinationPressure()
    {
        RtcpTwccPacketStatus[] statuses =
        [
            new() { Symbol = RtcpTwccPacketStatusSymbol.SmallDelta, Delta250Microseconds = -1 }
        ];
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            BaseSequenceNumber = 3,
            ReferenceTime64Milliseconds = 4,
            FeedbackPacketCount = 5,
            PacketStatuses = statuses
        };
        Span<byte> destination = stackalloc byte[8];

        RtcpPacketStatus status = RtcpTwccPacketWriter.TryWrite(feedback, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWrite_RejectsUnknownStatusSymbolBeforeDestinationPressure()
    {
        RtcpTwccPacketStatus[] statuses =
        [
            new() { Symbol = (RtcpTwccPacketStatusSymbol)3 }
        ];
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            BaseSequenceNumber = 3,
            ReferenceTime64Milliseconds = 4,
            FeedbackPacketCount = 5,
            PacketStatuses = statuses
        };
        Span<byte> destination = stackalloc byte[8];

        RtcpPacketStatus status = RtcpTwccPacketWriter.TryWrite(feedback, destination, out int bytesWritten);

        Assert.Equal(RtcpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Write_BufferWriterWritesPacket()
    {
        RtcpTwccPacketStatus[] statuses =
        [
            new() { Symbol = RtcpTwccPacketStatusSymbol.NotReceived }
        ];
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            BaseSequenceNumber = 3,
            ReferenceTime64Milliseconds = 4,
            FeedbackPacketCount = 5,
            PacketStatuses = statuses
        };
        var writer = new ArrayBufferWriter<byte>();

        RtcpTwccPacketWriter.Write(feedback, writer);

        Assert.Equal(24, writer.WrittenCount);
        Assert.Equal(0x8F, writer.WrittenSpan[0]);
        Assert.Equal(0xCD, writer.WrittenSpan[1]);
    }

    [Fact]
    public void Write_ValidatesPacketBeforeRequestingDestinationStorage()
    {
        RtcpTwccPacketStatus[] statuses =
        [
            new() { Symbol = RtcpTwccPacketStatusSymbol.SmallDelta, Delta250Microseconds = -1 }
        ];
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 1,
            MediaSsrc = 2,
            BaseSequenceNumber = 3,
            ReferenceTime64Milliseconds = 4,
            FeedbackPacketCount = 5,
            PacketStatuses = statuses
        };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtcpTwccPacketWriter.Write(feedback, writer));

        Assert.Contains(nameof(RtcpPacketStatus.InvalidPacket), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void TryParseAndTryWrite_DoNotAllocateInPacketLoop()
    {
        RtcpTwccPacketStatus[] statuses =
        [
            new() { Symbol = RtcpTwccPacketStatusSymbol.SmallDelta, Delta250Microseconds = 4 },
            new() { Symbol = RtcpTwccPacketStatusSymbol.SmallDelta, Delta250Microseconds = 8 },
            new() { Symbol = RtcpTwccPacketStatusSymbol.NotReceived },
            new() { Symbol = RtcpTwccPacketStatusSymbol.LargeOrNegativeDelta, Delta250Microseconds = -12 }
        ];
        var feedback = new RtcpTwccFeedbackPacket
        {
            SenderSsrc = 0x01020304,
            MediaSsrc = 0xAABBCCDD,
            BaseSequenceNumber = 0x1234,
            ReferenceTime64Milliseconds = 0x00A1B2C3,
            FeedbackPacketCount = 9,
            PacketStatuses = statuses
        };
        byte[] packet = new byte[64];
        RtcpTwccPacketStatus[] parsedStatuses = new RtcpTwccPacketStatus[4];
        Assert.Equal(RtcpPacketStatus.Success, RtcpTwccPacketWriter.TryWrite(feedback, packet, out int bytesWritten));
        Assert.Equal(RtcpPacketStatus.Success, RtcpTwccPacketReader.TryParse(packet.AsMemory(0, bytesWritten).Span, parsedStatuses, out _));

        bool loopSucceeded = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpTwccPacketWriter.TryWrite(feedback, packet, out bytesWritten) != RtcpPacketStatus.Success
                || RtcpTwccPacketReader.TryParse(packet.AsMemory(0, bytesWritten).Span, parsedStatuses, out RtcpTwccFeedbackPacket parsed) != RtcpPacketStatus.Success
                || parsed.PacketStatuses.Length != 4)
            {
                loopSucceeded = false;
                break;
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(loopSucceeded);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FeedbackBuilder_DoesNotAllocateInReceptionLoop()
    {
        RtcpTwccReceivedPacket[] received =
        [
            new() { SequenceNumber = 1000, ArrivalTime250Microseconds = 1024 + 4 },
            new() { SequenceNumber = 1002, ArrivalTime250Microseconds = 1024 + 9 },
            new() { SequenceNumber = 1003, ArrivalTime250Microseconds = 1024 - 3 }
        ];
        RtcpTwccPacketStatus[] statuses = new RtcpTwccPacketStatus[4];
        Assert.Equal(
            RtcpPacketStatus.Success,
            RtcpTwccFeedbackBuilder.TryBuild(1, 2, 3, received, statuses, out RtcpTwccFeedbackPacket feedback));
        Assert.Equal(4, feedback.PacketStatuses.Length);

        bool loopSucceeded = true;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtcpTwccFeedbackBuilder.TryBuild(1, 2, 3, received, statuses, out feedback) != RtcpPacketStatus.Success ||
                feedback.PacketStatuses.Length != 4)
            {
                loopSucceeded = false;
                break;
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(loopSucceeded);
        Assert.Equal(0, allocated);
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
