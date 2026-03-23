// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Vad;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HPD.Agent.AudioProviders.Silero;

/// <summary>
/// Voice activity detector using the Silero VAD ONNX model.
/// Local, offline inference — no network calls.
/// Supports 8 kHz and 16 kHz PCM int16 audio.
/// </summary>
public sealed class SileroVadDetector : IVoiceActivityDetector
{
    // Silero model requires exactly these frame sizes
    private const int FrameSamples16k = 512; // 32ms at 16000 Hz
    private const int FrameSamples8k  = 256; // 32ms at  8000 Hz

    // ONNX model state dimensions (from Silero V5 architecture)
    private const int StateDim = 128;

    private readonly InferenceSession _session;
    private readonly VadConfig _config;
    private readonly SileroVadConfig _sileroConfig;
    private readonly float _activationThreshold;
    private readonly float _deactivationThreshold;
    private readonly float _modelResetInterval;

    // ONNX model state (RNN hidden/cell)
    private float[] _state   = Array.Empty<float>(); // shape [2, 1, 128], initialized in ResetModelState
    private float[] _context = Array.Empty<float>(); // shape [1, context_size], initialized in ResetModelState
    private int _lastSampleRate;
    private DateTime _lastResetTime;

    // VAD state machine
    private VadState _vadState = VadState.Quiet;
    private TimeSpan _speechDuration;
    private TimeSpan _silenceDuration;
    private readonly List<AudioFrame> _preSpeechBuffer = new();

    private bool _disposed;

    internal SileroVadDetector(
        InferenceSession session,
        VadConfig config,
        SileroVadConfig sileroConfig)
    {
        _session = session;
        _config = config;
        _sileroConfig = sileroConfig;
        _activationThreshold = config.ActivationThreshold;
        _deactivationThreshold = sileroConfig.DeactivationThreshold
            ?? Math.Max(0f, _activationThreshold - 0.15f);
        _modelResetInterval = sileroConfig.ModelResetIntervalSeconds;

        ResetModelState();
        _lastResetTime = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public void Reset()
    {
        ResetModelState();
        _vadState = VadState.Quiet;
        _speechDuration = TimeSpan.Zero;
        _silenceDuration = TimeSpan.Zero;
        _preSpeechBuffer.Clear();
    }

    /// <inheritdoc />
    public VadResult Process(AudioFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sr = frame.SampleRate;
        if (sr != 8000 && sr != 16000)
            throw new ArgumentException($"Silero VAD requires 8000 or 16000 Hz audio, got {sr} Hz.");

        var requiredSamples = sr == 16000 ? FrameSamples16k : FrameSamples8k;

        // Convert PCM int16 bytes → float32 (normalized to [-1.0, 1.0])
        var data = frame.Data.Span;
        var sampleCount = data.Length / 2; // 2 bytes per int16

        if (sampleCount != requiredSamples)
            throw new ArgumentException(
                $"Silero VAD requires exactly {requiredSamples} samples at {sr} Hz, got {sampleCount}.");

        var audio = new float[requiredSamples];
        for (int i = 0; i < requiredSamples; i++)
        {
            var sample = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            audio[i] = sample / 32768f;
        }

        // Periodic model state reset (prevents memory growth)
        var now = DateTime.UtcNow;
        if ((now - _lastResetTime).TotalSeconds >= _modelResetInterval)
        {
            ResetModelState();
            _lastResetTime = now;
        }

        // Reset state if sample rate changed
        if (_lastSampleRate != 0 && _lastSampleRate != sr)
            ResetModelState();
        _lastSampleRate = sr;

        var confidence = RunInference(audio, sr);

        // State machine with hysteresis
        var frameDuration = frame.Duration;
        bool isSpeaking;

        switch (_vadState)
        {
            case VadState.Quiet:
            case VadState.Stopping:
                if (confidence >= _activationThreshold)
                {
                    _vadState = VadState.Starting;
                    _speechDuration = frameDuration;
                    _silenceDuration = TimeSpan.Zero;
                }
                else
                {
                    _vadState = VadState.Quiet;
                    _silenceDuration += frameDuration;
                    _speechDuration = TimeSpan.Zero;
                }
                isSpeaking = false;
                break;

            case VadState.Starting:
                if (confidence >= _activationThreshold)
                {
                    _speechDuration += frameDuration;
                    var minSpeech = TimeSpan.FromSeconds(_config.MinSpeechDuration);
                    if (_speechDuration >= minSpeech)
                    {
                        _vadState = VadState.Speaking;
                    }
                    isSpeaking = _vadState == VadState.Speaking;
                }
                else
                {
                    _vadState = VadState.Quiet;
                    _speechDuration = TimeSpan.Zero;
                    isSpeaking = false;
                }
                break;

            case VadState.Speaking:
                if (confidence < _deactivationThreshold)
                {
                    _vadState = VadState.Stopping;
                    _silenceDuration = frameDuration;
                    _speechDuration = TimeSpan.Zero;
                }
                else
                {
                    _speechDuration += frameDuration;
                    _silenceDuration = TimeSpan.Zero;
                }
                isSpeaking = true;
                break;

            default:
                isSpeaking = false;
                break;
        }

        return new VadResult
        {
            State = _vadState,
            SpeechProbability = confidence,
            IsSpeaking = isSpeaking
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<VadEvent> DetectAsync(
        IAsyncEnumerable<AudioFrame> audio,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Reset();
        var streamStart = DateTime.UtcNow;
        var prevState = VadState.Quiet;

        await foreach (var frame in audio.WithCancellation(cancellationToken))
        {
            var result = Process(frame);
            var timestamp = DateTime.UtcNow - streamStart;

            // Always emit InferenceDone for each frame
            yield return new VadEvent
            {
                Type = VadEventType.InferenceDone,
                Timestamp = timestamp,
                SpeechProbability = result.SpeechProbability,
                SpeechDuration = _speechDuration,
                SilenceDuration = _silenceDuration
            };

            // Transition: Quiet/Stopping → Speaking
            if (result.State == VadState.Speaking && prevState != VadState.Speaking)
            {
                // Flush pre-speech buffer into the StartOfSpeech event
                var frames = _preSpeechBuffer.ToList();
                frames.Add(frame);
                _preSpeechBuffer.Clear();

                yield return new VadEvent
                {
                    Type = VadEventType.StartOfSpeech,
                    Timestamp = timestamp,
                    SpeechProbability = result.SpeechProbability,
                    SpeechDuration = _speechDuration,
                    SilenceDuration = _silenceDuration,
                    Frames = frames
                };
            }
            // Transition: Speaking → Quiet
            else if (result.State == VadState.Quiet && prevState == VadState.Stopping)
            {
                var minSilence = TimeSpan.FromSeconds(_config.MinSilenceDuration);
                if (_silenceDuration >= minSilence)
                {
                    yield return new VadEvent
                    {
                        Type = VadEventType.EndOfSpeech,
                        Timestamp = timestamp,
                        SpeechProbability = result.SpeechProbability,
                        SpeechDuration = _speechDuration,
                        SilenceDuration = _silenceDuration
                    };
                }
            }

            // Maintain pre-speech buffer (prefix padding)
            if (result.State == VadState.Quiet)
            {
                _preSpeechBuffer.Add(frame);
                var maxBuffer = TimeSpan.FromSeconds(_config.PrefixPaddingDuration);
                while (_preSpeechBuffer.Count > 0)
                {
                    var bufferedDuration = _preSpeechBuffer.Aggregate(
                        TimeSpan.Zero, (acc, f) => acc + f.Duration);
                    if (bufferedDuration <= maxBuffer) break;
                    _preSpeechBuffer.RemoveAt(0);
                }
            }

            prevState = result.State;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }

    // -------------------------------------------------------------------------
    // ONNX inference
    // -------------------------------------------------------------------------

    private float RunInference(float[] audio, int sampleRate)
    {
        var contextSize = sampleRate == 16000 ? 64 : 32;

        // Ensure context is initialized
        if (_context.Length != contextSize)
            _context = new float[contextSize];

        // Concatenate context + audio → input tensor [1, context + samples]
        var inputData = new float[contextSize + audio.Length];
        _context.CopyTo(inputData, 0);
        audio.CopyTo(inputData, contextSize);

        // Build input tensors
        var inputTensor  = new DenseTensor<float>(inputData, [1, inputData.Length]);
        var stateTensor  = new DenseTensor<float>(_state, [2, 1, StateDim]);
        var srTensor     = new DenseTensor<long>(new long[] { sampleRate }, new int[] { 1 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", srTensor)
        };

        using var results = _session.Run(inputs);

        // Output[0] = confidence [1,1], Output[1] = new state [2,1,128]
        var outputTensor = results[0].AsTensor<float>();
        var stateTensorOut = results[1].AsTensor<float>();

        var confidence = outputTensor[0, 0];

        // Update state
        stateTensorOut.ToArray().CopyTo(_state, 0);

        // Update context (last contextSize samples of the input)
        Array.Copy(inputData, inputData.Length - contextSize, _context, 0, contextSize);

        return confidence;
    }

    private void ResetModelState()
    {
        _state   = new float[2 * 1 * StateDim]; // [2, 1, 128] flattened
        _context = Array.Empty<float>();
        _lastSampleRate = 0;
    }
}
