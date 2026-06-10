#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using HPD.Buffers;
using HPD.Media.Diagnostics;

namespace HPD.Media.Rtp;

/// <summary>
/// Represents the RTP fixed header and extension block metadata.
/// </summary>
public readonly struct RtpHeader
{
    /// <summary>Gets the RTP payload type.</summary>
    public required byte PayloadType { get; init; }

    /// <summary>Gets the RTP sequence number.</summary>
    public required ushort SequenceNumber { get; init; }

    /// <summary>Gets the RTP timestamp.</summary>
    public required uint Timestamp { get; init; }

    /// <summary>Gets the RTP synchronization source identifier.</summary>
    public required uint Ssrc { get; init; }

    /// <summary>Gets a value indicating whether the RTP marker bit is set.</summary>
    public bool Marker { get; init; }

    /// <summary>Gets a value indicating whether the RTP padding bit is set.</summary>
    public bool Padding { get; init; }

    /// <summary>Gets the RTP header-extension profile, or zero when absent.</summary>
    public ushort ExtensionProfile { get; init; }

    /// <summary>Gets the number of contributing source identifiers in the packet.</summary>
    public byte CsrcCount { get; init; }
}

/// <summary>
/// Represents one RTP packet whose bytes are safe to retain across async boundaries.
/// </summary>
public readonly struct RtpPacket
{
    /// <summary>Gets the RTP header.</summary>
    public required RtpHeader Header { get; init; }

    /// <summary>Gets contributing source identifiers for retained packets.</summary>
    public ReadOnlyMemory<uint> Csrcs { get; init; }

    /// <summary>Gets the RTP payload bytes.</summary>
    public required ReadOnlyMemory<byte> Payload { get; init; }

    /// <summary>Gets the raw RTP header extension bytes after the extension profile header.</summary>
    public ReadOnlyMemory<byte> ExtensionData { get; init; }

    /// <summary>Gets the packet arrival time.</summary>
    public required DateTimeOffset ArrivalTime { get; init; }
}

/// <summary>
/// Transfers ownership of a retained RTP packet backed by leased memory.
/// </summary>
public readonly struct OwnedRtpPacket : IDisposable
{
    /// <summary>Gets the retained RTP packet.</summary>
    public required RtpPacket Packet { get; init; }

    /// <summary>Gets the lease that owns the memory referenced by the packet payload and extensions.</summary>
    public required IByteBufferLease Lease { get; init; }

    /// <summary>Releases the owned RTP packet memory.</summary>
    public void Dispose() => Lease.Dispose();
}

/// <summary>
/// Represents a parsed RTP packet view over caller-owned bytes.
/// </summary>
public readonly ref struct RtpPacketView
{
    /// <summary>Initializes a new instance of the <see cref="RtpPacketView"/> struct.</summary>
    public RtpPacketView(RtpHeader header, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> extensionData)
        : this(header, ReadOnlySpan<byte>.Empty, payload, extensionData)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RtpPacketView"/> struct.</summary>
    public RtpPacketView(RtpHeader header, ReadOnlySpan<byte> csrcData, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> extensionData)
    {
        Header = header;
        CsrcData = csrcData;
        Payload = payload;
        ExtensionData = extensionData;
    }

    /// <summary>Gets the parsed RTP header.</summary>
    public RtpHeader Header { get; }

    /// <summary>Gets raw contributing source identifier bytes in network byte order.</summary>
    public ReadOnlySpan<byte> CsrcData { get; }

    /// <summary>Gets the RTP payload view.</summary>
    public ReadOnlySpan<byte> Payload { get; }

    /// <summary>Gets the RTP header extension data view.</summary>
    public ReadOnlySpan<byte> ExtensionData { get; }

    /// <summary>Gets a zero-allocation enumerator over RTP header extension elements.</summary>
    public RtpHeaderExtensionEnumerator GetHeaderExtensions() =>
        Header.ExtensionProfile == RtpHeaderExtensionEnumerator.OneByteHeaderProfile
            ? new RtpHeaderExtensionEnumerator(ExtensionData)
            : new RtpHeaderExtensionEnumerator(ReadOnlySpan<byte>.Empty);

    /// <summary>Gets a zero-allocation enumerator over RTP CSRC identifiers.</summary>
    public RtpCsrcEnumerator GetCsrcs() => new(CsrcData);
}

/// <summary>
/// Iterates RTP contributing source identifiers without allocating per packet.
/// </summary>
public ref struct RtpCsrcEnumerator
{
    private ReadOnlySpan<byte> remaining;

    /// <summary>Initializes a new instance of the <see cref="RtpCsrcEnumerator"/> struct.</summary>
    public RtpCsrcEnumerator(ReadOnlySpan<byte> csrcData)
    {
        remaining = csrcData;
        Current = 0;
    }

    /// <summary>Gets the current CSRC identifier.</summary>
    public uint Current { get; private set; }

    /// <summary>Advances to the next CSRC identifier.</summary>
    public bool MoveNext()
    {
        if (remaining.Length < 4)
        {
            Current = 0;
            return false;
        }

        Current = BinaryPrimitives.ReadUInt32BigEndian(remaining[..4]);
        remaining = remaining[4..];
        return true;
    }
}

/// <summary>
/// Represents one RTP header extension element.
/// </summary>
public readonly ref struct RtpHeaderExtensionView
{
    /// <summary>Initializes a new instance of the <see cref="RtpHeaderExtensionView"/> struct.</summary>
    public RtpHeaderExtensionView(int id, ReadOnlySpan<byte> data)
    {
        Id = id;
        Data = data;
    }

    /// <summary>Gets the extension identifier.</summary>
    public int Id { get; }

    /// <summary>Gets the extension payload bytes.</summary>
    public ReadOnlySpan<byte> Data { get; }
}

/// <summary>
/// Iterates RTP header extension elements without allocating per packet.
/// </summary>
public ref struct RtpHeaderExtensionEnumerator
{
    /// <summary>Identifies the RTP one-byte header extension profile.</summary>
    public const ushort OneByteHeaderProfile = 0xBEDE;

    private ReadOnlySpan<byte> remaining;

    /// <summary>Initializes a new instance of the <see cref="RtpHeaderExtensionEnumerator"/> struct.</summary>
    public RtpHeaderExtensionEnumerator(ReadOnlySpan<byte> extensionData)
    {
        remaining = extensionData;
        Current = default;
    }

    /// <summary>Gets the current RTP header extension element.</summary>
    public RtpHeaderExtensionView Current { get; private set; }

    /// <summary>Advances to the next RTP header extension element.</summary>
    public bool MoveNext()
    {
        Current = default;
        while (!remaining.IsEmpty)
        {
            byte descriptor = remaining[0];
            remaining = remaining[1..];

            if (descriptor == 0)
            {
                continue;
            }

            int id = descriptor >> 4;
            if (id == 15)
            {
                remaining = ReadOnlySpan<byte>.Empty;
                return false;
            }

            int length = (descriptor & 0x0F) + 1;
            if (remaining.Length < length)
            {
                remaining = ReadOnlySpan<byte>.Empty;
                return false;
            }

            Current = new RtpHeaderExtensionView(id, remaining[..length]);
            remaining = remaining[length..];
            return true;
        }

        return false;
    }
}

/// <summary>
/// Classifies RTP packet parse and write results without using exceptions for normal packet flow.
/// </summary>
public enum RtpPacketStatus
{
    Success = 0,
    InvalidPacket = 1,
    DestinationTooSmall = 2,
    UnsupportedVersion = 3,
    MalformedExtension = 4
}

/// <summary>
/// Parses RTP packets from caller-owned spans.
/// </summary>
public static class RtpPacketReader
{
    private const int FixedHeaderLength = 12;
    private const byte RtpVersion = 2;

    /// <summary>Attempts to parse an RTP packet view from a complete packet.</summary>
    public static RtpPacketStatus TryParse(ReadOnlySpan<byte> packet, out RtpPacketView view)
    {
        view = default;

        if (packet.Length < FixedHeaderLength)
        {
            return RtpPacketStatus.InvalidPacket;
        }

        byte first = packet[0];
        int version = first >> 6;
        if (version != RtpVersion)
        {
            return RtpPacketStatus.UnsupportedVersion;
        }

        bool hasPadding = (first & 0x20) != 0;
        bool hasExtension = (first & 0x10) != 0;
        byte csrcCount = (byte)(first & 0x0F);
        int csrcLength = csrcCount * 4;
        int cursor = FixedHeaderLength + csrcLength;

        if (packet.Length < cursor)
        {
            return RtpPacketStatus.InvalidPacket;
        }

        ReadOnlySpan<byte> csrcData = packet.Slice(FixedHeaderLength, csrcLength);
        ushort extensionProfile = 0;
        ReadOnlySpan<byte> extensionData = ReadOnlySpan<byte>.Empty;
        if (hasExtension)
        {
            if (packet.Length < cursor + 4)
            {
                return RtpPacketStatus.MalformedExtension;
            }

            extensionProfile = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(cursor, 2));
            ushort extensionWords = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(cursor + 2, 2));
            int extensionLength = checked(extensionWords * 4);
            cursor += 4;

            if (packet.Length < cursor + extensionLength)
            {
                return RtpPacketStatus.MalformedExtension;
            }

            extensionData = packet.Slice(cursor, extensionLength);
            if (extensionProfile == RtpHeaderExtensionEnumerator.OneByteHeaderProfile &&
                !RtpHeaderExtensionValidator.IsValidOneByteHeaderExtensionBlock(extensionData))
            {
                return RtpPacketStatus.MalformedExtension;
            }

            cursor += extensionLength;
        }

        int payloadEnd = packet.Length;
        if (hasPadding)
        {
            int paddingBytes = packet[^1];
            if (paddingBytes == 0 || paddingBytes > payloadEnd - cursor)
            {
                return RtpPacketStatus.InvalidPacket;
            }

            payloadEnd -= paddingBytes;
        }

        var header = new RtpHeader
        {
            PayloadType = (byte)(packet[1] & 0x7F),
            Marker = (packet[1] & 0x80) != 0,
            SequenceNumber = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2)),
            Timestamp = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4)),
            Ssrc = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(8, 4)),
            Padding = hasPadding,
            ExtensionProfile = extensionProfile,
            CsrcCount = csrcCount
        };

        view = new RtpPacketView(header, csrcData, packet[cursor..payloadEnd], extensionData);
        return RtpPacketStatus.Success;
    }
}

/// <summary>
/// Writes RTP packets to caller-provided storage.
/// </summary>
public static class RtpPacketWriter
{
    private const int FixedHeaderLength = 12;
    private const int MaximumExtensionDataBytes = ushort.MaxValue * 4;
    private const byte RtpVersion = 2;

    /// <summary>Attempts to write an RTP packet into a caller-provided span.</summary>
    public static RtpPacketStatus TryWrite(in RtpPacket packet, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;

        RtpPacketStatus validationStatus = ValidatePacket(packet, out int requiredLength, out bool hasExtension);
        if (validationStatus != RtpPacketStatus.Success)
        {
            return validationStatus;
        }

        int csrcCount = packet.Csrcs.Length;
        int csrcLength = csrcCount * 4;
        if (destination.Length < requiredLength)
        {
            return RtpPacketStatus.DestinationTooSmall;
        }

        destination[0] = (byte)((RtpVersion << 6) | csrcCount);
        if (hasExtension)
        {
            destination[0] |= 0x10;
        }

        destination[1] = packet.Header.PayloadType;
        if (packet.Header.Marker)
        {
            destination[1] |= 0x80;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), packet.Header.SequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), packet.Header.Timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), packet.Header.Ssrc);

        int cursor = FixedHeaderLength;
        ReadOnlySpan<uint> csrcs = packet.Csrcs.Span;
        for (int i = 0; i < csrcs.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor, 4), csrcs[i]);
            cursor += 4;
        }

        if (hasExtension)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(cursor, 2), packet.Header.ExtensionProfile);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(cursor + 2, 2), checked((ushort)(packet.ExtensionData.Length / 4)));
            cursor += 4;
            packet.ExtensionData.Span.CopyTo(destination[cursor..]);
            cursor += packet.ExtensionData.Length;
        }

        packet.Payload.Span.CopyTo(destination[cursor..]);
        bytesWritten = requiredLength;
        return RtpPacketStatus.Success;
    }

    /// <summary>Attempts to write an RTP packet from caller-owned spans without creating a retained packet value.</summary>
    public static RtpPacketStatus TryWrite(
        in RtpHeader header,
        ReadOnlySpan<uint> csrcs,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> extensionData,
        Span<byte> destination,
        out int bytesWritten)
    {
        bytesWritten = 0;

        RtpPacketStatus validationStatus = ValidatePacket(header, csrcs.Length, payload.Length, extensionData, out int requiredLength, out bool hasExtension);
        if (validationStatus != RtpPacketStatus.Success)
        {
            return validationStatus;
        }

        if (destination.Length < requiredLength)
        {
            return RtpPacketStatus.DestinationTooSmall;
        }

        destination[0] = (byte)((RtpVersion << 6) | csrcs.Length);
        if (hasExtension)
        {
            destination[0] |= 0x10;
        }

        destination[1] = header.PayloadType;
        if (header.Marker)
        {
            destination[1] |= 0x80;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(2, 2), header.SequenceNumber);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(4, 4), header.Timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), header.Ssrc);

        int cursor = FixedHeaderLength;
        for (int i = 0; i < csrcs.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(cursor, 4), csrcs[i]);
            cursor += 4;
        }

        if (hasExtension)
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(cursor, 2), header.ExtensionProfile);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(cursor + 2, 2), checked((ushort)(extensionData.Length / 4)));
            cursor += 4;
            extensionData.CopyTo(destination[cursor..]);
            cursor += extensionData.Length;
        }

        payload.CopyTo(destination[cursor..]);
        bytesWritten = requiredLength;
        return RtpPacketStatus.Success;
    }

    /// <summary>Writes an RTP packet to a caller-provided buffer writer.</summary>
    public static void Write(in RtpPacket packet, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        RtpPacketStatus validationStatus = ValidatePacket(packet, out int requiredLength, out _);
        if (validationStatus != RtpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTP packet write failed with status {validationStatus}.");
        }

        Span<byte> span = destination.GetSpan(requiredLength);
        RtpPacketStatus status = TryWrite(packet, span, out int bytesWritten);
        if (status != RtpPacketStatus.Success)
        {
            throw new InvalidOperationException($"RTP packet write failed with status {status}.");
        }

        destination.Advance(bytesWritten);
    }

    private static RtpPacketStatus ValidatePacket(in RtpPacket packet, out int requiredLength, out bool hasExtension)
    {
        requiredLength = 0;
        hasExtension = false;

        return ValidatePacket(packet.Header, packet.Csrcs.Length, packet.Payload.Length, packet.ExtensionData.Span, out requiredLength, out hasExtension);
    }

    private static RtpPacketStatus ValidatePacket(
        in RtpHeader header,
        int csrcCount,
        int payloadLength,
        ReadOnlySpan<byte> extensionData,
        out int requiredLength,
        out bool hasExtension)
    {
        requiredLength = 0;
        hasExtension = false;

        if (header.PayloadType > 127 || header.Padding)
        {
            return RtpPacketStatus.InvalidPacket;
        }

        if (csrcCount > 15 || header.CsrcCount != csrcCount)
        {
            return RtpPacketStatus.InvalidPacket;
        }

        hasExtension = header.ExtensionProfile != 0 || !extensionData.IsEmpty;
        if (hasExtension &&
            (header.ExtensionProfile == 0 ||
             extensionData.Length % 4 != 0 ||
             extensionData.Length > MaximumExtensionDataBytes ||
             (header.ExtensionProfile == RtpHeaderExtensionEnumerator.OneByteHeaderProfile &&
              !RtpHeaderExtensionValidator.IsValidOneByteHeaderExtensionBlock(extensionData))))
        {
            return RtpPacketStatus.MalformedExtension;
        }

        int extensionHeaderLength = hasExtension ? 4 : 0;
        long required = (long)FixedHeaderLength +
            (csrcCount * 4L) +
            extensionHeaderLength +
            extensionData.Length +
            payloadLength;
        if (required > int.MaxValue)
        {
            return RtpPacketStatus.DestinationTooSmall;
        }

        requiredLength = (int)required;
        return RtpPacketStatus.Success;
    }
}

internal static class RtpHeaderExtensionValidator
{
    public static bool IsValidOneByteHeaderExtensionBlock(ReadOnlySpan<byte> extensionData)
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
/// Represents the RTP reorderer output item kind.
/// </summary>
public enum RtpPacketEventKind
{
    Packet = 0,
    Loss = 1
}

/// <summary>
/// Represents an RTP reorderer output item.
/// </summary>
public readonly struct RtpPacketEvent
{
    /// <summary>Gets the event kind.</summary>
    public required RtpPacketEventKind Kind { get; init; }

    /// <summary>Gets the RTP packet when this event carries payload.</summary>
    public RtpPacket Packet { get; init; }

    /// <summary>Gets the affected SSRC.</summary>
    public required uint Ssrc { get; init; }

    /// <summary>Gets the affected RTP sequence number.</summary>
    public required ushort SequenceNumber { get; init; }

    /// <summary>Gets the expected RTP timestamp when known.</summary>
    public uint? ExpectedTimestamp { get; init; }

    /// <summary>Gets the number of packets represented by this loss event.</summary>
    public int LostPacketCount { get; init; }
}

/// <summary>
/// Receives RTP packet events without requiring collection allocation.
/// </summary>
public interface IRtpPacketEventSink
{
    /// <summary>Attempts to accept one RTP packet event.</summary>
    bool TryWrite(in RtpPacketEvent packetEvent);
}

/// <summary>
/// Orders RTP packets and emits explicit packet-loss events.
/// </summary>
public interface IRtpPacketReorderer
{
    /// <summary>Adds a received RTP packet to the reorderer.</summary>
    bool TryPush(in RtpPacket packet);

    /// <summary>Drains currently available ordered packets and loss events to <paramref name="sink"/>.</summary>
    bool TryReadAvailable(IRtpPacketEventSink sink);

    /// <summary>Reads one ordered packet or loss event asynchronously.</summary>
    ValueTask<RtpPacketEvent?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Completes asynchronous reads after currently available packet events are drained.</summary>
    void Complete();
}

/// <summary>
/// Orders retained RTP packets for one SSRC using bounded construction-time storage.
/// </summary>
public sealed class RtpPacketReorderer : IRtpPacketReorderer
{
    private const int DefaultCapacity = 64;
    private const int DefaultMaxReorderDistance = 8;
    private const int MaximumExtensionDataBytes = ushort.MaxValue * 4;

    private readonly RtpPacket[] packets;
    private readonly bool[] occupied;
    private readonly SingleEventSink readSink = new();
    private readonly Queue<RtpReadWaiter> readWaiters = new();
    private readonly object gate = new();
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly int maxReorderDistance;
    private readonly bool hasTelemetry;
    private bool initialized;
    private uint ssrc;
    private ushort nextSequenceNumber;
    private ushort highestSequenceNumber;
    private int bufferedPacketCount;
    private bool hasPendingEvent;
    private RtpPacketEvent pendingEvent;
    private bool completed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RtpPacketReorderer"/> class.
    /// </summary>
    public RtpPacketReorderer(int capacity = DefaultCapacity, int maxReorderDistance = DefaultMaxReorderDistance)
        : this(default, hasTelemetry: false, capacity, maxReorderDistance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RtpPacketReorderer"/> class with cached telemetry emitters.
    /// </summary>
    public RtpPacketReorderer(
        RealtimeMediaTelemetryEmitters telemetry,
        int capacity = DefaultCapacity,
        int maxReorderDistance = DefaultMaxReorderDistance)
        : this(telemetry, hasTelemetry: true, capacity, maxReorderDistance)
    {
    }

    private RtpPacketReorderer(
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry,
        int capacity,
        int maxReorderDistance)
    {
        if (capacity <= 0 || capacity > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (maxReorderDistance < 0 || maxReorderDistance >= capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReorderDistance));
        }

        packets = new RtpPacket[capacity];
        occupied = new bool[capacity];
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        this.maxReorderDistance = maxReorderDistance;
    }

    /// <inheritdoc />
    public bool TryPush(in RtpPacket packet)
    {
        List<RtpReadCompletion>? completions = null;
        RtpPacketEvent waiterEvent = default;

        lock (gate)
        {
            if (completed || !IsRetainedPacketUsable(packet))
            {
                return false;
            }

            ushort sequenceNumber = packet.Header.SequenceNumber;
            if (!initialized)
            {
                initialized = true;
                ssrc = packet.Header.Ssrc;
                nextSequenceNumber = sequenceNumber;
                highestSequenceNumber = sequenceNumber;
            }
            else if (packet.Header.Ssrc != ssrc || IsBefore(sequenceNumber, nextSequenceNumber))
            {
                return false;
            }
            else if (ForwardDistance(nextSequenceNumber, sequenceNumber) >= packets.Length)
            {
                return false;
            }

            int index = sequenceNumber % packets.Length;
            if (occupied[index])
            {
                return false;
            }

            packets[index] = packet;
            occupied[index] = true;
            bufferedPacketCount++;
            EmitReorderDepth();

            if (IsNewer(sequenceNumber, highestSequenceNumber))
            {
                highestSequenceNumber = sequenceNumber;
            }

            if (readWaiters.Count != 0 && TryReadOneNoLock(out waiterEvent))
            {
                completions = [];
                do
                {
                    bool delivered = false;
                    while (readWaiters.Count != 0)
                    {
                        RtpReadWaiter waiter = readWaiters.Dequeue();
                        if (waiter.TrySetResult(waiterEvent))
                        {
                            completions.Add(new RtpReadCompletion(waiter));
                            delivered = true;
                            break;
                        }

                        waiter.Dispose();
                    }

                    if (!delivered)
                    {
                        pendingEvent = waiterEvent;
                        hasPendingEvent = true;
                        break;
                    }
                }
                while (readWaiters.Count != 0 && TryReadOneNoLock(out waiterEvent));
            }
        }

        if (completions is not null)
        {
            foreach (RtpReadCompletion completion in completions)
            {
                completion.Waiter.Dispose();
            }
        }

        return true;
    }

    private static bool IsRetainedPacketUsable(in RtpPacket packet)
    {
        if (packet.Header.PayloadType > 127)
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
             RtpHeaderExtensionValidator.IsValidOneByteHeaderExtensionBlock(packet.ExtensionData.Span));
    }

    /// <inheritdoc />
    public bool TryReadAvailable(IRtpPacketEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (gate)
        {
            return TryReadAvailableNoLock(sink);
        }
    }

    private bool TryReadAvailableNoLock(IRtpPacketEventSink sink)
    {
        if (hasPendingEvent)
        {
            if (!sink.TryWrite(pendingEvent))
            {
                return false;
            }

            hasPendingEvent = false;
            pendingEvent = default;
        }

        while (initialized)
        {
            if (TryTakePacket(nextSequenceNumber, out RtpPacket packet))
            {
                var packetEvent = new RtpPacketEvent
                {
                    Kind = RtpPacketEventKind.Packet,
                    Packet = packet,
                    Ssrc = packet.Header.Ssrc,
                    SequenceNumber = packet.Header.SequenceNumber,
                    ExpectedTimestamp = packet.Header.Timestamp
                };

                if (!sink.TryWrite(packetEvent))
                {
                    pendingEvent = packetEvent;
                    hasPendingEvent = true;
                    nextSequenceNumber++;
                    return false;
                }

                nextSequenceNumber++;
                continue;
            }

            if (bufferedPacketCount == 0 || ForwardDistance(nextSequenceNumber, highestSequenceNumber) <= maxReorderDistance)
            {
                return true;
            }

            var lossEvent = new RtpPacketEvent
            {
                Kind = RtpPacketEventKind.Loss,
                Ssrc = ssrc,
                SequenceNumber = nextSequenceNumber,
                LostPacketCount = 1
            };
            EmitLoss(lossEvent);

            if (!sink.TryWrite(lossEvent))
            {
                pendingEvent = lossEvent;
                hasPendingEvent = true;
                nextSequenceNumber++;
                return false;
            }

            nextSequenceNumber++;
        }

        return true;
    }

    /// <inheritdoc />
    public ValueTask<RtpPacketEvent?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (TryReadOneNoLock(out RtpPacketEvent packetEvent))
            {
                return new ValueTask<RtpPacketEvent?>((RtpPacketEvent?)packetEvent);
            }

            if (completed)
            {
                return new ValueTask<RtpPacketEvent?>((RtpPacketEvent?)null);
            }

            var waiter = new RtpReadWaiter(cancellationToken);
            readWaiters.Enqueue(waiter);
            return new ValueTask<RtpPacketEvent?>(waiter.Task);
        }
    }

    /// <inheritdoc />
    public void Complete()
    {
        Queue<RtpReadWaiter> waiters;
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            waiters = new Queue<RtpReadWaiter>(readWaiters);
            readWaiters.Clear();
        }

        while (waiters.Count != 0)
        {
            RtpReadWaiter waiter = waiters.Dequeue();
            _ = waiter.TrySetResult(null);
            waiter.Dispose();
        }
    }

    private readonly struct RtpReadCompletion
    {
        public RtpReadCompletion(RtpReadWaiter waiter)
        {
            Waiter = waiter;
        }

        public RtpReadWaiter Waiter { get; }
    }

    private sealed class RtpReadWaiter : IDisposable
    {
        private readonly CancellationTokenRegistration cancellationRegistration;

        public RtpReadWaiter(CancellationToken cancellationToken)
        {
            Source = new TaskCompletionSource<RtpPacketEvent?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(
                    static state => ((RtpReadWaiter)state!).Source.TrySetCanceled(),
                    this);
            }
        }

        public Task<RtpPacketEvent?> Task => Source.Task;

        private TaskCompletionSource<RtpPacketEvent?> Source { get; }

        public bool TrySetResult(RtpPacketEvent? value) => Source.TrySetResult(value);

        public void Dispose() => cancellationRegistration.Dispose();
    }

    private bool TryReadOneNoLock(out RtpPacketEvent packetEvent)
    {
        readSink.Reset();
        _ = TryReadAvailableNoLock(readSink);
        if (readSink.HasEvent)
        {
            packetEvent = readSink.Event;
            return true;
        }

        packetEvent = default;
        return false;
    }

    private bool TryTakePacket(ushort sequenceNumber, out RtpPacket packet)
    {
        int index = sequenceNumber % packets.Length;
        if (occupied[index] && packets[index].Header.SequenceNumber == sequenceNumber)
        {
            packet = packets[index];
            packets[index] = default;
            occupied[index] = false;
            bufferedPacketCount--;
            EmitReorderDepth();
            return true;
        }

        packet = default;
        return false;
    }

    private static bool IsBefore(ushort sequenceNumber, ushort reference)
    {
        return sequenceNumber != reference && ForwardDistance(sequenceNumber, reference) < 0x8000;
    }

    private static bool IsNewer(ushort sequenceNumber, ushort reference)
    {
        return sequenceNumber != reference && ForwardDistance(reference, sequenceNumber) < 0x8000;
    }

    private static int ForwardDistance(ushort from, ushort to)
    {
        return (ushort)(to - from);
    }

    private void EmitLoss(in RtpPacketEvent lossEvent)
    {
        if (!hasTelemetry)
        {
            return;
        }

        _ = telemetry.RtpLoss.Emit(new RtpLossSample
        {
            Ssrc = lossEvent.Ssrc,
            SequenceStart = lossEvent.SequenceNumber,
            LostPacketCount = lossEvent.LostPacketCount,
            ExpectedTimestamp = lossEvent.ExpectedTimestamp
        });
    }

    private void EmitReorderDepth()
    {
        if (!hasTelemetry || !initialized)
        {
            return;
        }

        _ = telemetry.RtpReorderDepth.Emit(new RtpReorderDepthSample
        {
            Ssrc = ssrc,
            Depth = bufferedPacketCount,
            Capacity = packets.Length
        });
    }

    private sealed class SingleEventSink : IRtpPacketEventSink
    {
        public bool HasEvent { get; private set; }

        public RtpPacketEvent Event { get; private set; }

        public bool TryWrite(in RtpPacketEvent packetEvent)
        {
            if (HasEvent)
            {
                return false;
            }

            Event = packetEvent;
            HasEvent = true;
            return true;
        }

        public void Reset()
        {
            Event = default;
            HasEvent = false;
        }
    }
}
