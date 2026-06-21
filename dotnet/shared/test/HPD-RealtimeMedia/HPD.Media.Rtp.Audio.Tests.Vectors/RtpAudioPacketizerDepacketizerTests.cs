#nullable enable

using HPD.Audio.Codecs;
using HPD.Media.Rtp;
using HPD.Media.Rtp.Audio;

namespace HPD.Media.Rtp.Audio.Tests.Vectors;

public sealed class RtpAudioPacketizerDepacketizerTests
{
    [Fact]
    public void FormatMap_ResolvesPayloadType()
    {
        var map = CreateMap();

        bool found = map.TryGetFormat(0, out RtpAudioFormatBinding binding);

        Assert.True(found);
        Assert.Equal(AudioEncoding.Pcmu, binding.EncodedFormat.Encoding);
        Assert.Equal(TimeSpan.FromMilliseconds(20), binding.DefaultPacketTime);
    }

    [Fact]
    public void FormatMap_RejectsPayloadTypeOutsideRtpRange()
    {
        RtpAudioFormatBinding binding = CreateBinding() with { PayloadType = 128 };

        Assert.Throws<ArgumentOutOfRangeException>(() => new RtpAudioFormatMap(version: 1, [binding]));
    }

    [Fact]
    public void FormatMap_RejectsDuplicatePayloadType()
    {
        RtpAudioFormatBinding first = CreateBinding();
        RtpAudioFormatBinding second = CreateBinding() with
        {
            EncodedFormat = PcmuFormat with { ChannelCount = 2 }
        };

        Assert.Throws<ArgumentException>(() => new RtpAudioFormatMap(version: 1, [first, second]));
    }

    [Fact]
    public void FormatMap_RejectsUnusableFormat()
    {
        RtpAudioFormatBinding binding = CreateBinding() with
        {
            EncodedFormat = PcmuFormat with { RtpClockRate = 0 }
        };

        Assert.Throws<ArgumentException>(() => new RtpAudioFormatMap(version: 1, [binding]));
    }

    [Fact]
    public void FormatMap_RejectsNonPositiveDefaultPacketTime()
    {
        RtpAudioFormatBinding binding = CreateBinding() with { DefaultPacketTime = TimeSpan.Zero };

        Assert.Throws<ArgumentException>(() => new RtpAudioFormatMap(version: 1, [binding]));
    }

    [Fact]
    public void Packetizer_WritesOnePacketPerAccessUnit()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap(), initialSequenceNumber: 10, initialTimestamp: 8000);
        var sink = new CollectingPacketSink();
        byte[] payload = [0x11, 0x22, 0x33];
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = payload,
            Duration = TimeSpan.FromMilliseconds(20)
        };

        RtpAudioStatus status = packetizer.Packetize(frame, ssrc: 0xAABBCCDD, payloadType: 0, sink);

        Assert.Equal(RtpAudioStatus.Success, status);
        RtpPacket packet = Assert.Single(sink.Packets);
        Assert.Equal(0, packet.Header.PayloadType);
        Assert.Equal(10, packet.Header.SequenceNumber);
        Assert.Equal(8000u, packet.Header.Timestamp);
        Assert.Equal(0xAABBCCDDu, packet.Header.Ssrc);
        Assert.Equal(payload, packet.Payload.ToArray());
    }

    [Fact]
    public void Packetizer_IncrementsSequenceAndTimestamp()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap(), initialSequenceNumber: 10, initialTimestamp: 8000);
        var sink = new CollectingPacketSink();
        byte[] payload = [0x11];
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = payload,
            Duration = TimeSpan.FromMilliseconds(20)
        };

        Assert.Equal(RtpAudioStatus.Success, packetizer.Packetize(frame, 1, 0, sink));
        Assert.Equal(RtpAudioStatus.Success, packetizer.Packetize(frame, 1, 0, sink));

        Assert.Equal(10, sink.Packets[0].Header.SequenceNumber);
        Assert.Equal(8000u, sink.Packets[0].Header.Timestamp);
        Assert.Equal(11, sink.Packets[1].Header.SequenceNumber);
        Assert.Equal(8160u, sink.Packets[1].Header.Timestamp);
    }

    [Fact]
    public void Depacketizer_WritesEncodedAccessUnit()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CollectingAccessUnitSink();
        RtpPacket packet = CreatePacket(sequenceNumber: 20, timestamp: 12_000, payloadType: 0, payload: [0x7F, 0x80]);
        var packetEvent = new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Packet,
            Packet = packet,
            Ssrc = packet.Header.Ssrc,
            SequenceNumber = packet.Header.SequenceNumber
        };

        RtpAudioStatus status = depacketizer.Push(packetEvent, sink);

        Assert.Equal(RtpAudioStatus.Success, status);
        RtpAudioAccessUnitEvent accessUnit = Assert.Single(sink.Events);
        Assert.False(accessUnit.IsLoss);
        Assert.Equal(AudioEncoding.Pcmu, accessUnit.Frame.Format.Encoding);
        Assert.Equal(TimeSpan.FromMilliseconds(20), accessUnit.Duration);
        Assert.Equal(12_000u, accessUnit.RtpTimestamp);
        Assert.Equal((ushort?)20, accessUnit.RtpSequenceNumber);
        Assert.Equal([0x7F, 0x80], accessUnit.Frame.Data.ToArray());
    }

    [Fact]
    public void Depacketizer_UsesTimestampDeltaForSubsequentDuration()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CollectingAccessUnitSink();

        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(CreatePacketEvent(1, 1_000), sink));
        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(CreatePacketEvent(2, 1_240), sink));

        Assert.Equal(TimeSpan.FromMilliseconds(20), sink.Events[0].Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(30), sink.Events[1].Duration);
    }

    [Fact]
    public void Depacketizer_DoesNotUseTimestampDeltaAcrossSsrcs()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CollectingAccessUnitSink();
        RtpPacket first = CreatePacket(sequenceNumber: 1, timestamp: 1_000, payloadType: 0, payload: [0x01], ssrc: 1);
        RtpPacket second = CreatePacket(sequenceNumber: 2, timestamp: 10_000, payloadType: 0, payload: [0x02], ssrc: 2);

        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(ToPacketEvent(first), sink));
        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(ToPacketEvent(second), sink));

        Assert.Equal(TimeSpan.FromMilliseconds(20), sink.Events[0].Duration);
        Assert.Equal(TimeSpan.FromMilliseconds(20), sink.Events[1].Duration);
    }

    [Fact]
    public void Depacketizer_EmitsLossEventAfterPacket()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CollectingAccessUnitSink();

        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(CreatePacketEvent(1, 1_000), sink));
        var loss = new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Loss,
            Ssrc = 1,
            SequenceNumber = 2,
            ExpectedTimestamp = 1_160,
            LostPacketCount = 2
        };

        RtpAudioStatus status = depacketizer.Push(loss, sink);

        Assert.Equal(RtpAudioStatus.Success, status);
        Assert.True(sink.Events[1].IsLoss);
        Assert.Equal(TimeSpan.FromMilliseconds(40), sink.Events[1].Duration);
        Assert.Equal((ushort?)2, sink.Events[1].RtpSequenceNumber);
    }

    [Fact]
    public void Depacketizer_ReturnsInvalidPacketForZeroLostPacketCount()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CollectingAccessUnitSink();
        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(CreatePacketEvent(1, 1_000), sink));
        var loss = new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Loss,
            Ssrc = 1,
            SequenceNumber = 2,
            ExpectedTimestamp = 1_160,
            LostPacketCount = 0
        };

        RtpAudioStatus status = depacketizer.Push(loss, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void Depacketizer_ReturnsInvalidPacketForLossDurationOverflow()
    {
        var binding = CreateBinding() with { DefaultPacketTime = TimeSpan.FromTicks(TimeSpan.MaxValue.Ticks / 2 + 1) };
        var depacketizer = new RtpAudioDepacketizer(new RtpAudioFormatMap(version: 1, [binding]));
        var sink = new CollectingAccessUnitSink();
        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(CreatePacketEvent(1, 1_000), sink));
        var loss = new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Loss,
            Ssrc = 1,
            SequenceNumber = 2,
            ExpectedTimestamp = 1_160,
            LostPacketCount = 2
        };

        RtpAudioStatus status = depacketizer.Push(loss, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void Depacketizer_ReturnsInvalidPacketForLossBeforePacketContext()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CountingAccessUnitSink();
        var loss = new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Loss,
            Ssrc = 1,
            SequenceNumber = 1,
            ExpectedTimestamp = 1_000,
            LostPacketCount = 1
        };

        RtpAudioStatus status = depacketizer.Push(loss, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void Depacketizer_ReturnsInvalidPacketForLossFromDifferentSsrc()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CollectingAccessUnitSink();
        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(CreatePacketEvent(1, 1_000), sink));
        var loss = new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Loss,
            Ssrc = 2,
            SequenceNumber = 2,
            ExpectedTimestamp = 1_160,
            LostPacketCount = 1
        };

        RtpAudioStatus status = depacketizer.Push(loss, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
        Assert.Single(sink.Events);
    }

    [Fact]
    public void Depacketizer_ReturnsInvalidPacketForUnknownPacketEventKind()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CountingAccessUnitSink();
        var packetEvent = new RtpPacketEvent
        {
            Kind = (RtpPacketEventKind)99,
            Ssrc = 1,
            SequenceNumber = 1
        };

        RtpAudioStatus status = depacketizer.Push(packetEvent, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void Depacketizer_ReturnsInvalidPacketForMismatchedPacketEventMetadata()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CountingAccessUnitSink();
        RtpPacket packet = CreatePacket(sequenceNumber: 10, timestamp: 1_000, payloadType: 0, payload: [0x01]);
        var packetEvent = new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Packet,
            Packet = packet,
            Ssrc = 2,
            SequenceNumber = 11
        };

        RtpAudioStatus status = depacketizer.Push(packetEvent, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void Packetizer_ReturnsUnknownPayloadType()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap());
        var sink = new SinglePacketSink();
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.FromMilliseconds(20)
        };

        RtpAudioStatus status = packetizer.Packetize(frame, ssrc: 1, payloadType: 111, sink);

        Assert.Equal(RtpAudioStatus.UnknownPayloadType, status);
    }

    [Fact]
    public void Packetizer_ReturnsInvalidPacketForPayloadTypeOutsideRtpRange()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap());
        var sink = new SinglePacketSink();
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.FromMilliseconds(20)
        };

        RtpAudioStatus status = packetizer.Packetize(frame, ssrc: 1, payloadType: 128, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
    }

    [Fact]
    public void Packetizer_ReturnsUnsupportedFormat()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap());
        var sink = new SinglePacketSink();
        var frame = new EncodedAudioFrame
        {
            Format = new EncodedAudioFormat
            {
                Encoding = AudioEncoding.Pcma,
                SampleRate = 8000,
                ChannelCount = 1,
                RtpClockRate = 8000
            },
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.FromMilliseconds(20)
        };

        RtpAudioStatus status = packetizer.Packetize(frame, ssrc: 1, payloadType: 0, sink);

        Assert.Equal(RtpAudioStatus.UnsupportedFormat, status);
    }

    [Fact]
    public void Packetizer_ReturnsInvalidPacketForNonPositiveDuration()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap());
        var sink = new SinglePacketSink();
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.Zero
        };

        RtpAudioStatus status = packetizer.Packetize(frame, ssrc: 1, payloadType: 0, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
    }

    [Fact]
    public void Packetizer_ReturnsInvalidPacketForUnrepresentableTimestampDelta()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap());
        var sink = new SinglePacketSink();
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.MaxValue
        };

        RtpAudioStatus status = packetizer.Packetize(frame, ssrc: 1, payloadType: 0, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
    }

    [Fact]
    public void Packetizer_ReturnsInvalidPacketForSubTimestampUnitDuration()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap());
        var sink = new SinglePacketSink();
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.FromTicks(1)
        };

        RtpAudioStatus status = packetizer.Packetize(frame, ssrc: 1, payloadType: 0, sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
    }

    [Fact]
    public void PacketizerAndDepacketizer_ReturnUnsupportedFormatForUnusableClockRate()
    {
        RtpAudioFormatBinding binding = CreateBinding() with
        {
            EncodedFormat = PcmuFormat with { RtpClockRate = 0 }
        };
        var map = new MutableFormatMap(version: 1, binding);
        var packetizer = new RtpAudioPacketizer(map);
        var depacketizer = new RtpAudioDepacketizer(map);
        var packetSink = new SinglePacketSink();
        var accessUnitSink = new CountingAccessUnitSink();
        var frame = new EncodedAudioFrame
        {
            Format = binding.EncodedFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.FromMilliseconds(20)
        };

        RtpAudioStatus packetizeStatus = packetizer.Packetize(frame, ssrc: 1, payloadType: 0, packetSink);
        RtpAudioStatus depacketizeStatus = depacketizer.Push(CreatePacketEvent(1, 1_000), accessUnitSink);

        Assert.Equal(RtpAudioStatus.UnsupportedFormat, packetizeStatus);
        Assert.Equal(RtpAudioStatus.UnsupportedFormat, depacketizeStatus);
    }

    [Fact]
    public void Packetizer_ReturnsSinkBackpressureWithoutAdvancingSequence()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap(), initialSequenceNumber: 10, initialTimestamp: 8000);
        var rejectingSink = new RejectingPacketSink();
        var acceptingSink = new SinglePacketSink();
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.FromMilliseconds(20)
        };

        RtpAudioStatus rejected = packetizer.Packetize(frame, ssrc: 1, payloadType: 0, rejectingSink);
        RtpAudioStatus accepted = packetizer.Packetize(frame, ssrc: 1, payloadType: 0, acceptingSink);

        Assert.Equal(RtpAudioStatus.SinkBackpressure, rejected);
        Assert.Equal(RtpAudioStatus.Success, accepted);
        Assert.Equal(10, acceptingSink.Packet.Header.SequenceNumber);
        Assert.Equal(8000u, acceptingSink.Packet.Header.Timestamp);
    }

    [Fact]
    public void Depacketizer_ReturnsUnknownPayloadType()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CountingAccessUnitSink();

        RtpAudioStatus status = depacketizer.Push(CreatePacketEvent(1, 1_000, payloadType: 111), sink);

        Assert.Equal(RtpAudioStatus.UnknownPayloadType, status);
    }

    [Fact]
    public void Depacketizer_ReturnsInvalidPacketForPayloadTypeOutsideRtpRange()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var sink = new CountingAccessUnitSink();

        RtpAudioStatus status = depacketizer.Push(CreatePacketEvent(1, 1_000, payloadType: 128), sink);

        Assert.Equal(RtpAudioStatus.InvalidPacket, status);
        Assert.Equal(0, sink.Count);
    }

    [Fact]
    public void Depacketizer_ReturnsSinkBackpressureWithoutAdvancingState()
    {
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var rejectingSink = new RejectingAccessUnitSink();
        var acceptingSink = new CollectingAccessUnitSink();

        RtpAudioStatus rejected = depacketizer.Push(CreatePacketEvent(1, 1_000), rejectingSink);
        RtpAudioStatus accepted = depacketizer.Push(CreatePacketEvent(2, 1_160), acceptingSink);

        Assert.Equal(RtpAudioStatus.SinkBackpressure, rejected);
        Assert.Equal(RtpAudioStatus.Success, accepted);
        Assert.Equal(TimeSpan.FromMilliseconds(20), acceptingSink.Events[0].Duration);
    }

    [Fact]
    public void PacketizerAndDepacketizer_UpdateFormatMapAtPacketBoundary()
    {
        var packetizer = new RtpAudioPacketizer(CreateMap(), initialSequenceNumber: 10, initialTimestamp: 8_000);
        var depacketizer = new RtpAudioDepacketizer(CreateMap());
        var packetSink = new SinglePacketSink();
        var accessUnitSink = new CollectingAccessUnitSink();
        var pcmuFrame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = new byte[] { 0x11 },
            Duration = TimeSpan.FromMilliseconds(20)
        };
        var opusFrame = new EncodedAudioFrame
        {
            Format = OpusFormat,
            Data = new byte[] { 0x22, 0x33 },
            Duration = TimeSpan.FromMilliseconds(20)
        };

        Assert.Equal(RtpAudioStatus.Success, packetizer.Packetize(pcmuFrame, ssrc: 1, payloadType: 0, packetSink));
        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(ToPacketEvent(packetSink.Packet), accessUnitSink));
        Assert.Equal(RtpAudioStatus.UnknownPayloadType, packetizer.Packetize(opusFrame, ssrc: 1, payloadType: 111, packetSink));
        Assert.Equal(RtpAudioStatus.UnknownPayloadType, depacketizer.Push(CreatePacketEvent(11, 8_960, payloadType: 111), accessUnitSink));

        packetizer.UpdateFormatMap(CreateRenegotiatedMap());
        depacketizer.UpdateFormatMap(CreateRenegotiatedMap());

        Assert.Equal(RtpAudioStatus.Success, packetizer.Packetize(opusFrame, ssrc: 1, payloadType: 111, packetSink));
        Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(ToPacketEvent(packetSink.Packet), accessUnitSink));

        Assert.Equal(111, packetSink.Packet.Header.PayloadType);
        Assert.Equal(11, packetSink.Packet.Header.SequenceNumber);
        Assert.Equal(8_160u, packetSink.Packet.Header.Timestamp);
        Assert.Equal(2, accessUnitSink.Events.Count);
        Assert.Equal(AudioEncoding.Opus, accessUnitSink.Events[1].Frame.Format.Encoding);
        Assert.Equal(TimeSpan.FromMilliseconds(20), accessUnitSink.Events[1].Duration);
    }

    [Fact]
    public void PacketizerAndDepacketizer_DoNotAllocateInSteadyLoop()
    {
        var map = CreateMap();
        var packetizer = new RtpAudioPacketizer(map, initialSequenceNumber: 1, initialTimestamp: 1);
        var packetSink = new SinglePacketSink();
        var depacketizer = new RtpAudioDepacketizer(map);
        var accessUnitSink = new CountingAccessUnitSink();
        byte[] payload = [0x11];
        var frame = new EncodedAudioFrame
        {
            Format = PcmuFormat,
            Data = payload,
            Duration = TimeSpan.FromMilliseconds(20)
        };

        for (int i = 0; i < 1_000; i++)
        {
            Assert.Equal(RtpAudioStatus.Success, packetizer.Packetize(frame, 1, 0, packetSink));
            Assert.Equal(RtpAudioStatus.Success, depacketizer.Push(ToPacketEvent(packetSink.Packet), accessUnitSink));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_000; i++)
        {
            if (packetizer.Packetize(frame, 1, 0, packetSink) != RtpAudioStatus.Success ||
                depacketizer.Push(ToPacketEvent(packetSink.Packet), accessUnitSink) != RtpAudioStatus.Success)
            {
                throw new InvalidOperationException("RTP audio bridge failed during allocation measurement.");
            }
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    private static EncodedAudioFormat PcmuFormat => new()
    {
        Encoding = AudioEncoding.Pcmu,
        SampleRate = 8000,
        ChannelCount = 1,
        RtpClockRate = 8000
    };

    private static EncodedAudioFormat OpusFormat => new()
    {
        Encoding = AudioEncoding.Opus,
        SampleRate = 48000,
        ChannelCount = 2,
        RtpClockRate = 48000
    };

    private static RtpAudioFormatMap CreateMap()
    {
        RtpAudioFormatBinding[] bindings = [CreateBinding()];
        return new RtpAudioFormatMap(version: 1, bindings);
    }

    private static RtpAudioFormatMap CreateRenegotiatedMap()
    {
        RtpAudioFormatBinding[] bindings =
        [
            new()
            {
                PayloadType = 111,
                EncodedFormat = OpusFormat,
                DefaultPacketTime = TimeSpan.FromMilliseconds(20)
            }
        ];

        return new RtpAudioFormatMap(version: 2, bindings);
    }

    private static RtpAudioFormatBinding CreateBinding()
    {
        return new RtpAudioFormatBinding
        {
            PayloadType = 0,
            EncodedFormat = PcmuFormat,
            DefaultPacketTime = TimeSpan.FromMilliseconds(20)
        };
    }

    private static RtpPacketEvent CreatePacketEvent(ushort sequenceNumber, uint timestamp)
    {
        return CreatePacketEvent(sequenceNumber, timestamp, payloadType: 0);
    }

    private static RtpPacketEvent CreatePacketEvent(ushort sequenceNumber, uint timestamp, byte payloadType)
    {
        RtpPacket packet = CreatePacket(sequenceNumber, timestamp, payloadType, payload: [0x01]);
        return ToPacketEvent(packet);
    }

    private static RtpPacketEvent ToPacketEvent(in RtpPacket packet)
    {
        return new RtpPacketEvent
        {
            Kind = RtpPacketEventKind.Packet,
            Packet = packet,
            Ssrc = packet.Header.Ssrc,
            SequenceNumber = packet.Header.SequenceNumber,
            ExpectedTimestamp = packet.Header.Timestamp
        };
    }

    private static RtpPacket CreatePacket(ushort sequenceNumber, uint timestamp, byte payloadType, byte[] payload)
    {
        return CreatePacket(sequenceNumber, timestamp, payloadType, payload, ssrc: 1);
    }

    private static RtpPacket CreatePacket(ushort sequenceNumber, uint timestamp, byte payloadType, byte[] payload, uint ssrc)
    {
        return new RtpPacket
        {
            Header = new RtpHeader
            {
                PayloadType = payloadType,
                SequenceNumber = sequenceNumber,
                Timestamp = timestamp,
                Ssrc = ssrc
            },
            Payload = payload,
            ArrivalTime = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class CollectingPacketSink : IRtpPacketSink
    {
        public List<RtpPacket> Packets { get; } = [];

        public bool TryWrite(in RtpPacket packet)
        {
            Packets.Add(packet);
            return true;
        }
    }

    private sealed class SinglePacketSink : IRtpPacketSink
    {
        public RtpPacket Packet { get; private set; }

        public bool TryWrite(in RtpPacket packet)
        {
            Packet = packet;
            return true;
        }
    }

    private sealed class RejectingPacketSink : IRtpPacketSink
    {
        public bool TryWrite(in RtpPacket packet)
        {
            return false;
        }
    }

    private sealed class CollectingAccessUnitSink : IRtpAudioAccessUnitSink
    {
        public List<RtpAudioAccessUnitEvent> Events { get; } = [];

        public bool TryWrite(in RtpAudioAccessUnitEvent accessUnitEvent)
        {
            Events.Add(accessUnitEvent);
            return true;
        }
    }

    private sealed class CountingAccessUnitSink : IRtpAudioAccessUnitSink
    {
        public int Count { get; private set; }

        public bool TryWrite(in RtpAudioAccessUnitEvent accessUnitEvent)
        {
            Count++;
            return true;
        }
    }

    private sealed class RejectingAccessUnitSink : IRtpAudioAccessUnitSink
    {
        public bool TryWrite(in RtpAudioAccessUnitEvent accessUnitEvent)
        {
            return false;
        }
    }

    private sealed class MutableFormatMap : IRtpAudioFormatMap
    {
        private readonly RtpAudioFormatBinding binding;

        public MutableFormatMap(ulong version, RtpAudioFormatBinding binding)
        {
            Version = version;
            this.binding = binding;
        }

        public ulong Version { get; set; }

        public bool TryGetFormat(byte payloadType, out RtpAudioFormatBinding value)
        {
            if (payloadType == binding.PayloadType)
            {
                value = binding;
                return true;
            }

            value = default;
            return false;
        }
    }
}
