using System.Buffers;
using System.Text;

namespace HPD.Agent.Audio.LiveKit;

/// <summary>
/// The admitted LiveKit FFI protobuf subset. This intentionally does not take a
/// dependency on the community wrapper or Google.Protobuf; the field numbers
/// are pinned by the B1 descriptor lock and the B2 manifest gate.
/// </summary>
internal static class LiveKitFfiProtocolCodec
{
    internal readonly record struct ConnectedHandles(ulong Room, ulong Participant);
    internal readonly record struct SubscribedTrack(string ParticipantIdentity, ulong Track, string Sid, bool IsAudio);
    internal enum RoomObservation : byte { Other, Reconnecting, Reconnected, Disconnected }

    internal static byte[] Connect(string endpoint, ReadOnlySpan<char> token, LiveKitTransportProviderConfig options)
    {
        var body = new ProtoWriter();
        body.String(1, endpoint);
        body.String(2, token);
        body.Message(3, room =>
        {
            room.Bool(1, options.AutoSubscribe);
            room.Bool(2, options.AdaptiveStream);
            room.Bool(3, options.Dynacast);
            room.UInt32(9, 15_000);
        });
        return Envelope(3, body);
    }

    internal static byte[] Ready(ulong roomHandle) => Envelope(83, Message(static (writer, state) => writer.UInt64(1, state), roomHandle));
    internal static byte[] NewAudioStream(ulong trackHandle, int sampleRate, int channels, uint queueFrames = 64) =>
        Envelope(25, Message((writer, _) =>
        {
            writer.UInt64(1, trackHandle); writer.UInt32(2, 0); writer.UInt32(3, checked((uint)sampleRate));
            writer.UInt32(4, checked((uint)channels)); writer.UInt32(7, 10); writer.UInt32(8, queueFrames);
        }, 0));
    internal static byte[] NewAudioSource(int sampleRate, int channels, uint queueMilliseconds = 1_000) =>
        Envelope(26, Message((writer, _) =>
        {
            writer.UInt32(1, 0); writer.UInt32(3, checked((uint)sampleRate));
            writer.UInt32(4, checked((uint)channels)); writer.UInt32(5, queueMilliseconds);
        }, 0));
    internal static byte[] CreateAudioTrack(string name, ulong sourceHandle) => Envelope(16, Message((writer, _) =>
    {
        writer.String(1, name); writer.UInt64(2, sourceHandle);
    }, 0));
    internal static byte[] PublishTrack(ulong participantHandle, ulong trackHandle) => Envelope(5, Message((writer, _) =>
    {
        writer.UInt64(1, participantHandle); writer.UInt64(2, trackHandle);
        writer.Message(3, options => options.UInt32(7, 2)); // TrackSource.Microphone
    }, 0));
    internal static byte[] ClearAudioBuffer(ulong sourceHandle) => Envelope(28, Message(static (writer, state) => writer.UInt64(1, state), sourceHandle));
    internal static byte[] UnpublishTrack(ulong participantHandle, string publicationSid) => Envelope(6, Message((writer, _) =>
    {
        writer.UInt64(1, participantHandle); writer.String(2, publicationSid); writer.Bool(3, true);
    }, 0));
    internal static byte[] Disconnect(ulong roomHandle) => Envelope(4, Message(static (writer, state) => writer.UInt64(1, state), roomHandle));

    internal static byte[] CaptureAudioFrame(ulong sourceHandle, nint pcm, int channels, int sampleRate, int samplesPerChannel)
    {
        var body = new ProtoWriter();
        body.UInt64(1, sourceHandle);
        body.Message(2, info =>
        {
            info.UInt64(1, checked((ulong)pcm)); info.UInt32(2, checked((uint)channels));
            info.UInt32(3, checked((uint)sampleRate)); info.UInt32(4, checked((uint)samplesPerChannel));
        });
        return Envelope(27, body);
    }

    internal static ConnectedHandles DecodeConnectCompletion(ReadOnlySpan<byte> envelope)
    {
        var callback = RequiredMessage(envelope, 5, "connect callback");
        ThrowIfError(callback);
        var result = RequiredMessage(callback, 3, "connect result");
        return new(OwnedHandle(RequiredMessage(result, 1, "owned room")), OwnedHandle(RequiredMessage(result, 2, "owned participant")));
    }

    internal static ulong DecodeOwnedHandleResponse(ReadOnlySpan<byte> envelope, int responseField, string description)
    {
        var response = RequiredMessage(envelope, responseField, description);
        return OwnedHandle(RequiredMessage(response, 1, $"owned {description}"));
    }

    internal static (ulong Handle, string Sid) DecodePublishCompletion(ReadOnlySpan<byte> envelope)
    {
        var callback = RequiredMessage(envelope, 9, "publish callback");
        ThrowIfError(callback);
        var publication = RequiredMessage(callback, 3, "owned publication");
        return (OwnedHandle(publication), ReadString(RequiredMessage(publication, 2, "publication info"), 1));
    }

    internal static void DecodeOperationSuccess(ReadOnlySpan<byte> envelope, int callbackField, string description)
    {
        var callback = RequiredMessage(envelope, callbackField, description);
        ThrowIfError(callback);
    }

    internal static bool TryDecodeTrackSubscribed(ReadOnlySpan<byte> envelope, out ulong roomHandle, out SubscribedTrack track)
    {
        track = default;
        var roomEvent = RequiredMessage(envelope, 1, "room event");
        roomHandle = ReadVarintField(roomEvent, 1);
        if (!TryMessage(roomEvent, 9, out var subscribed)) return false;
        var ownedTrack = RequiredMessage(subscribed, 2, "subscribed track");
        var info = RequiredMessage(ownedTrack, 2, "track info");
        var kind = ReadVarintField(info, 3);
        track = new(ReadString(subscribed, 1), OwnedHandle(ownedTrack), ReadString(info, 1), kind == 1);
        return true;
    }

    internal static RoomObservation DecodeRoomObservation(ReadOnlySpan<byte> envelope)
    {
        var room = RequiredMessage(envelope, 1, "room event");
        return HasField(room, 23) ? RoomObservation.Reconnecting
            : HasField(room, 24) ? RoomObservation.Reconnected
            : HasField(room, 22) ? RoomObservation.Disconnected
            : RoomObservation.Other;
    }

    internal static ulong DecodeStreamHandle(ReadOnlySpan<byte> envelope) => DecodeOwnedHandleResponse(envelope, 25, "audio stream response");

    private static byte[] Envelope(int field, ProtoWriter body)
    {
        var writer = new ProtoWriter(); writer.Bytes(field, body.WrittenSpan); return writer.ToArray();
    }
    private static ProtoWriter Message<T>(Action<ProtoWriter, T> write, T state) { var writer = new ProtoWriter(); write(writer, state); return writer; }
    private static ulong OwnedHandle(ReadOnlySpan<byte> owned) => ReadVarintField(RequiredMessage(owned, 1, "owned handle"), 1);
    private static void ThrowIfError(ReadOnlySpan<byte> callback)
    {
        var error = ReadString(callback, 2);
        if (!string.IsNullOrEmpty(error)) throw new InvalidDataException($"LiveKit operation failed: {error}");
    }
    private static ReadOnlySpan<byte> RequiredMessage(ReadOnlySpan<byte> message, int field, string description) =>
        TryMessage(message, field, out var value) ? value : throw new InvalidDataException($"LiveKit message lacked {description}.");
    private static bool TryMessage(ReadOnlySpan<byte> message, int field, out ReadOnlySpan<byte> value)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset); var number = checked((int)(key >> 3)); var wire = (int)(key & 7);
            if (number == field && wire == 2)
            {
                var length = checked((int)ReadVarint(message, ref offset));
                if (length < 0 || offset + length > message.Length) throw new InvalidDataException("Truncated LiveKit protobuf field.");
                value = message.Slice(offset, length); return true;
            }
            Skip(message, ref offset, wire);
        }
        value = default; return false;
    }
    private static bool HasField(ReadOnlySpan<byte> message, int field)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset); var number = checked((int)(key >> 3)); var wire = (int)(key & 7);
            if (number == field) return true;
            Skip(message, ref offset, wire);
        }
        return false;
    }
    private static ulong ReadVarintField(ReadOnlySpan<byte> message, int field)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset); var number = checked((int)(key >> 3)); var wire = (int)(key & 7);
            if (number == field && wire == 0) return ReadVarint(message, ref offset);
            Skip(message, ref offset, wire);
        }
        throw new InvalidDataException($"LiveKit message lacked varint field {field}.");
    }
    private static string ReadString(ReadOnlySpan<byte> message, int field)
    {
        if (!TryMessage(message, field, out var bytes)) return string.Empty;
        return Encoding.UTF8.GetString(bytes);
    }
    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if ((uint)offset >= (uint)bytes.Length) throw new InvalidDataException("Truncated LiveKit protobuf varint.");
            var current = bytes[offset++]; value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0) return value;
        }
        throw new InvalidDataException("LiveKit protobuf varint exceeded 64 bits.");
    }
    private static void Skip(ReadOnlySpan<byte> bytes, ref int offset, int wire)
    {
        switch (wire)
        {
            case 0: ReadVarint(bytes, ref offset); break;
            case 1: offset = checked(offset + 8); break;
            case 2:
                var length = checked((int)ReadVarint(bytes, ref offset));
                offset = checked(offset + length);
                break;
            case 5: offset = checked(offset + 4); break;
            default: throw new InvalidDataException($"Unsupported LiveKit protobuf wire type {wire}.");
        }
        if ((uint)offset > (uint)bytes.Length) throw new InvalidDataException("Truncated LiveKit protobuf field.");
    }

    private sealed class ProtoWriter
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();
        internal ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;
        internal void UInt32(int field, uint value) => UInt64(field, value);
        internal void UInt64(int field, ulong value) { Varint((ulong)field << 3); Varint(value); }
        internal void Bool(int field, bool value) { if (value) UInt64(field, 1); }
        internal void String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
        internal void String(int field, ReadOnlySpan<char> value) => Bytes(field, Encoding.UTF8.GetBytes(value.ToString()));
        internal void Message(int field, Action<ProtoWriter> write) { var nested = new ProtoWriter(); write(nested); Bytes(field, nested.WrittenSpan); }
        internal void Bytes(int field, ReadOnlySpan<byte> value)
        {
            Varint(((ulong)field << 3) | 2); Varint(checked((ulong)value.Length));
            var target = _buffer.GetSpan(value.Length); value.CopyTo(target); _buffer.Advance(value.Length);
        }
        internal byte[] ToArray() => _buffer.WrittenSpan.ToArray();
        private void Varint(ulong value)
        {
            var target = _buffer.GetSpan(10); var count = 0;
            while (value >= 0x80) { target[count++] = (byte)(value | 0x80); value >>= 7; }
            target[count++] = (byte)value; _buffer.Advance(count);
        }
    }
}

