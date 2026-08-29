using System.Runtime.InteropServices;
using HPD.Agent.Audio.LiveKit.Generated;

namespace HPD.Agent.Audio.LiveKit;

internal sealed class LiveKitFfiWireDecoder : ILiveKitFfiWireDecoder
{
    public LiveKitFfiIssuedResponse DecodeResponse(ReadOnlySpan<byte> bytes)
    {
        var envelope = ReadOneofEnvelope(bytes);
        var responseCase = (LiveKitFfiResponseCase)envelope.FieldNumber;
        if (!LiveKitFfiGeneratedProtocol.TryGetOperation(responseCase, out _))
            throw new InvalidDataException($"Unadmitted LiveKit response case {envelope.FieldNumber}.");
        var asyncId = RequiresIssuedResponse(responseCase) ? ReadRequiredAsyncId(envelope.Payload) : 0;
        return new(responseCase, asyncId, bytes.ToArray());
    }

    public LiveKitFfiDecodedEvent DecodeEvent(ReadOnlySpan<byte> bytes)
    {
        var envelope = ReadOneofEnvelope(bytes);
        var eventCase = (LiveKitFfiEventCase)envelope.FieldNumber;
        if (!IsAdmittedEvent(eventCase))
            throw new InvalidDataException($"Unadmitted LiveKit event case {envelope.FieldNumber}.");
        var asyncId = eventCase is LiveKitFfiEventCase.Connect or LiveKitFfiEventCase.PublishTrack or
            LiveKitFfiEventCase.CaptureAudioFrame or LiveKitFfiEventCase.UnpublishTrack or LiveKitFfiEventCase.Disconnect
            ? ReadRequiredAsyncId(envelope.Payload)
            : 0;
        if (eventCase == LiveKitFfiEventCase.AudioStreamEvent)
        {
            var observation = DecodeAudioStream(envelope.Payload);
            return new(eventCase, 0, bytes.ToArray(), observation.StreamHandle, observation.Frame, observation.Ended);
        }
        var ownerHandle = eventCase == LiveKitFfiEventCase.RoomEvent
            ? ReadRequiredVarintField(envelope.Payload, 1, "room handle")
            : 0;
        return new(eventCase, asyncId, bytes.ToArray(), ownerHandle);
    }

    private static bool RequiresIssuedResponse(LiveKitFfiResponseCase value) =>
        value is LiveKitFfiResponseCase.Connect or LiveKitFfiResponseCase.PublishTrack or
            LiveKitFfiResponseCase.CaptureAudioFrame or LiveKitFfiResponseCase.UnpublishTrack or
            LiveKitFfiResponseCase.Disconnect;

    private static bool IsAdmittedEvent(LiveKitFfiEventCase value)
    {
        foreach (var admitted in LiveKitFfiGeneratedProtocol.Events)
            if (admitted == value) return true;
        return false;
    }

    private static Envelope ReadOneofEnvelope(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        var key = ReadVarint(bytes, ref offset);
        var fieldNumber = checked((int)(key >> 3));
        if (fieldNumber == 0 || (key & 7) != 2) throw new InvalidDataException("LiveKit envelope was not a length-delimited oneof.");
        var length = checked((int)ReadVarint(bytes, ref offset));
        if (length < 0 || offset + length != bytes.Length) throw new InvalidDataException("LiveKit envelope length was invalid or had trailing fields.");
        return new(fieldNumber, bytes.Slice(offset, length));
    }

    private static ulong ReadRequiredAsyncId(ReadOnlySpan<byte> message)
        => ReadRequiredVarintField(message, 1, "async ID");

    private static ulong ReadRequiredVarintField(ReadOnlySpan<byte> message, int requiredField, string description)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var field = checked((int)(key >> 3));
            var wire = checked((int)(key & 7));
            if (field == requiredField && wire == 0)
            {
                var value = ReadVarint(message, ref offset);
                if (value == 0) throw new InvalidDataException($"LiveKit message had a zero {description}.");
                return value;
            }
            Skip(message, ref offset, wire);
        }
        throw new InvalidDataException($"LiveKit message lacked {description} field {requiredField}.");
    }

    private static AudioObservation DecodeAudioStream(ReadOnlySpan<byte> message)
    {
        var streamHandle = ReadRequiredVarintField(message, 1, "audio stream handle");
        if (!TryReadMessageField(message, 2, out var received))
        {
            if (TryReadMessageField(message, 3, out _)) return new(streamHandle, null, true);
            throw new InvalidDataException("LiveKit audio stream event had neither a frame nor EOS.");
        }
        if (!TryReadMessageField(received, 1, out var owned))
            throw new InvalidDataException("LiveKit audio frame wrapper was incomplete.");
        if (!TryReadMessageField(owned, 1, out var handleMessage))
            throw new InvalidDataException("LiveKit audio frame handle was absent.");
        if (!TryReadMessageField(owned, 2, out var info))
            throw new InvalidDataException("LiveKit audio frame info was absent.");
        var frameHandle = ReadRequiredVarintField(handleMessage, 1, "audio frame handle");
        var pointer = ReadRequiredVarintField(info, 1, "audio frame data pointer");
        var channels = checked((int)ReadRequiredVarintField(info, 2, "audio channel count"));
        var sampleRate = checked((int)ReadRequiredVarintField(info, 3, "audio sample rate"));
        var samples = checked((int)ReadRequiredVarintField(info, 4, "audio samples per channel"));
        var byteLength = checked(channels * samples * sizeof(short));
        if (channels is < 1 or > 32 || sampleRate is < 8_000 or > 384_000 || samples <= 0 || byteLength > 16 * 1024 * 1024)
            throw new InvalidDataException("LiveKit audio frame format was outside the admitted bounds.");
        var pcm = new byte[byteLength];
        Marshal.Copy(checked((nint)pointer), pcm, 0, pcm.Length);
        return new(streamHandle, new(frameHandle, pcm, channels, sampleRate, samples), false);
    }

    private static bool TryReadMessageField(ReadOnlySpan<byte> message, int requiredField, out ReadOnlySpan<byte> value)
    {
        var offset = 0;
        while (offset < message.Length)
        {
            var key = ReadVarint(message, ref offset);
            var field = checked((int)(key >> 3));
            var wire = checked((int)(key & 7));
            if (field == requiredField && wire == 2)
            {
                var length = checked((int)ReadVarint(message, ref offset));
                if (length < 0 || offset + length > message.Length) throw new InvalidDataException("Truncated LiveKit protobuf message field.");
                value = message.Slice(offset, length);
                return true;
            }
            Skip(message, ref offset, wire);
        }
        value = default;
        return false;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if ((uint)offset >= (uint)bytes.Length) throw new InvalidDataException("Truncated LiveKit protobuf varint.");
            var current = bytes[offset++];
            value |= (ulong)(current & 0x7f) << shift;
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

    private readonly ref struct Envelope(int fieldNumber, ReadOnlySpan<byte> payload)
    {
        internal int FieldNumber { get; } = fieldNumber;
        internal ReadOnlySpan<byte> Payload { get; } = payload;
    }

    private readonly record struct AudioObservation(ulong StreamHandle, LiveKitFfiInboundAudioFrame? Frame, bool Ended);
}

