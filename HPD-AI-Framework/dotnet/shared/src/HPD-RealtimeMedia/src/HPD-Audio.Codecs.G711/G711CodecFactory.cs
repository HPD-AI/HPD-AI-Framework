#nullable enable

using HPD.Audio.Primitives;
using HPD.Media.Diagnostics;

namespace HPD.Audio.Codecs.G711;

/// <summary>
/// Creates managed G.711 PCMU and PCMA codecs without reflection-based activation.
/// </summary>
public sealed class G711CodecFactory : IAudioCodecFactory
{
    private readonly int maxPayloadBytes;
    private readonly int maxPcmBytes;
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;

    /// <summary>Initializes a new instance of the <see cref="G711CodecFactory"/> class.</summary>
    public G711CodecFactory(int maxPayloadBytes = 4096, int maxPcmBytes = 8192)
        : this(default, hasTelemetry: false, maxPayloadBytes, maxPcmBytes)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="G711CodecFactory"/> class with cached telemetry emitters.</summary>
    public G711CodecFactory(
        RealtimeMediaTelemetryEmitters telemetry,
        int maxPayloadBytes = 4096,
        int maxPcmBytes = 8192)
        : this(telemetry, hasTelemetry: true, maxPayloadBytes, maxPcmBytes)
    {
    }

    private G711CodecFactory(
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry,
        int maxPayloadBytes,
        int maxPcmBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPcmBytes);
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        this.maxPayloadBytes = maxPayloadBytes;
        this.maxPcmBytes = maxPcmBytes;
    }

    /// <inheritdoc />
    public bool TryCreateDecoder(in EncodedAudioFormat format, out IAudioDecoder decoder)
    {
        if (G711Codec.IsUsableEncodedFormat(format))
        {
            decoder = hasTelemetry
                ? new G711Decoder(format, telemetry, maxPayloadBytes)
                : new G711Decoder(format, maxPayloadBytes);
            return true;
        }

        decoder = default!;
        return false;
    }

    /// <inheritdoc />
    public bool TryCreateEncoder(in AudioFormat inputFormat, in EncodedAudioFormat outputFormat, out IAudioEncoder encoder)
    {
        if (G711Codec.IsUsablePcmFormat(inputFormat) &&
            G711Codec.IsUsableEncodedFormat(outputFormat) &&
            outputFormat.SampleRate == inputFormat.SampleRate &&
            outputFormat.ChannelCount == inputFormat.ChannelCount)
        {
            encoder = hasTelemetry
                ? new G711Encoder(inputFormat, outputFormat, telemetry, maxPcmBytes)
                : new G711Encoder(inputFormat, outputFormat, maxPcmBytes);
            return true;
        }

        encoder = default!;
        return false;
    }
}
