// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Batch-backed recognizer over <see cref="ISpeechToTextClient.GetTextAsync"/>.
/// </summary>
public sealed class MeaiBatchSpeechRecognizer : ISpeechRecognizer
{
    private readonly ISpeechToTextClient _client;
    private readonly bool _disposeClient;

    /// <summary>
    /// Creates a batch recognizer over a Microsoft.Extensions.AI speech-to-text client.
    /// </summary>
    public MeaiBatchSpeechRecognizer(
        ISpeechToTextClient client,
        string? provider = null,
        string? model = null,
        bool disposeClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Provider = provider;
        Model = model;
        _disposeClient = disposeClient;
    }

    /// <summary>Provider name used for emitted recognition context.</summary>
    public string? Provider { get; }

    /// <summary>Model name used for emitted recognition context.</summary>
    public string? Model { get; }

    /// <inheritdoc />
    public SpeechRecognitionCapabilities Capabilities { get; } = new()
    {
        StreamingInput = false,
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

        await using var buffer = new MemoryStream();
        AudioInputFrame? firstFrame = null;
        AudioInputFrame? lastFrame = null;
        var started = false;

        await foreach (var frame in audio.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (frame.Audio.IsEmpty && !frame.IsFinal)
                continue;

            firstFrame ??= frame;
            lastFrame = frame;

            if (!started && !frame.Audio.IsEmpty)
            {
                started = true;
                yield return new SpeechRecognitionStartedEvent
                {
                    Context = CreateContext(options, recognitionId, utteranceId, frame)
                };
            }

            if (!frame.Audio.IsEmpty)
                await buffer.WriteAsync(frame.Audio, cancellationToken).ConfigureAwait(false);
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
        var finalFrame = lastFrame ?? firstFrame;
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

        if (started && finalFrame.HasValue)
        {
            yield return new SpeechRecognitionEndedEvent
            {
                Context = CreateContext(options, recognitionId, utteranceId, finalFrame.Value),
                SpeechDuration = CalculateDuration(firstFrame, finalFrame)
            };
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposeClient)
            _client.Dispose();

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

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

    private static TimeSpan? CalculateDuration(AudioInputFrame? firstFrame, AudioInputFrame? lastFrame)
    {
        if (!firstFrame.HasValue || !lastFrame.HasValue)
            return null;

        var start = firstFrame.Value.TimestampNs;
        var end = lastFrame.Value.TimestampNs;
        if (start <= 0 || end <= start)
            return null;

        return TimeSpan.FromTicks((end - start) / 100);
    }
}
