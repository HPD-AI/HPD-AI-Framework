using System.Collections.ObjectModel;
using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.VoiceActivity;

internal abstract record VoiceActivityWindowAssemblyResultV1
{
    private VoiceActivityWindowAssemblyResultV1() { }

    internal sealed record Produced : VoiceActivityWindowAssemblyResultV1
    {
        private readonly VoiceActivityAssembledWindowV1[] _windows;

        internal Produced(IReadOnlyList<VoiceActivityAssembledWindowV1> windows)
        {
            ArgumentNullException.ThrowIfNull(windows);
            _windows = windows.ToArray();
            Windows = new ReadOnlyCollection<VoiceActivityAssembledWindowV1>(_windows);
        }

        internal IReadOnlyList<VoiceActivityAssembledWindowV1> Windows { get; }
    }

    internal sealed record Rejected(VoiceActivityInputInvalidReasonV1 Reason) :
        VoiceActivityWindowAssemblyResultV1;
}

internal sealed record VoiceActivityAssembledWindowV1
{
    private readonly byte[] _bytes;

    internal VoiceActivityAssembledWindowV1(
        byte[] bytes,
        VoiceActivityInputFormatV1 format,
        VoiceActivityMediaExtentV1 extent)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(format);
        _bytes = bytes.ToArray();
        Format = format;
        Extent = extent;
    }

    internal ReadOnlyMemory<byte> Bytes => _bytes;
    internal VoiceActivityInputFormatV1 Format { get; }
    internal VoiceActivityMediaExtentV1 Extent { get; }
}

internal sealed class VoiceActivityPcm16WindowAssemblerV1
{
    private readonly AudioFormat _inputFormat;
    private readonly VoiceActivityInputFormatV1 _outputFormat;
    private readonly int _windowSamples;
    private readonly int _maximumBatchSize;
    private readonly List<short> _pending = [];
    private readonly LinkedList<ExtentSegment> _segments = [];
    private long _resampleWeight;
    private long _weightedSampleSum;
    private long? _unemittedExtentStart;
    private bool _unemittedExact = true;
    private SessionAuthorityStampV1? _session;
    private GraphGenerationId? _graphGeneration;
    private ulong? _nextPosition;

    internal VoiceActivityPcm16WindowAssemblerV1(
        AudioFormat inputFormat,
        VoiceActivityInputFormatV1 outputFormat,
        TimeSpan window,
        int maximumBatchSize)
    {
        ArgumentNullException.ThrowIfNull(outputFormat);
        if (inputFormat.SampleFormat != AudioSampleFormat.Pcm16 ||
            inputFormat.SampleRate is < 8_000 or > 192_000 ||
            inputFormat.ChannelCount is < 1 or > 8)
            throw new ArgumentException("The input must be bounded interleaved PCM16.", nameof(inputFormat));
        if (outputFormat.Encoding != VoiceActivitySampleEncodingV1.SignedPcm16 || outputFormat.Channels != 1)
            throw new ArgumentException("The assembled output must be mono PCM16.", nameof(outputFormat));
        if (window <= TimeSpan.Zero || window > TimeSpan.FromMinutes(1) ||
            (long)outputFormat.SampleRate * window.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new ArgumentException("The window must contain an integral number of output samples.", nameof(window));
        var samples = (long)outputFormat.SampleRate * window.Ticks / TimeSpan.TicksPerSecond;
        if (samples is < 1 or > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(window));
        if (maximumBatchSize is < 1 or > 4_096) throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));
        _inputFormat = inputFormat;
        _outputFormat = outputFormat;
        _windowSamples = (int)samples;
        _maximumBatchSize = maximumBatchSize;
    }

    internal int RetainedSamples => _pending.Count;

    internal VoiceActivityWindowAssemblyResultV1 Process(
        ReadOnlySpan<byte> bytes,
        AudioFormat inputFormat,
        int samplesPerChannel,
        AudioRecoveryKind recovery,
        AudioFrameFlags flags,
        GraphMediaRangeV1 range)
    {
        if ((flags & AudioFrameFlags.Discontinuity) != 0)
        {
            Reset();
            return Reject(VoiceActivityInputInvalidReasonV1.DiscontinuousWindow);
        }
        if (!SameFormat(inputFormat, _inputFormat) || samplesPerChannel <= 0 ||
            bytes.Length != (long)samplesPerChannel * inputFormat.ChannelCount * sizeof(short))
            return RejectAndReset(VoiceActivityInputInvalidReasonV1.FormatMismatch);
        if (!range.IsValid || range.Direction != GraphDirectionV1.IngressForward ||
            range.Domain != GraphTrafficDomainV1.Media || range.EncodedBytes != (ulong)bytes.Length ||
            range.Start.Value > long.MaxValue || range.EndExclusive.Value > long.MaxValue)
            return RejectAndReset(VoiceActivityInputInvalidReasonV1.ExtentInvalid);
        if (_session is { } session &&
            (session != range.Session || _graphGeneration != range.GraphGeneration || _nextPosition != range.Start.Value))
        {
            Reset();
            return Reject(VoiceActivityInputInvalidReasonV1.MixedGeneration);
        }

        var outputSamples = (_resampleWeight + (long)samplesPerChannel * _outputFormat.SampleRate) /
            _inputFormat.SampleRate;
        var outputWindows = (_pending.Count + outputSamples) / _windowSamples;
        if (outputWindows > _maximumBatchSize)
            return RejectAndReset(VoiceActivityInputInvalidReasonV1.ExtentInvalid);

        _session ??= range.Session;
        _graphGeneration ??= range.GraphGeneration;
        _nextPosition = range.EndExclusive.Value;
        var exact = recovery == AudioRecoveryKind.None && (flags & AudioFrameFlags.ClockAdjusted) == 0;
        var contributionStart = _unemittedExtentStart ?? checked((long)range.Start.Value);
        var contributionExact = _unemittedExact && exact;
        var emittedForRange = 0;
        for (var sample = 0; sample < samplesPerChannel; sample++)
        {
            var mixed = Downmix(bytes, sample, inputFormat.ChannelCount);
            var remainingWeight = (long)_outputFormat.SampleRate;
            while (remainingWeight > 0)
            {
                var acceptedWeight = Math.Min(remainingWeight, _inputFormat.SampleRate - _resampleWeight);
                _weightedSampleSum += mixed * acceptedWeight;
                _resampleWeight += acceptedWeight;
                remainingWeight -= acceptedWeight;
                if (_resampleWeight != _inputFormat.SampleRate) continue;
                _pending.Add(RoundPcm16(_weightedSampleSum, _inputFormat.SampleRate));
                emittedForRange++;
                _resampleWeight = 0;
                _weightedSampleSum = 0;
            }
        }
        if (emittedForRange > 0)
        {
            _segments.AddLast(new ExtentSegment(1, contributionStart,
                checked((long)range.EndExclusive.Value), contributionExact));
            if (emittedForRange > 1)
                _segments.AddLast(new ExtentSegment(emittedForRange - 1, checked((long)range.Start.Value),
                    checked((long)range.EndExclusive.Value), exact));
        }
        if (_resampleWeight > 0)
        {
            _unemittedExtentStart = emittedForRange == 0
                ? contributionStart : checked((long)range.Start.Value);
            _unemittedExact = emittedForRange == 0 ? contributionExact : exact;
        }
        else
        {
            _unemittedExtentStart = null;
            _unemittedExact = true;
        }

        var windows = new List<VoiceActivityAssembledWindowV1>((int)outputWindows);
        var consumedSamples = 0;
        while (_pending.Count - consumedSamples >= _windowSamples)
        {
            var output = new byte[_windowSamples * sizeof(short)];
            for (var index = 0; index < _windowSamples; index++)
            {
                var value = _pending[consumedSamples + index];
                output[index * 2] = (byte)value;
                output[index * 2 + 1] = (byte)(value >> 8);
            }
            consumedSamples += _windowSamples;
            windows.Add(new VoiceActivityAssembledWindowV1(output, _outputFormat, ConsumeExtent(_windowSamples)));
        }
        if (consumedSamples > 0) _pending.RemoveRange(0, consumedSamples);
        return new VoiceActivityWindowAssemblyResultV1.Produced(windows);
    }

    internal void Reset()
    {
        _pending.Clear();
        _segments.Clear();
        _resampleWeight = 0;
        _weightedSampleSum = 0;
        _unemittedExtentStart = null;
        _unemittedExact = true;
        _session = null;
        _graphGeneration = null;
        _nextPosition = null;
    }

    private VoiceActivityMediaExtentV1 ConsumeExtent(int samples)
    {
        var remaining = samples;
        var start = 0L;
        var end = 0L;
        var exact = true;
        var first = true;
        while (remaining > 0)
        {
            var segment = _segments.First!.Value;
            _segments.RemoveFirst();
            if (first)
            {
                start = segment.Start;
                first = false;
            }
            end = segment.End;
            exact &= segment.Exact;
            var consumed = Math.Min(remaining, segment.Samples);
            remaining -= consumed;
            if (consumed < segment.Samples)
                _segments.AddFirst(segment with { Samples = segment.Samples - consumed });
        }
        return new VoiceActivityMediaExtentV1(_graphGeneration!.Value, start, end, exact);
    }

    private static short Downmix(ReadOnlySpan<byte> bytes, int sample, int channels)
    {
        var sum = 0;
        var offset = sample * channels * sizeof(short);
        for (var channel = 0; channel < channels; channel++)
        {
            var index = offset + channel * sizeof(short);
            sum += (short)(bytes[index] | bytes[index + 1] << 8);
        }
        return (short)(sum / channels);
    }

    private static short RoundPcm16(long weightedSum, int weight)
    {
        var rounded = weightedSum >= 0
            ? (weightedSum + weight / 2) / weight
            : (weightedSum - weight / 2) / weight;
        return (short)Math.Clamp(rounded, short.MinValue, short.MaxValue);
    }

    private static bool SameFormat(AudioFormat left, AudioFormat right) =>
        left.SampleRate == right.SampleRate && left.ChannelCount == right.ChannelCount &&
        left.SampleFormat == right.SampleFormat;

    private static VoiceActivityWindowAssemblyResultV1.Rejected Reject(VoiceActivityInputInvalidReasonV1 reason) =>
        new(reason);

    private VoiceActivityWindowAssemblyResultV1.Rejected RejectAndReset(VoiceActivityInputInvalidReasonV1 reason)
    {
        Reset();
        return Reject(reason);
    }

    private sealed record ExtentSegment(int Samples, long Start, long End, bool Exact);
}
