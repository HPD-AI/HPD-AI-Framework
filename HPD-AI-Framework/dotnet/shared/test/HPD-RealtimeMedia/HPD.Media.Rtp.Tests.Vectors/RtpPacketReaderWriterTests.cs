#nullable enable

using System.Buffers;
using HPD.Events.Struct;
using HPD.Media.Diagnostics;
using HPD.Media.Rtp;

namespace HPD.Media.Rtp.Tests.Vectors;

public sealed class RtpPacketReaderWriterTests
{
    [Fact]
    public void TryParse_ReadsFixedHeaderAndPayload()
    {
        ReadOnlySpan<byte> packet =
        [
            0x80, 0xE0, 0x12, 0x34,
            0x01, 0x02, 0x03, 0x04,
            0xA1, 0xA2, 0xA3, 0xA4,
            0xDE, 0xAD, 0xBE, 0xEF
        ];

        RtpPacketStatus status = RtpPacketReader.TryParse(packet, out RtpPacketView view);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.True(view.Header.Marker);
        Assert.Equal(96, view.Header.PayloadType);
        Assert.Equal(0x1234, view.Header.SequenceNumber);
        Assert.Equal(0x01020304u, view.Header.Timestamp);
        Assert.Equal(0xA1A2A3A4u, view.Header.Ssrc);
        Assert.False(view.Header.Padding);
        Assert.Equal([0xDE, 0xAD, 0xBE, 0xEF], view.Payload.ToArray());
    }

    [Fact]
    public void TryParse_SkipsCsrcValues()
    {
        ReadOnlySpan<byte> packet =
        [
            0x82, 0x11, 0x00, 0x09,
            0x00, 0x00, 0x00, 0x10,
            0x00, 0x00, 0x00, 0x20,
            0xCA, 0xFE, 0x00, 0x01,
            0xCA, 0xFE, 0x00, 0x02,
            0xAA, 0xBB
        ];

        RtpPacketStatus status = RtpPacketReader.TryParse(packet, out RtpPacketView view);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.Equal(2, view.Header.CsrcCount);
        RtpCsrcEnumerator csrcs = view.GetCsrcs();
        Assert.True(csrcs.MoveNext());
        Assert.Equal(0xCAFE0001u, csrcs.Current);
        Assert.True(csrcs.MoveNext());
        Assert.Equal(0xCAFE0002u, csrcs.Current);
        Assert.False(csrcs.MoveNext());
        Assert.Equal([0xCA, 0xFE, 0x00, 0x01, 0xCA, 0xFE, 0x00, 0x02], view.CsrcData.ToArray());
        Assert.Equal([0xAA, 0xBB], view.Payload.ToArray());
    }

    [Fact]
    public void TryParse_RemovesPaddingFromPayload()
    {
        ReadOnlySpan<byte> packet =
        [
            0xA0, 0x61, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x10, 0x20, 0x00, 0x02
        ];

        RtpPacketStatus status = RtpPacketReader.TryParse(packet, out RtpPacketView view);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.True(view.Header.Padding);
        Assert.Equal([0x10, 0x20], view.Payload.ToArray());
    }

    [Fact]
    public void TryParse_RejectsInvalidPaddingCount()
    {
        ReadOnlySpan<byte> zeroPadding =
        [
            0xA0, 0x61, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x10, 0x20, 0x00, 0x00
        ];
        ReadOnlySpan<byte> excessivePadding =
        [
            0xA0, 0x61, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x10, 0x20, 0x00, 0x05
        ];

        Assert.Equal(RtpPacketStatus.InvalidPacket, RtpPacketReader.TryParse(zeroPadding, out _));
        Assert.Equal(RtpPacketStatus.InvalidPacket, RtpPacketReader.TryParse(excessivePadding, out _));
    }

    [Fact]
    public void TryParse_ReadsHeaderExtensionBlock()
    {
        ReadOnlySpan<byte> packet =
        [
            0x90, 0x60, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0xBE, 0xDE, 0x00, 0x01,
            0x11, 0xAB, 0xCD, 0x00,
            0x44, 0x55
        ];

        RtpPacketStatus status = RtpPacketReader.TryParse(packet, out RtpPacketView view);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.Equal(0xBEDE, view.Header.ExtensionProfile);
        Assert.Equal([0x11, 0xAB, 0xCD, 0x00], view.ExtensionData.ToArray());
        Assert.Equal([0x44, 0x55], view.Payload.ToArray());

        RtpHeaderExtensionEnumerator extensions = view.GetHeaderExtensions();
        Assert.True(extensions.MoveNext());
        Assert.Equal(1, extensions.Current.Id);
        Assert.Equal([0xAB, 0xCD], extensions.Current.Data.ToArray());
        Assert.False(extensions.MoveNext());
    }

    [Fact]
    public void GetHeaderExtensions_IgnoresNonOneByteExtensionProfile()
    {
        ReadOnlySpan<byte> packet =
        [
            0x90, 0x60, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0x10, 0x00, 0x00, 0x01,
            0x11, 0xAB, 0xCD, 0x00,
            0x44, 0x55
        ];

        RtpPacketStatus status = RtpPacketReader.TryParse(packet, out RtpPacketView view);
        RtpHeaderExtensionEnumerator extensions = view.GetHeaderExtensions();

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.Equal(0x1000, view.Header.ExtensionProfile);
        Assert.Equal([0x11, 0xAB, 0xCD, 0x00], view.ExtensionData.ToArray());
        Assert.False(extensions.MoveNext());
    }

    [Fact]
    public void TryParse_RejectsUnsupportedVersion()
    {
        ReadOnlySpan<byte> packet =
        [
            0x40, 0x60, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03
        ];

        RtpPacketStatus status = RtpPacketReader.TryParse(packet, out _);

        Assert.Equal(RtpPacketStatus.UnsupportedVersion, status);
    }

    [Fact]
    public void TryParse_RejectsMalformedExtension()
    {
        ReadOnlySpan<byte> packet =
        [
            0x90, 0x60, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0xBE, 0xDE, 0x00, 0x02,
            0x01, 0x02, 0x03, 0x04
        ];

        RtpPacketStatus status = RtpPacketReader.TryParse(packet, out _);

        Assert.Equal(RtpPacketStatus.MalformedExtension, status);
    }

    [Fact]
    public void TryParse_RejectsMalformedOneByteHeaderExtensionElements()
    {
        ReadOnlySpan<byte> reservedId =
        [
            0x90, 0x60, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0xBE, 0xDE, 0x00, 0x01,
            0xF0, 0x00, 0x00, 0x00
        ];
        ReadOnlySpan<byte> truncatedElement =
        [
            0x90, 0x60, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0xBE, 0xDE, 0x00, 0x01,
            0x13, 0xAA, 0x00, 0x00
        ];

        Assert.Equal(RtpPacketStatus.MalformedExtension, RtpPacketReader.TryParse(reservedId, out _));
        Assert.Equal(RtpPacketStatus.MalformedExtension, RtpPacketReader.TryParse(truncatedElement, out _));
    }

    [Fact]
    public void TryWrite_WritesPacketThatParsesBack()
    {
        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] extension = [0x11, 0xAB, 0xCD, 0x00];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                Marker = true,
                SequenceNumber = 0x1234,
                Timestamp = 0x01020304,
                Ssrc = 0xA1A2A3A4,
                ExtensionProfile = 0xBEDE
            },
            ExtensionData = extension,
            Payload = payload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        Span<byte> destination = stackalloc byte[32];

        RtpPacketStatus status = RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.Equal(24, bytesWritten);
        Assert.Equal(RtpPacketStatus.Success, RtpPacketReader.TryParse(destination[..bytesWritten], out RtpPacketView view));
        Assert.Equal(packet.Header.PayloadType, view.Header.PayloadType);
        Assert.Equal(packet.Header.SequenceNumber, view.Header.SequenceNumber);
        Assert.Equal(packet.Header.Timestamp, view.Header.Timestamp);
        Assert.Equal(packet.Header.Ssrc, view.Header.Ssrc);
        Assert.Equal(payload, view.Payload.ToArray());
        Assert.Equal(extension, view.ExtensionData.ToArray());
    }

    [Fact]
    public void TryWrite_WithCallerOwnedPayloadSpan_WritesPacketThatParsesBack()
    {
        ReadOnlySpan<byte> payload = [0xDE, 0xAD, 0xBE, 0xEF];
        ReadOnlySpan<byte> extension = [0x11, 0xAB, 0xCD, 0x00];
        Span<byte> destination = stackalloc byte[32];
        var header = new RtpHeader
        {
            PayloadType = 96,
            Marker = true,
            SequenceNumber = 0x1234,
            Timestamp = 0x01020304,
            Ssrc = 0xA1A2A3A4,
            ExtensionProfile = 0xBEDE
        };

        RtpPacketStatus status = RtpPacketWriter.TryWrite(
            header,
            ReadOnlySpan<uint>.Empty,
            payload,
            extension,
            destination,
            out int bytesWritten);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.Equal(24, bytesWritten);
        Assert.Equal(RtpPacketStatus.Success, RtpPacketReader.TryParse(destination[..bytesWritten], out RtpPacketView view));
        Assert.Equal(header.PayloadType, view.Header.PayloadType);
        Assert.Equal(header.SequenceNumber, view.Header.SequenceNumber);
        Assert.Equal(header.Timestamp, view.Header.Timestamp);
        Assert.Equal(header.Ssrc, view.Header.Ssrc);
        Assert.Equal(payload.ToArray(), view.Payload.ToArray());
        Assert.Equal(extension.ToArray(), view.ExtensionData.ToArray());
    }

    [Fact]
    public void TryWrite_WithCallerOwnedCsrcSpan_WritesCsrcValues()
    {
        ReadOnlySpan<uint> csrcs = [0xCAFE0001u, 0xCAFE0002u];
        ReadOnlySpan<byte> payload = [0xAA, 0xBB, 0xCC, 0xDD];
        Span<byte> destination = stackalloc byte[32];
        var header = new RtpHeader
        {
            PayloadType = 17,
            SequenceNumber = 9,
            Timestamp = 16,
            Ssrc = 32,
            CsrcCount = 2
        };

        RtpPacketStatus status = RtpPacketWriter.TryWrite(
            header,
            csrcs,
            payload,
            ReadOnlySpan<byte>.Empty,
            destination,
            out int bytesWritten);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.Equal(24, bytesWritten);
        Assert.Equal(0x82, destination[0]);
        Assert.Equal(RtpPacketStatus.Success, RtpPacketReader.TryParse(destination[..bytesWritten], out RtpPacketView view));
        RtpCsrcEnumerator parsedCsrcs = view.GetCsrcs();
        Assert.True(parsedCsrcs.MoveNext());
        Assert.Equal(csrcs[0], parsedCsrcs.Current);
        Assert.True(parsedCsrcs.MoveNext());
        Assert.Equal(csrcs[1], parsedCsrcs.Current);
        Assert.False(parsedCsrcs.MoveNext());
        Assert.Equal(payload.ToArray(), view.Payload.ToArray());
    }

    [Fact]
    public void TryWrite_WritesCsrcValues()
    {
        uint[] csrcs = [0xCAFE0001u, 0xCAFE0002u];
        byte[] payload = [0xAA, 0xBB, 0xCC, 0xDD];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 17,
                SequenceNumber = 9,
                Timestamp = 16,
                Ssrc = 32,
                CsrcCount = 2
            },
            Csrcs = csrcs,
            Payload = payload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        Span<byte> destination = stackalloc byte[32];

        RtpPacketStatus status = RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten);

        Assert.Equal(RtpPacketStatus.Success, status);
        Assert.Equal(24, bytesWritten);
        Assert.Equal(0x82, destination[0]);
        Assert.Equal(RtpPacketStatus.Success, RtpPacketReader.TryParse(destination[..bytesWritten], out RtpPacketView view));
        Assert.Equal(2, view.Header.CsrcCount);
        RtpCsrcEnumerator parsedCsrcs = view.GetCsrcs();
        Assert.True(parsedCsrcs.MoveNext());
        Assert.Equal(csrcs[0], parsedCsrcs.Current);
        Assert.True(parsedCsrcs.MoveNext());
        Assert.Equal(csrcs[1], parsedCsrcs.Current);
        Assert.False(parsedCsrcs.MoveNext());
        Assert.Equal(payload, view.Payload.ToArray());
    }

    [Fact]
    public void TryWrite_RejectsMismatchedCsrcCount()
    {
        uint[] csrcs = [0xCAFE0001u];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 17,
                SequenceNumber = 9,
                Timestamp = 16,
                Ssrc = 32,
                CsrcCount = 2
            },
            Csrcs = csrcs,
            Payload = ReadOnlyMemory<byte>.Empty,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        Span<byte> destination = stackalloc byte[32];

        RtpPacketStatus status = RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten);

        Assert.Equal(RtpPacketStatus.InvalidPacket, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWrite_RejectsExtensionDataBeyondHeaderLengthLimit()
    {
        byte[] extension = new byte[(ushort.MaxValue + 1) * 4];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                SequenceNumber = 1,
                Timestamp = 2,
                Ssrc = 3,
                ExtensionProfile = 0xBEDE
            },
            ExtensionData = extension,
            Payload = ReadOnlyMemory<byte>.Empty,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        Span<byte> destination = stackalloc byte[16];

        RtpPacketStatus status = RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten);

        Assert.Equal(RtpPacketStatus.MalformedExtension, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWrite_RejectsMalformedOneByteHeaderExtensionElements()
    {
        byte[] extension = [0xF0, 0x00, 0x00, 0x00];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                SequenceNumber = 1,
                Timestamp = 2,
                Ssrc = 3,
                ExtensionProfile = 0xBEDE
            },
            ExtensionData = extension,
            Payload = ReadOnlyMemory<byte>.Empty,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        Span<byte> destination = stackalloc byte[32];

        RtpPacketStatus status = RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten);

        Assert.Equal(RtpPacketStatus.MalformedExtension, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryWrite_ReturnsDestinationTooSmall()
    {
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                SequenceNumber = 1,
                Timestamp = 2,
                Ssrc = 3
            },
            Payload = new byte[] { 1, 2, 3, 4 },
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        Span<byte> destination = stackalloc byte[8];

        RtpPacketStatus status = RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten);

        Assert.Equal(RtpPacketStatus.DestinationTooSmall, status);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void Write_BufferWriterWritesPacket()
    {
        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                Marker = true,
                SequenceNumber = 0x1234,
                Timestamp = 0x01020304,
                Ssrc = 0xA1A2A3A4
            },
            Payload = payload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var writer = new ArrayBufferWriter<byte>();

        RtpPacketWriter.Write(packet, writer);

        Assert.Equal(16, writer.WrittenCount);
        Assert.Equal(0x80, writer.WrittenSpan[0]);
        Assert.Equal(0xE0, writer.WrittenSpan[1]);
    }

    [Fact]
    public void Write_ValidatesPacketBeforeRequestingDestinationStorage()
    {
        byte[] extension = [0xF0, 0x00, 0x00, 0x00];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                SequenceNumber = 1,
                Timestamp = 2,
                Ssrc = 3,
                ExtensionProfile = 0xBEDE
            },
            ExtensionData = extension,
            Payload = ReadOnlyMemory<byte>.Empty,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        var writer = new ThrowingBufferWriter();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => RtpPacketWriter.Write(packet, writer));

        Assert.Contains(nameof(RtpPacketStatus.MalformedExtension), exception.Message);
        Assert.Equal(0, writer.GetSpanCallCount);
        Assert.Equal(0, writer.GetMemoryCallCount);
        Assert.Equal(0, writer.AdvanceCallCount);
    }

    [Fact]
    public void TryParse_DoesNotAllocateForFixedPacket()
    {
        byte[] packet =
        [
            0x80, 0xE0, 0x12, 0x34,
            0x01, 0x02, 0x03, 0x04,
            0xA1, 0xA2, 0xA3, 0xA4,
            0xDE, 0xAD, 0xBE, 0xEF
        ];

        for (int i = 0; i < 1_000; i++)
        {
            Assert.Equal(RtpPacketStatus.Success, RtpPacketReader.TryParse(packet, out _));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtpPacketReader.TryParse(packet, out _) != RtpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTP packet parse failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void TryWrite_DoesNotAllocateForFixedPacket()
    {
        byte[] payload = [0xDE, 0xAD, 0xBE, 0xEF];
        byte[] extension = [0x11, 0xAB, 0xCD, 0x00];
        var packet = new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                Marker = true,
                SequenceNumber = 0x1234,
                Timestamp = 0x01020304,
                Ssrc = 0xA1A2A3A4,
                ExtensionProfile = 0xBEDE
            },
            ExtensionData = extension,
            Payload = payload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
        Span<byte> destination = stackalloc byte[32];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtpPacketWriter.TryWrite(packet, destination, out _) != RtpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTP packet write failed during warmup.");
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtpPacketWriter.TryWrite(packet, destination, out int bytesWritten) != RtpPacketStatus.Success ||
                bytesWritten != 24)
            {
                throw new InvalidOperationException("RTP packet write failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void HeaderExtensionTraversal_DoesNotAllocate()
    {
        byte[] packet =
        [
            0x90, 0x60, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x02,
            0x00, 0x00, 0x00, 0x03,
            0xBE, 0xDE, 0x00, 0x01,
            0x11, 0xAB, 0xCD, 0x00,
            0x44, 0x55
        ];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtpPacketReader.TryParse(packet, out RtpPacketView view) != RtpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTP extension packet parse failed during warmup.");
            }

            RtpHeaderExtensionEnumerator extensions = view.GetHeaderExtensions();
            while (extensions.MoveNext())
            {
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (RtpPacketReader.TryParse(packet, out RtpPacketView view) != RtpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTP extension packet parse failed during allocation measurement.");
            }

            RtpHeaderExtensionEnumerator extensions = view.GetHeaderExtensions();
            while (extensions.MoveNext())
            {
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void CsrcTraversal_DoesNotAllocate()
    {
        byte[] packet =
        [
            0x82, 0x11, 0x00, 0x09,
            0x00, 0x00, 0x00, 0x10,
            0x00, 0x00, 0x00, 0x20,
            0xCA, 0xFE, 0x00, 0x01,
            0xCA, 0xFE, 0x00, 0x02,
            0xAA, 0xBB
        ];

        for (int i = 0; i < 1_000; i++)
        {
            if (RtpPacketReader.TryParse(packet, out RtpPacketView view) != RtpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTP CSRC packet parse failed during warmup.");
            }

            RtpCsrcEnumerator csrcs = view.GetCsrcs();
            while (csrcs.MoveNext())
            {
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        uint observed = 0;
        for (int i = 0; i < 1_000; i++)
        {
            if (RtpPacketReader.TryParse(packet, out RtpPacketView view) != RtpPacketStatus.Success)
            {
                throw new InvalidOperationException("RTP CSRC packet parse failed during allocation measurement.");
            }

            RtpCsrcEnumerator csrcs = view.GetCsrcs();
            while (csrcs.MoveNext())
            {
                observed ^= csrcs.Current;
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0u, observed);
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Reorderer_DrainsPacketsInSequenceOrder()
    {
        var reorderer = new RtpPacketReorderer(capacity: 8, maxReorderDistance: 2);
        var sink = new CollectingPacketEventSink();

        Assert.True(reorderer.TryPush(CreatePacket(10)));
        Assert.True(reorderer.TryPush(CreatePacket(12)));
        Assert.True(reorderer.TryPush(CreatePacket(11)));

        Assert.True(reorderer.TryReadAvailable(sink));

        Assert.Equal(
            [10, 11, 12],
            sink.Events.Select(static packetEvent => packetEvent.SequenceNumber).ToArray());
        Assert.All(sink.Events, static packetEvent => Assert.Equal(RtpPacketEventKind.Packet, packetEvent.Kind));
    }

    [Fact]
    public void Reorderer_EmitsLossWhenGapExceedsReorderDistance()
    {
        var reorderer = new RtpPacketReorderer(capacity: 8, maxReorderDistance: 1);
        var sink = new CollectingPacketEventSink();

        Assert.True(reorderer.TryPush(CreatePacket(20)));
        Assert.True(reorderer.TryPush(CreatePacket(22)));
        Assert.True(reorderer.TryPush(CreatePacket(23)));

        Assert.True(reorderer.TryReadAvailable(sink));

        Assert.Equal(
            [
                RtpPacketEventKind.Packet,
                RtpPacketEventKind.Loss,
                RtpPacketEventKind.Packet,
                RtpPacketEventKind.Packet
            ],
            sink.Events.Select(static packetEvent => packetEvent.Kind).ToArray());
        Assert.Equal([20, 21, 22, 23], sink.Events.Select(static packetEvent => packetEvent.SequenceNumber).ToArray());
        Assert.Equal(1, sink.Events[1].LostPacketCount);
    }

    [Fact]
    public void Reorderer_EmitsStructTelemetryForLossAndDepth()
    {
        using var hub = new StructEventHub();
        using StructEventInbox<RtpLossSample> lossInbox = hub
            .Route<RtpLossSample>(RealtimeMediaTelemetry.RouteOptions)
            .CreateInbox(new StructEventInboxOptions { Capacity = 4 });
        using StructEventInbox<RtpReorderDepthSample> depthInbox = hub
            .Route<RtpReorderDepthSample>(RealtimeMediaTelemetry.RouteOptions)
            .CreateInbox(new StructEventInboxOptions { Capacity = 8 });
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var reorderer = new RtpPacketReorderer(emitters, capacity: 8, maxReorderDistance: 1);
        var sink = new CollectingPacketEventSink();

        Assert.True(reorderer.TryPush(CreatePacket(20, ssrc: 4321)));
        Assert.True(reorderer.TryPush(CreatePacket(22, ssrc: 4321)));
        Assert.True(reorderer.TryPush(CreatePacket(23, ssrc: 4321)));
        Assert.True(reorderer.TryReadAvailable(sink));

        Assert.True(lossInbox.TryRead(out RtpLossSample loss));
        Assert.Equal(4321u, loss.Ssrc);
        Assert.Equal(21, loss.SequenceStart);
        Assert.Equal(1, loss.LostPacketCount);
        Assert.True(depthInbox.TryRead(out RtpReorderDepthSample firstDepth));
        Assert.Equal(1, firstDepth.Depth);
        Assert.Equal(8, firstDepth.Capacity);
    }

    [Fact]
    public void Reorderer_HandlesSequenceWrap()
    {
        var reorderer = new RtpPacketReorderer(capacity: 8, maxReorderDistance: 2);
        var sink = new CollectingPacketEventSink();

        Assert.True(reorderer.TryPush(CreatePacket(65534)));
        Assert.True(reorderer.TryPush(CreatePacket(0)));
        Assert.True(reorderer.TryPush(CreatePacket(65535)));

        Assert.True(reorderer.TryReadAvailable(sink));

        Assert.Equal([65534, 65535, 0], sink.Events.Select(static packetEvent => (int)packetEvent.SequenceNumber).ToArray());
    }

    [Fact]
    public async Task Reorderer_ReadAsyncReturnsOneEventAtATime()
    {
        var reorderer = new RtpPacketReorderer(capacity: 8, maxReorderDistance: 0);

        Assert.True(reorderer.TryPush(CreatePacket(100)));
        Assert.True(reorderer.TryPush(CreatePacket(102)));

        RtpPacketEvent? first = await reorderer.ReadAsync();
        RtpPacketEvent? second = await reorderer.ReadAsync();
        RtpPacketEvent? third = await reorderer.ReadAsync();
        reorderer.Complete();
        RtpPacketEvent? fourth = await reorderer.ReadAsync();

        Assert.Equal(RtpPacketEventKind.Packet, first?.Kind);
        Assert.Equal((ushort?)100, first?.SequenceNumber);
        Assert.Equal(RtpPacketEventKind.Loss, second?.Kind);
        Assert.Equal((ushort?)101, second?.SequenceNumber);
        Assert.Equal(RtpPacketEventKind.Packet, third?.Kind);
        Assert.Equal((ushort?)102, third?.SequenceNumber);
        Assert.Null(fourth);
    }

    [Fact]
    public async Task Reorderer_ReadAsyncWaitsForFuturePacket()
    {
        var reorderer = new RtpPacketReorderer(capacity: 8, maxReorderDistance: 0);
        ValueTask<RtpPacketEvent?> pending = reorderer.ReadAsync();

        Assert.False(pending.IsCompleted);
        Assert.True(reorderer.TryPush(CreatePacket(200)));

        RtpPacketEvent? packetEvent = await pending;
        Assert.Equal(RtpPacketEventKind.Packet, packetEvent?.Kind);
        Assert.Equal((ushort?)200, packetEvent?.SequenceNumber);
    }

    [Fact]
    public async Task Reorderer_CanceledReadAsyncDoesNotConsumeFuturePacket()
    {
        var reorderer = new RtpPacketReorderer(capacity: 8, maxReorderDistance: 0);
        using var cancellation = new CancellationTokenSource();
        ValueTask<RtpPacketEvent?> canceled = reorderer.ReadAsync(cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled);

        ValueTask<RtpPacketEvent?> pending = reorderer.ReadAsync();
        Assert.True(reorderer.TryPush(CreatePacket(210)));

        RtpPacketEvent? packetEvent = await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(RtpPacketEventKind.Packet, packetEvent?.Kind);
        Assert.Equal((ushort?)210, packetEvent?.SequenceNumber);
    }

    [Fact]
    public void Reorderer_RejectsMalformedRetainedPacketsWithoutInitializing()
    {
        var reorderer = new RtpPacketReorderer(capacity: 8, maxReorderDistance: 1);
        var sink = new CollectingPacketEventSink();
        var invalidPayloadType = CreatePacket(10) with
        {
            Header = CreatePacket(10).Header with { PayloadType = 128 }
        };
        var mismatchedCsrcCount = CreatePacket(11) with
        {
            Header = CreatePacket(11).Header with { CsrcCount = 2 },
            Csrcs = new uint[] { 0xCAFE0001 }
        };
        var malformedExtension = CreatePacket(12) with
        {
            Header = CreatePacket(12).Header with { ExtensionProfile = RtpHeaderExtensionEnumerator.OneByteHeaderProfile },
            ExtensionData = new byte[] { 0xF0, 0x00, 0x00, 0x00 }
        };

        Assert.False(reorderer.TryPush(invalidPayloadType));
        Assert.False(reorderer.TryPush(mismatchedCsrcCount));
        Assert.False(reorderer.TryPush(malformedExtension));
        Assert.True(reorderer.TryPush(CreatePacket(20)));
        Assert.True(reorderer.TryReadAvailable(sink));

        RtpPacketEvent packetEvent = Assert.Single(sink.Events);
        Assert.Equal(20, packetEvent.SequenceNumber);
    }

    [Fact]
    public void Reorderer_DrainDoesNotAllocateAfterConstruction()
    {
        var reorderer = new RtpPacketReorderer(capacity: 16, maxReorderDistance: 1);
        var sink = new CountingPacketEventSink();

        for (int i = 0; i < 8; i++)
        {
            Assert.True(reorderer.TryPush(CreatePacket((ushort)i)));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.True(reorderer.TryReadAvailable(sink));
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(8, sink.Count);
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Reorderer_TelemetryWithNoSubscribersDoesNotAllocateAfterWarmup()
    {
        using var hub = new StructEventHub();
        RealtimeMediaTelemetryEmitters emitters = RealtimeMediaTelemetry.CreateEmitters(hub);
        var reorderer = new RtpPacketReorderer(emitters, capacity: 128, maxReorderDistance: 64);

        for (ushort sequenceNumber = 0; sequenceNumber < 32; sequenceNumber++)
        {
            Assert.True(reorderer.TryPush(CreatePacket(sequenceNumber)));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (ushort sequenceNumber = 32; sequenceNumber < 64; sequenceNumber++)
        {
            Assert.True(reorderer.TryPush(CreatePacket(sequenceNumber)));
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    private static RtpPacket CreatePacket(ushort sequenceNumber, uint ssrc = 1234)
    {
        return new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = 96,
                SequenceNumber = sequenceNumber,
                Timestamp = sequenceNumber,
                Ssrc = ssrc
            },
            Payload = ReadOnlyMemory<byte>.Empty,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class CollectingPacketEventSink : IRtpPacketEventSink
    {
        public List<RtpPacketEvent> Events { get; } = [];

        public bool TryWrite(in RtpPacketEvent packetEvent)
        {
            Events.Add(packetEvent);
            return true;
        }
    }

    private sealed class CountingPacketEventSink : IRtpPacketEventSink
    {
        public int Count { get; private set; }

        public bool TryWrite(in RtpPacketEvent packetEvent)
        {
            Count++;
            return true;
        }
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
