// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Recognition;

/// <summary>
/// Streaming-shaped recognizer over <see cref="ISpeechToTextClient.GetStreamingTextAsync"/>.
/// </summary>
/// <remarks>
/// This adapter does not infer realtime capability from the MEAI streaming method shape.
/// Pass truthful capabilities for the concrete provider path being wrapped.
/// </remarks>
public sealed class MeaiStreamingSpeechRecognizer : ISpeechRecognizer
{
    private readonly ISpeechToTextClient _client;
    private readonly bool _disposeClient;

    /// <summary>Creates a streaming-shaped recognizer over a Microsoft.Extensions.AI speech-to-text client.</summary>
    public MeaiStreamingSpeechRecognizer(
        ISpeechToTextClient client,
        SpeechRecognitionCapabilities? capabilities = null,
        string? provider = null,
        string? model = null,
        bool disposeClient = false)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Capabilities = capabilities ?? new SpeechRecognitionCapabilities
        {
            StreamingInput = false,
            InterimResults = false,
            PreflightResults = false,
            FinalResults = true,
            OfflineRecognize = true
        };
        Provider = provider;
        Model = model;
        _disposeClient = disposeClient;
    }

    /// <summary>Provider name used for emitted recognition context.</summary>
    public string? Provider { get; }

    /// <summary>Model name used for emitted recognition context.</summary>
    public string? Model { get; }

    /// <inheritdoc />
    public SpeechRecognitionCapabilities Capabilities { get; }

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

        if (buffer.Length == 0 || firstFrame is null)
            yield break;

        buffer.Position = 0;
        var revision = 0;
        var emittedEnded = false;
        var fallbackFrame = lastFrame ?? firstFrame.Value;

        await foreach (var update in _client
            .GetStreamingTextAsync(buffer, ToSpeechToTextOptions(options), cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            var frame = lastFrame ?? firstFrame.Value;
            var context = CreateContext(
                options,
                recognitionId,
                utteranceId,
                frame,
                providerRequestId: update.ResponseId,
                providerModel: update.ModelId);

            if (update.Kind == SpeechToTextResponseUpdateKind.Error)
            {
                yield return new SpeechRecognitionErrorEvent
                {
                    Context = context,
                    Error = string.IsNullOrWhiteSpace(update.Text)
                        ? "Speech recognition streaming update reported an error."
                        : update.Text,
                    ErrorCode = "meai_streaming_error"
                };
                continue;
            }

            if (update.Kind == SpeechToTextResponseUpdateKind.SessionClose)
            {
                emittedEnded = true;
                yield return new SpeechRecognitionEndedEvent
                {
                    Context = context,
                    SpeechDuration = CalculateDuration(firstFrame, lastFrame)
                };
                continue;
            }

            if (string.IsNullOrWhiteSpace(update.Text))
                continue;

            var transcript = new SpeechRecognitionTranscript(
                Text: update.Text,
                Language: options.Language,
                StartTime: update.StartTime,
                EndTime: update.EndTime,
                TranscriptRevisionId: $"{recognitionId}:{++revision}");

            if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdated)
            {
                yield return new SpeechRecognitionFinalEvent
                {
                    Context = context,
                    Transcript = transcript
                };
            }
            else if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdating && Capabilities.PreflightResults)
            {
                yield return new SpeechRecognitionPreflightEvent
                {
                    Context = context,
                    Transcript = transcript
                };
            }
            else if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdating && Capabilities.InterimResults)
            {
                yield return new SpeechRecognitionInterimEvent
                {
                    Context = context,
                    Transcript = transcript
                };
            }
        }

        if (started && !emittedEnded)
        {
            yield return new SpeechRecognitionEndedEvent
            {
                Context = CreateContext(options, recognitionId, utteranceId, fallbackFrame),
                SpeechDuration = CalculateDuration(firstFrame, lastFrame)
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
        AudioInputFrame frame,
        string? providerRequestId = null,
        string? providerModel = null) =>
        new(
            RuntimeId: options.RuntimeId,
            SessionId: options.SessionId ?? frame.SessionId,
            BranchId: options.BranchId ?? frame.BranchId,
            UtteranceId: utteranceId,
            RecognitionId: recognitionId,
            SegmentId: null,
            ProviderRequestId: providerRequestId,
            Provider: options.Provider ?? Provider,
            Model: options.Model ?? providerModel ?? Model,
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
