// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Buffers.Binary;
using System.Security.Cryptography;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HPD.Agent.Providers.Audio.Silero;

internal sealed class SileroModelHostV1 : IDisposable
{
    private readonly InferenceSession _session;
    private readonly object _gate = new();
    private readonly object _inferenceGate = new();
    private int _leases;
    private bool _disposeRequested;

    internal SileroModelHostV1(string modelPath, string expectedSha256, int intraOpThreads)
    {
        var fullPath = Path.GetFullPath(modelPath);
        using (var stream = File.OpenRead(fullPath))
        {
            if (StringComparer.Ordinal.Equals(expectedSha256, SileroModelArtifactV1.OfficialSha256) &&
                stream.Length != SileroModelArtifactV1.OfficialLength)
                throw new InvalidDataException(
                    $"Silero model length mismatch; expected {SileroModelArtifactV1.OfficialLength}, found {stream.Length}.");
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!StringComparer.Ordinal.Equals(expectedSha256, actual))
                throw new InvalidDataException($"Silero model digest mismatch; expected {expectedSha256}, found {actual}.");
        }
        var sessionOptions = new SessionOptions
        {
            IntraOpNumThreads = intraOpThreads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(fullPath, sessionOptions);
        ValidateContract(_session);
        Warmup(_session, 8_000, 256, 32);
        Warmup(_session, 16_000, 512, 64);
    }

    internal SileroVoiceActivitySourceV1 CreateSource()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeRequested, this);
            _leases++;
            return new SileroVoiceActivitySourceV1(_session, _inferenceGate, Release);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposeRequested) return;
            _disposeRequested = true;
            if (_leases == 0) _session.Dispose();
        }
    }

    private static void ValidateContract(InferenceSession session)
    {
        if (!session.InputMetadata.ContainsKey("input") || !session.InputMetadata.ContainsKey("state") ||
            !session.InputMetadata.ContainsKey("sr") || !session.OutputMetadata.ContainsKey("output") ||
            !session.OutputMetadata.ContainsKey("stateN"))
            throw new InvalidDataException("The ONNX artifact is not the supported streaming Silero contract.");
    }

    private static void Warmup(InferenceSession session, int sampleRate, int sampleCount, int contextCount)
    {
        var input = NamedOnnxValue.CreateFromTensor("input",
            new DenseTensor<float>(new float[sampleCount + contextCount], [1, sampleCount + contextCount]));
        var state = NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(new float[256], [2, 1, 128]));
        var rate = NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { sampleRate }, [1]));
        using var results = session.Run([input, state, rate]);
        _ = results.First(static value => value.Name == "output").AsTensor<float>().First();
        _ = results.First(static value => value.Name == "stateN").AsTensor<float>().First();
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_leases <= 0) throw new InvalidOperationException("The Silero model lease count is invalid.");
            _leases--;
            if (_disposeRequested && _leases == 0) _session.Dispose();
        }
    }
}

internal sealed class SileroVoiceActivitySourceV1 : IBorrowedSynchronousVoiceActivitySourceV1, IDisposable
{
    private static readonly VoiceActivityMeasurementDescriptorV1 Measurement = new(
        VoiceActivityMeasurementKindV1.EngineScore, new BoundedAscii("silero-score"), 0, 1, null);
    private static readonly VoiceActivitySourceCapabilitiesV1 DeclaredCapabilities = new(
        VoiceActivityInputOwnershipV1.BorrowedSynchronous,
        [new(VoiceActivitySampleEncodingV1.SignedPcm16, 8_000, 1),
         new(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new(TimeSpan.FromMilliseconds(32), TimeSpan.FromMilliseconds(32), TimeSpan.FromMilliseconds(32), 1),
        Measurement, VoiceActivitySourceStateModelV1.StreamLocal, VoiceActivitySourceConcurrencyV1.Serial,
        VoiceActivitySourceControlV1.ReplacementRequired, VoiceActivitySourceControlV1.ReplacementRequired,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
        false, true, 1);

    private readonly InferenceSession _session;
    private readonly object _inferenceGate;
    private Action? _release;
    private readonly object _gate = new();
    private float[] _state = new float[256];
    private float[] _context = [];
    private int _lastSampleRate;
    private ulong _sequence;
    private bool _disposed;

    internal SileroVoiceActivitySourceV1(InferenceSession session, object inferenceGate, Action release)
    {
        _session = session;
        _inferenceGate = inferenceGate;
        _release = release;
    }

    public VoiceActivitySourceCapabilitiesV1 Capabilities => DeclaredCapabilities;

    public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window)
    {
        lock (_gate)
        {
            if (_disposed)
                return new VoiceActivitySourceOutcomeV1.Unavailable(
                    VoiceActivitySourceUnavailableReasonV1.ModelUnavailable,
                    VoiceActivityRetryabilityV1.AfterReplacement);
            var sampleRate = window.Format.SampleRate;
            var sampleCount = sampleRate == 16_000 ? 512 : sampleRate == 8_000 ? 256 : 0;
            if (window.Format.Encoding != VoiceActivitySampleEncodingV1.SignedPcm16 ||
                window.Format.Channels != 1 || sampleCount == 0 || window.Bytes.Length != sampleCount * 2)
                return new VoiceActivitySourceOutcomeV1.InvalidInput(VoiceActivityInputInvalidReasonV1.FormatMismatch);
            try
            {
                if (_lastSampleRate != 0 && _lastSampleRate != sampleRate) ResetState();
                var contextCount = sampleRate == 16_000 ? 64 : 32;
                if (_context.Length != contextCount) _context = new float[contextCount];
                var input = new float[contextCount + sampleCount];
                _context.CopyTo(input, 0);
                for (var index = 0; index < sampleCount; index++)
                    input[contextCount + index] = BinaryPrimitives.ReadInt16LittleEndian(window.Bytes.Slice(index * 2, 2)) / 32768f;
                var inputValue = NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, [1, input.Length]));
                var stateValue = NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(_state, [2, 1, 128]));
                var sampleRateValue = NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { sampleRate }, [1]));
                if (!Monitor.TryEnter(_inferenceGate))
                    return new VoiceActivitySourceOutcomeV1.Unavailable(
                        VoiceActivitySourceUnavailableReasonV1.CapacityUnavailable,
                        VoiceActivityRetryabilityV1.SameGeneration);
                float score;
                try
                {
                    using var results = _session.Run([inputValue, stateValue, sampleRateValue]);
                    score = Math.Clamp(results.First(static value => value.Name == "output").AsTensor<float>().First(), 0, 1);
                    _state = results.First(static value => value.Name == "stateN").AsTensor<float>().ToArray();
                }
                finally
                {
                    Monitor.Exit(_inferenceGate);
                }
                Array.Copy(input, input.Length - contextCount, _context, 0, contextCount);
                _lastSampleRate = sampleRate;
                if (_sequence == ulong.MaxValue)
                    return new VoiceActivitySourceOutcomeV1.Fault(VoiceActivitySourceFaultClassV1.ContractViolation,
                        VoiceActivityStateValidityV1.Quarantined, VoiceActivityRetryabilityV1.AfterReplacement);
                _sequence++;
                return new VoiceActivitySourceOutcomeV1.Observed(new VoiceActivityMeasurementV1.Numeric(score),
                    Measurement, window.Extent, _sequence, window.ObservedAt, window.ObservedAt);
            }
            catch (OnnxRuntimeException)
            {
                return new VoiceActivitySourceOutcomeV1.Fault(VoiceActivitySourceFaultClassV1.InferenceFailure,
                    VoiceActivityStateValidityV1.Quarantined, VoiceActivityRetryabilityV1.AfterReplacement);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ResetState();
            var release = _release;
            _release = null;
            release?.Invoke();
        }
    }

    private void ResetState()
    {
        Array.Clear(_state);
        Array.Clear(_context);
        _lastSampleRate = 0;
    }
}
