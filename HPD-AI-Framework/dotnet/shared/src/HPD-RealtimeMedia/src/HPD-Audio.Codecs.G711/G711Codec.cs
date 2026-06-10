#nullable enable

using System.Buffers.Binary;
using HPD.Audio.Primitives;

namespace HPD.Audio.Codecs.G711;

/// <summary>
/// Provides managed G.711 PCMU and PCMA encode/decode primitives.
/// </summary>
public static class G711Codec
{
    private const int MuLawBias = 0x84;
    private const int MuLawClip = 32635;

    private static readonly short[] MuLawSegmentEnd =
    [
        0x00FF, 0x01FF, 0x03FF, 0x07FF,
        0x0FFF, 0x1FFF, 0x3FFF, 0x7FFF
    ];

    private static readonly short[] ALawSegmentEnd =
    [
        0x001F, 0x003F, 0x007F, 0x00FF,
        0x01FF, 0x03FF, 0x07FF, 0x0FFF
    ];

    /// <summary>Gets a value indicating whether an encoding is a G.711 law.</summary>
    public static bool IsSupportedEncoding(AudioEncoding encoding) => encoding is AudioEncoding.Pcmu or AudioEncoding.Pcma;

    /// <summary>Gets a value indicating whether a PCM format can be used by the G.711 adapter.</summary>
    public static bool IsUsablePcmFormat(AudioFormat format)
    {
        return format.SampleFormat == AudioSampleFormat.Pcm16 &&
            format.SampleRate > 0 &&
            format.ChannelCount > 0;
    }

    /// <summary>Gets a value indicating whether an encoded format can be used by the G.711 adapter.</summary>
    public static bool IsUsableEncodedFormat(EncodedAudioFormat format)
    {
        return IsSupportedEncoding(format.Encoding) &&
            format.SampleRate > 0 &&
            format.ChannelCount > 0 &&
            format.RtpClockRate is null or > 0;
    }

    /// <summary>Decodes one G.711 sample to 16-bit PCM.</summary>
    public static short DecodeSample(byte encoded, AudioEncoding encoding) =>
        encoding switch
        {
            AudioEncoding.Pcmu => DecodePcmuSample(encoded),
            AudioEncoding.Pcma => DecodePcmaSample(encoded),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unsupported G.711 encoding.")
        };

    /// <summary>Encodes one 16-bit PCM sample to G.711.</summary>
    public static byte EncodeSample(short sample, AudioEncoding encoding) =>
        encoding switch
        {
            AudioEncoding.Pcmu => EncodePcmuSample(sample),
            AudioEncoding.Pcma => EncodePcmaSample(sample),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unsupported G.711 encoding.")
        };

    /// <summary>Decodes a G.711 payload to little-endian PCM16.</summary>
    public static AudioCodecStatus Decode(ReadOnlySpan<byte> payload, AudioEncoding encoding, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (!IsSupportedEncoding(encoding))
        {
            return AudioCodecStatus.UnsupportedFormat;
        }

        int requiredBytes = checked(payload.Length * 2);
        if (destination.Length < requiredBytes)
        {
            return AudioCodecStatus.DestinationTooSmall;
        }

        for (int i = 0; i < payload.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination.Slice(i * 2, 2), DecodeSample(payload[i], encoding));
        }

        bytesWritten = requiredBytes;
        return AudioCodecStatus.Success;
    }

    /// <summary>Encodes little-endian PCM16 samples to a G.711 payload.</summary>
    public static AudioCodecStatus Encode(ReadOnlySpan<byte> pcm16, AudioEncoding encoding, Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        if (!IsSupportedEncoding(encoding))
        {
            return AudioCodecStatus.UnsupportedFormat;
        }

        if ((pcm16.Length & 1) != 0)
        {
            return AudioCodecStatus.InvalidInput;
        }

        int sampleCount = pcm16.Length / 2;
        if (destination.Length < sampleCount)
        {
            return AudioCodecStatus.DestinationTooSmall;
        }

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(pcm16.Slice(i * 2, 2));
            destination[i] = EncodeSample(sample, encoding);
        }

        bytesWritten = sampleCount;
        return AudioCodecStatus.Success;
    }

    private static short DecodePcmuSample(byte encoded)
    {
        int value = (byte)~encoded;
        int magnitude = ((value & 0x0F) << 3) + MuLawBias;
        magnitude <<= (value & 0x70) >> 4;
        return (short)((value & 0x80) != 0 ? MuLawBias - magnitude : magnitude - MuLawBias);
    }

    private static byte EncodePcmuSample(short sample)
    {
        int pcm = sample;
        int sign = (pcm >> 8) & 0x80;
        if (sign != 0)
        {
            pcm = -pcm;
        }

        if (pcm > MuLawClip)
        {
            pcm = MuLawClip;
        }

        pcm += MuLawBias;
        int segment = SearchSegment(pcm, MuLawSegmentEnd);
        int encoded = (segment << 4) | ((pcm >> (segment + 3)) & 0x0F);
        return (byte)~(sign | encoded);
    }

    private static short DecodePcmaSample(byte encoded)
    {
        int value = encoded ^ 0x55;
        int magnitude = (value & 0x0F) << 4;
        int segment = (value & 0x70) >> 4;

        magnitude = segment switch
        {
            0 => magnitude + 8,
            1 => magnitude + 0x108,
            _ => (magnitude + 0x108) << (segment - 1)
        };

        return (short)((value & 0x80) != 0 ? magnitude : -magnitude);
    }

    private static byte EncodePcmaSample(short sample)
    {
        int pcm = sample >> 3;
        int mask;
        if (pcm >= 0)
        {
            mask = 0xD5;
        }
        else
        {
            mask = 0x55;
            pcm = -pcm - 1;
        }

        int segment = SearchSegment(pcm, ALawSegmentEnd);
        if (segment >= 8)
        {
            return (byte)(0x7F ^ mask);
        }

        int encoded = segment << 4;
        encoded |= segment < 2
            ? (pcm >> 1) & 0x0F
            : (pcm >> segment) & 0x0F;

        return (byte)(encoded ^ mask);
    }

    private static int SearchSegment(int value, ReadOnlySpan<short> ends)
    {
        for (int i = 0; i < ends.Length; i++)
        {
            if (value <= ends[i])
            {
                return i;
            }
        }

        return ends.Length - 1;
    }
}
