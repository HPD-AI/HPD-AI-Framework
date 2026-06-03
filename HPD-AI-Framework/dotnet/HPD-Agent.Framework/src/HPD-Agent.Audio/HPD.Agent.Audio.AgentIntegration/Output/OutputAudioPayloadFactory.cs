using HPD.Agent.Audio.Output;
using RealtimeAudioFormat = HPD.Audio.Primitives.AudioFormat;
using RealtimeAudioFrame = HPD.Audio.Primitives.AudioFrame;
using RealtimeAudioSampleFormat = HPD.Audio.Primitives.AudioSampleFormat;

namespace HPD.Agent.Audio.AgentIntegration.Output;

internal static class OutputAudioPayloadFactory
{
    private const int DefaultPcmSampleRate = 16000;
    private const int DefaultPcmChannelCount = 1;
    private const int BytesPerPcm16Sample = sizeof(short);

    public static OutputAudioPayload Create(
        ReadOnlyMemory<byte> data,
        string mediaType,
        string? outputFormat,
        long? sequenceNumber = null,
        DateTimeOffset? observedAt = null)
    {
        if (TryCreatePcmFrame(data, mediaType, outputFormat, sequenceNumber, observedAt, out var frame))
        {
            return new DecodedOutputAudioFrame
            {
                Frame = frame
            };
        }

        return new EncodedOutputAudioData
        {
            ContentType = mediaType,
            Data = data
        };
    }

    public static OutputAudioPayloadKind ResolveKind(
        ReadOnlyMemory<byte> data,
        string mediaType,
        string? outputFormat) =>
        TryCreatePcmFrame(data, mediaType, outputFormat, null, null, out _)
            ? OutputAudioPayloadKind.DecodedPcmFrame
            : OutputAudioPayloadKind.EncodedBytes;

    private static bool TryCreatePcmFrame(
        ReadOnlyMemory<byte> data,
        string mediaType,
        string? outputFormat,
        long? sequenceNumber,
        DateTimeOffset? observedAt,
        out RealtimeAudioFrame frame)
    {
        frame = default;
        if (data.IsEmpty || !LooksLikePcm(mediaType, outputFormat))
        {
            return false;
        }

        var channelCount = DefaultPcmChannelCount;
        var bytesPerFrameSample = channelCount * BytesPerPcm16Sample;
        if (data.Length % bytesPerFrameSample != 0)
        {
            return false;
        }

        var sampleRate = TryParsePcmSampleRate(outputFormat) ??
            TryParsePcmSampleRate(mediaType) ??
            DefaultPcmSampleRate;
        var samplesPerChannel = data.Length / bytesPerFrameSample;
        frame = new RealtimeAudioFrame
        {
            Data = data,
            Format = new RealtimeAudioFormat
            {
                SampleRate = sampleRate,
                ChannelCount = channelCount,
                SampleFormat = RealtimeAudioSampleFormat.Pcm16
            },
            SamplesPerChannel = samplesPerChannel,
            SequenceNumber = sequenceNumber,
            ObservedAt = observedAt
        };
        return true;
    }

    private static bool LooksLikePcm(string mediaType, string? outputFormat) =>
        IsPcmToken(mediaType) || IsPcmToken(outputFormat);

    private static bool IsPcmToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.StartsWith("audio/pcm", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("pcm", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("pcm_", StringComparison.OrdinalIgnoreCase));

    private static int? TryParsePcmSampleRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var marker = value.IndexOf("pcm_", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + "pcm_".Length;
        var end = start;
        while (end < value.Length && char.IsDigit(value[end]))
        {
            end++;
        }

        return end > start &&
            int.TryParse(value.AsSpan(start, end - start), out var sampleRate) &&
            sampleRate > 0
            ? sampleRate
            : null;
    }
}
