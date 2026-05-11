// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// VAD-backed batch recognizer: speech boundaries are live, transcripts are final only.
/// </summary>
public sealed class BatchSttWithVadRecognizer : ISpeechRecognizer
{
    private readonly ISpeechToTextClient _client;
    private readonly IVoiceActivityDetector _vad;
    private readonly bool _disposeClient;
    private readonly bool _disposeVad;

    /// <summary>
    /// Creates a recognizer that emits VAD boundaries live and final transcript after batch STT.
    /// </summary>
    public BatchSttWithVadRecognizer(
        ISpeechToTextClient client,
        IVoiceActivityDetector vad,
        string? provider = null,
        string? model = null,
        bool disposeClient = false,
        bool disposeVad = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _vad = vad ?? throw new ArgumentNullException(nameof(vad));
        Provider = provider;
        Model = model;
        _disposeClient = disposeClient;
        _disposeVad = disposeVad;
    }

    /// <summary>Provider name used for emitted recognition context.</summary>
    public string? Provider { get; }

    /// <summary>Model name used for emitted recognition context.</summary>
    public string? Model { get; }

    /// <inheritdoc />
    public SpeechRecognitionCapabilities Capabilities { get; } = new()
    {
        StreamingInput = true,
        InterimResults = false,
        PreflightResults = false,
        FinalResults = true,
        OfflineRecognize = true
    };

    /// <inheritdoc />
    public async IAsyncEnumerable<SpeechRecognitionEvent> RecognizeAsync(
        IAsyncEnumerable<AudioInputFrame> audio,
        SpeechRecognitionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(options);

        var recognitionId = string.IsNullOrWhiteSpace(options.RecognitionId)
            ? Guid.NewGuid().ToString("N")
            : options.RecognitionId;
        var utteranceId = string.IsNullOrWhiteSpace(options.UtteranceId)
            ? Guid.NewGuid().ToString("N")
            : options.UtteranceId;

        _vad.Reset();

        await using var buffer = new MemoryStream();
        AudioInputFrame? firstFrame = null;
        AudioInputFrame? lastFrame = null;
        AudioInputFrame? speechStartedFrame = null;
        AudioInputFrame? speechEndedFrame = null;
        var isSpeaking = false;

        await foreach (var frame in audio.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (frame.Audio.IsEmpty && !frame.IsFinal)
                continue;

            firstFrame ??= frame;
            lastFrame = frame;

            if (!frame.Audio.IsEmpty)
            {
                await buffer.WriteAsync(frame.Audio, cancellationToken).ConfigureAwait(false);

                var vadResult = _vad.Process(ToAudioFrame(frame, options));
                if (!isSpeaking &&
                    (vadResult.State == VadState.Starting || vadResult.State == VadState.Speaking))
                {
                    isSpeaking = true;
                    speechStartedFrame = frame;
                    yield return new SpeechRecognitionStartedEvent
                    {
                        Context = CreateContext(options, recognitionId, utteranceId, frame),
                        SpeechProbability = vadResult.SpeechProbability
                    };
                }

                if (isSpeaking &&
                    (vadResult.State == VadState.Stopping || (!vadResult.IsSpeaking && vadResult.State == VadState.Quiet)))
                {
                    isSpeaking = false;
                    speechEndedFrame = frame;
                    yield return new SpeechRecognitionEndedEvent
                    {
                        Context = CreateContext(options, recognitionId, utteranceId, frame),
                        SpeechDuration = CalculateDuration(speechStartedFrame, speechEndedFrame)
                    };
                }
            }
        }

        if (buffer.Length == 0)
            yield break;

        buffer.Position = 0;
        var response = await _client.GetTextAsync(
                buffer,
                ToSpeechToTextOptions(options),
                cancellationToken)
            .ConfigureAwait(false);

        var transcript = response.Text;
        var finalFrame = speechEndedFrame ?? lastFrame ?? firstFrame;
        if (!string.IsNullOrWhiteSpace(transcript) && finalFrame.HasValue)
        {
            yield return new SpeechRecognitionFinalEvent
            {
                Context = CreateContext(options, recognitionId, utteranceId, finalFrame.Value),
                Transcript = new SpeechRecognitionTranscript(
                    Text: transcript,
                    Language: options.Language,
                    TranscriptRevisionId: recognitionId)
            };
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposeClient)
            _client.Dispose();

        if (_disposeVad)
            _vad.Dispose();

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private static AudioFrame ToAudioFrame(AudioInputFrame frame, SpeechRecognitionOptions options) =>
        new()
        {
            Data = frame.Audio,
            SampleRate = options.SampleRate ?? 16000,
            Channels = options.Channels ?? 1,
            Timestamp = TimeSpan.FromTicks(frame.TimestampNs / 100),
            Duration = TimeSpan.Zero
        };

    private SpeechRecognitionContext CreateContext(
        SpeechRecognitionOptions options,
        string recognitionId,
        string utteranceId,
        AudioInputFrame frame) =>
        new(
            RuntimeId: options.RuntimeId,
            SessionId: options.SessionId ?? frame.SessionId,
            BranchId: options.BranchId ?? frame.BranchId,
            UtteranceId: utteranceId,
            RecognitionId: recognitionId,
            SegmentId: null,
            ProviderRequestId: null,
            Provider: options.Provider ?? Provider,
            Model: options.Model ?? Model,
            SequenceNumber: frame.SequenceNumber == 0 ? null : frame.SequenceNumber,
            TimestampNs: frame.TimestampNs == 0 ? null : frame.TimestampNs,
            ObservedAt: DateTimeOffset.UtcNow);

    private static SpeechToTextOptions ToSpeechToTextOptions(SpeechRecognitionOptions options) =>
        new()
        {
            ModelId = options.Model,
            SpeechLanguage = options.Language,
            SpeechSampleRate = options.SampleRate
        };

    private static TimeSpan? CalculateDuration(AudioInputFrame? startedFrame, AudioInputFrame? endedFrame)
    {
        if (!startedFrame.HasValue || !endedFrame.HasValue)
            return null;

        var start = startedFrame.Value.TimestampNs;
        var end = endedFrame.Value.TimestampNs;
        if (start < 0 || end < start)
            return null;

        return TimeSpan.FromTicks((end - start) / 100);
    }
}
