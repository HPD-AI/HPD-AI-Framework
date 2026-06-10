#nullable enable

using System.Diagnostics;
using HPD.Audio.Primitives;
using HPD.Media.Diagnostics;

namespace HPD.Audio.Codecs.G711;

/// <summary>
/// Managed G.711 decoder using an instance-owned scratch buffer.
/// </summary>
public sealed class G711Decoder : IAudioDecoder, IRealtimeAudioDecoder
{
    private readonly byte[] pcmScratch;
    private readonly RealtimeMediaTelemetryEmitters telemetry;
    private readonly bool hasTelemetry;
    private bool disposed;

    /// <summary>Initializes a new instance of the <see cref="G711Decoder"/> class.</summary>
    public G711Decoder(EncodedAudioFormat inputFormat, int maxPayloadBytes = 4096)
        : this(inputFormat, default, hasTelemetry: false, maxPayloadBytes)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="G711Decoder"/> class with cached telemetry emitters.</summary>
    public G711Decoder(EncodedAudioFormat inputFormat, RealtimeMediaTelemetryEmitters telemetry, int maxPayloadBytes = 4096)
        : this(inputFormat, telemetry, hasTelemetry: true, maxPayloadBytes)
    {
    }

    private G711Decoder(
        EncodedAudioFormat inputFormat,
        RealtimeMediaTelemetryEmitters telemetry,
        bool hasTelemetry,
        int maxPayloadBytes)
    {
        if (!G711Codec.IsSupportedEncoding(inputFormat.Encoding))
        {
            throw new ArgumentException("The input format must be PCMU or PCMA.", nameof(inputFormat));
        }

        if (!G711Codec.IsUsableEncodedFormat(inputFormat))
        {
            throw new ArgumentException("The input format must declare a positive sample rate, channel count, and RTP clock rate when present.", nameof(inputFormat));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);
        InputFormat = inputFormat;
        OutputFormat = new AudioFormat
        {
            SampleRate = inputFormat.SampleRate,
            ChannelCount = inputFormat.ChannelCount,
            SampleFormat = AudioSampleFormat.Pcm16
        };
        this.telemetry = telemetry;
        this.hasTelemetry = hasTelemetry;
        pcmScratch = new byte[checked(maxPayloadBytes * 2)];
    }

    /// <summary>Gets the decoder input format.</summary>
    public EncodedAudioFormat InputFormat { get; }

    /// <inheritdoc />
    public AudioFormat OutputFormat { get; }

    /// <inheritdoc />
    public AudioCodecStatus Decode(in AudioDecodeInput input, IAudioFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (disposed)
        {
            return AudioCodecStatus.Disposed;
        }

        if (!IsInputFormatSupported(input.Format))
        {
            return AudioCodecStatus.UnsupportedFormat;
        }

        if (!IsSupportedDecodeMode(input.Mode))
        {
            return AudioCodecStatus.InvalidInput;
        }

        long started = Stopwatch.GetTimestamp();
        AudioCodecStatus status = G711Codec.Decode(input.Payload.Span, input.Format.Encoding, pcmScratch, out int bytesWritten);
        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        if (bytesWritten == 0)
        {
            return AudioCodecStatus.Success;
        }

        var frame = new AudioFrame
        {
            Data = pcmScratch.AsMemory(0, bytesWritten),
            Format = OutputFormat,
            SamplesPerChannel = bytesWritten / 2 / OutputFormat.ChannelCount,
            RecoveryKind = input.Mode == DecodeMode.ConcealLoss
                ? AudioRecoveryKind.PacketLossConcealment
                : AudioRecoveryKind.None
        };

        if (!sink.TryWrite(frame))
        {
            return AudioCodecStatus.SinkBackpressure;
        }

        EmitTiming(started, input.Payload.Length, bytesWritten);
        return AudioCodecStatus.Success;
    }

    /// <inheritdoc />
    public AudioCodecStatus Decode(in AudioDecodeInputView input, IAudioFrameViewSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (disposed)
        {
            return AudioCodecStatus.Disposed;
        }

        if (!IsInputFormatSupported(input.Format))
        {
            return AudioCodecStatus.UnsupportedFormat;
        }

        if (!IsSupportedDecodeMode(input.Mode))
        {
            return AudioCodecStatus.InvalidInput;
        }

        long started = Stopwatch.GetTimestamp();
        AudioCodecStatus status = G711Codec.Decode(input.Payload, input.Format.Encoding, pcmScratch, out int bytesWritten);
        if (status != AudioCodecStatus.Success)
        {
            return status;
        }

        if (bytesWritten == 0)
        {
            return AudioCodecStatus.Success;
        }

        var frame = new AudioFrameView(
            pcmScratch.AsSpan(0, bytesWritten),
            OutputFormat,
            bytesWritten / 2 / OutputFormat.ChannelCount,
            recoveryKind: input.Mode == DecodeMode.ConcealLoss
                ? AudioRecoveryKind.PacketLossConcealment
                : AudioRecoveryKind.None);

        if (!sink.TryWrite(frame))
        {
            return AudioCodecStatus.SinkBackpressure;
        }

        EmitTiming(started, input.Payload.Length, bytesWritten);
        return AudioCodecStatus.Success;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        disposed = true;
        return ValueTask.CompletedTask;
    }

    private bool IsInputFormatSupported(EncodedAudioFormat format)
    {
        return G711Codec.IsUsableEncodedFormat(format) &&
            format.Encoding == InputFormat.Encoding &&
            format.SampleRate == InputFormat.SampleRate &&
            format.ChannelCount == InputFormat.ChannelCount &&
            (format.RtpClockRate ?? format.SampleRate) == (InputFormat.RtpClockRate ?? InputFormat.SampleRate);
    }

    private static bool IsSupportedDecodeMode(DecodeMode mode)
    {
        return mode is DecodeMode.Primary or DecodeMode.ConcealLoss;
    }

    private void EmitTiming(long started, int inputBytes, int outputBytes)
    {
        if (!hasTelemetry)
        {
            return;
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - started;
        _ = telemetry.CodecTiming.Emit(new CodecTimingSample
        {
            Operation = CodecOperation.Decode,
            Encoding = (int)InputFormat.Encoding,
            ElapsedNanoseconds = elapsedTicks * 1_000_000_000L / Stopwatch.Frequency,
            InputBytes = inputBytes,
            OutputBytes = outputBytes
        });
    }
}
