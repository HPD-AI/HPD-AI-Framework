using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Trace;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Output;

#pragma warning disable MEAI001

internal interface ITextToSpeechSegmentSynthesizer
{
    ValueTask<TextToSpeechSegmentSynthesisResult> SynthesizeAsync(
        IOutputFlow outputFlow,
        TextToSpeechSegmentRequest request,
        TextToSpeechSynthesisContext context,
        CancellationToken cancellationToken = default);
}

internal sealed class TextToSpeechSegmentSynthesizer : ITextToSpeechSegmentSynthesizer
{
    private readonly OutputArtifactWriter _artifactWriter;
    private readonly OutputLedgerTraceWriter _ledgerTraceWriter;

    public TextToSpeechSegmentSynthesizer()
        : this(new OutputArtifactWriter(), new OutputLedgerTraceWriter())
    {
    }

    public TextToSpeechSegmentSynthesizer(
        OutputArtifactWriter artifactWriter,
        OutputLedgerTraceWriter ledgerTraceWriter)
    {
        _artifactWriter = artifactWriter ?? throw new ArgumentNullException(nameof(artifactWriter));
        _ledgerTraceWriter = ledgerTraceWriter ?? throw new ArgumentNullException(nameof(ledgerTraceWriter));
    }

    public async ValueTask<TextToSpeechSegmentSynthesisResult> SynthesizeAsync(
        IOutputFlow outputFlow,
        TextToSpeechSegmentRequest request,
        TextToSpeechSynthesisContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputFlow);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var ledger = new List<RealtimeLedgerRecord>();
        var trace = new List<RealtimeAudioTraceRecord>();
        var providerKey = FirstNonWhiteSpace(request.ProviderKey, ResolveProviderKey(context.Options))!;
        var modelId = request.ModelId ?? context.Options.ModelId;
        var requestWithProvider = request with
        {
            ProviderKey = providerKey,
            ModelId = modelId,
            VoiceId = request.VoiceId ?? context.Options.VoiceId,
            Language = request.Language ?? context.Options.Language,
            OutputFormat = request.OutputFormat ?? context.Options.OutputFormat,
            ContentType = request.ContentType ?? context.Options.ContentType
        };

        _ledgerTraceWriter.AppendTtsRequested(
            ledger,
            trace,
            context.SessionId,
            context.Correlation,
            outputFlow.Id,
            requestWithProvider,
            providerKey);

        var profile = context.Options.TextToSpeechClient.GetService(typeof(TextToSpeechCapabilityProfile)) as TextToSpeechCapabilityProfile
            ?? new TextToSpeechCapabilityProfile();
        if (!profile.SupportsCompletedTextAudioStreaming &&
            !profile.SupportsCompletedTextSynthesis)
        {
            var unsupported = new AudioErrorInfo
            {
                Code = "UnsupportedTextToSpeechCapability",
                Message = "The configured text-to-speech client cannot synthesize completed text segments.",
                Category = "TextToSpeech",
                IsRetryable = false
            };
            _ledgerTraceWriter.AppendTtsResult(
                ledger,
                trace,
                context.SessionId,
                context.Correlation,
                outputFlow.Id,
                requestWithProvider.ResponseId,
                requestWithProvider,
                providerKey,
                modelId,
                context.Options,
                TtsSynthesisDisposition.Unsupported,
                null,
                null,
                null,
                unsupported);
            return FailedResult(
                outputFlow.Id,
                requestWithProvider,
                TtsSynthesisDisposition.Unsupported,
                unsupported,
                ledger,
                trace);
        }

        if (RequiresContentStoreArtifact(context.Options) && context.Options.ContentStore is null)
        {
            var missingStore = new AudioErrorInfo
            {
                Code = "MissingContentStore",
                Message = "Assistant TTS output synthesis requires IContentStore; no content store is configured.",
                Category = "TextToSpeech",
                IsRetryable = false
            };
            _ledgerTraceWriter.AppendTtsResult(
                ledger,
                trace,
                context.SessionId,
                context.Correlation,
                outputFlow.Id,
                requestWithProvider.ResponseId,
                requestWithProvider,
                providerKey,
                modelId,
                context.Options,
                TtsSynthesisDisposition.Failed,
                null,
                null,
                null,
                missingStore);
            return FailedResult(
                outputFlow.Id,
                requestWithProvider,
                TtsSynthesisDisposition.Failed,
                missingStore,
                ledger,
                trace);
        }

        var ttsOptions = new TextToSpeechOptions
        {
            ModelId = context.Options.ModelId,
            VoiceId = context.Options.VoiceId,
            Language = context.Options.Language,
            AudioFormat = context.Options.OutputFormat,
            Speed = context.Options.Speed
        };

        try
        {
            if (profile.SupportsCompletedTextAudioStreaming)
            {
                return await SynthesizeStreamingAudioAsync(
                    outputFlow,
                    requestWithProvider,
                    context,
                    providerKey,
                    modelId,
                    ttsOptions,
                    ledger,
                    trace,
                    cancellationToken).ConfigureAwait(false);
            }

            var audioResponse = await GetCompletedAudioDataAsync(
                    context.Options.TextToSpeechClient,
                    requestWithProvider.Text,
                    ttsOptions,
                    cancellationToken).ConfigureAwait(false);

            modelId = FirstNonWhiteSpace(audioResponse.ModelId, modelId);
            var mediaType = FirstNonWhiteSpace(
                    audioResponse.MediaType,
                    requestWithProvider.ContentType,
                    OutputArtifactWriter.ToMediaType(requestWithProvider.OutputFormat))
                ?? "application/octet-stream";
            var segmentId = requestWithProvider.SegmentId ??
                new OutputSegmentId($"{outputFlow.Id.Value}:audio-{requestWithProvider.SegmentIndex + 1:D4}");
            var observedAt = DateTimeOffset.UtcNow;
            var payload = OutputAudioPayloadFactory.Create(
                audioResponse.Data,
                mediaType,
                requestWithProvider.OutputFormat,
                sequenceNumber: 0,
                observedAt);
            var stream = new OutputAudioStream
            {
                OutputFlowId = outputFlow.Id,
                ResponseId = requestWithProvider.ResponseId,
                SegmentId = segmentId,
                SegmentIndex = requestWithProvider.SegmentIndex,
                IsFinalSegment = requestWithProvider.IsFinalSegment,
                SourceTextStart = requestWithProvider.SourceTextStart,
                SourceTextLength = requestWithProvider.SourceTextLength,
                ProviderKey = providerKey,
                ModelId = modelId,
                VoiceId = requestWithProvider.VoiceId,
                Language = requestWithProvider.Language,
                OutputFormat = requestWithProvider.OutputFormat,
                MediaType = payload.MediaType,
                PayloadKind = payload.Kind,
                StartedAt = observedAt
            };
            var chunk = new OutputAudioChunk
            {
                OutputFlowId = outputFlow.Id,
                ResponseId = requestWithProvider.ResponseId,
                SegmentId = segmentId,
                SegmentIndex = requestWithProvider.SegmentIndex,
                Sequence = 0,
                Payload = payload,
                ObservedAt = observedAt,
                IsFinalChunk = true
            };
            await outputFlow.StartAudioStreamAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            var sinkAccepted = await TryStartOutputSinkAsync(stream, context, cancellationToken)
                .ConfigureAwait(false);
            context.EmitEvent?.Invoke(new AssistantAudioOutputStreamStartedEvent(
                context.SessionId.Value,
                outputFlow.Id.Value,
                requestWithProvider.ResponseId.Value,
                segmentId.Value,
                requestWithProvider.SegmentIndex,
                providerKey,
                modelId,
                requestWithProvider.VoiceId,
                requestWithProvider.Language,
                requestWithProvider.OutputFormat,
                mediaType,
                stream.PayloadKind.ToString()));
            await outputFlow.AppendAudioChunkAsync(chunk, cancellationToken)
                .ConfigureAwait(false);
            if (sinkAccepted)
            {
                await context.OutputSink!.WriteAsync(chunk, cancellationToken)
                    .ConfigureAwait(false);
            }
            context.EmitEvent?.Invoke(new AssistantAudioOutputChunkReadyEvent(
                context.SessionId.Value,
                outputFlow.Id.Value,
                requestWithProvider.ResponseId.Value,
                segmentId.Value,
                requestWithProvider.SegmentIndex,
                chunk.Sequence,
                providerKey,
                modelId,
                requestWithProvider.VoiceId,
                requestWithProvider.Language,
                requestWithProvider.OutputFormat,
                chunk.MediaType,
                chunk.SizeBytes,
                chunk.Duration,
                chunk.IsFinalChunk,
                chunk.Payload.Kind.ToString()));
            var completion = new OutputAudioStreamCompletion
            {
                OutputFlowId = outputFlow.Id,
                ResponseId = requestWithProvider.ResponseId,
                SegmentId = segmentId,
                SegmentIndex = requestWithProvider.SegmentIndex,
                Disposition = OutputAudioStreamDisposition.Completed,
                ChunkCount = 1,
                SizeBytes = chunk.SizeBytes,
                Duration = chunk.Duration,
                CompletedAt = DateTimeOffset.UtcNow
            };
            await outputFlow.CompleteAudioStreamAsync(completion, cancellationToken).ConfigureAwait(false);
            if (sinkAccepted)
            {
                await context.OutputSink!.CompleteAsync(completion, cancellationToken)
                    .ConfigureAwait(false);
            }
            context.EmitEvent?.Invoke(new AssistantAudioOutputStreamCompletedEvent(
                context.SessionId.Value,
                outputFlow.Id.Value,
                requestWithProvider.ResponseId.Value,
                segmentId.Value,
                requestWithProvider.SegmentIndex,
                OutputAudioStreamDisposition.Completed.ToString(),
                1,
                chunk.SizeBytes,
                chunk.Duration));

            _ledgerTraceWriter.AppendTtsResult(
                ledger,
                trace,
                context.SessionId,
                context.Correlation,
                outputFlow.Id,
                requestWithProvider.ResponseId,
                requestWithProvider,
                providerKey,
                modelId,
                context.Options,
                TtsSynthesisDisposition.Synthesized,
                chunk.MediaType,
                chunk.SizeBytes,
                chunk.Duration,
                null);
            if (RequiresContentStoreArtifact(context.Options))
            {
                var artifact = await _artifactWriter.WriteAssistantAudioArtifactAsync(
                    context.Options.ContentStore!,
                    context.SessionId,
                    outputFlow.Id,
                    requestWithProvider.ResponseId,
                    providerKey,
                    modelId,
                    context.Options,
                    chunk.MediaType,
                    audioResponse.Data,
                    cancellationToken).ConfigureAwait(false);

                await outputFlow.AttachAudioArtifactAsync(new OutputAudioArtifact
                {
                    OutputFlowId = outputFlow.Id,
                    ResponseId = requestWithProvider.ResponseId,
                    SegmentId = segmentId,
                    SegmentIndex = requestWithProvider.SegmentIndex,
                    Artifact = artifact.Artifact,
                    MediaType = chunk.MediaType,
                    SizeBytes = artifact.SizeBytes,
                    Sha256 = artifact.Sha256,
                    Duration = chunk.Duration,
                    CapturedAt = DateTimeOffset.UtcNow
                }, cancellationToken).ConfigureAwait(false);
                context.EmitEvent?.Invoke(new AssistantAudioOutputArtifactCapturedEvent(
                    context.SessionId.Value,
                    outputFlow.Id.Value,
                    requestWithProvider.ResponseId.Value,
                    segmentId.Value,
                    requestWithProvider.SegmentIndex,
                    chunk.MediaType,
                    artifact.Artifact,
                    artifact.SizeBytes,
                    artifact.Sha256,
                    chunk.Duration));
                _ledgerTraceWriter.AppendOutputArtifact(
                    ledger,
                    trace,
                    context.SessionId,
                    context.Correlation,
                    outputFlow.Id,
                    requestWithProvider.ResponseId,
                    requestWithProvider,
                    artifact);
            }

            return new TextToSpeechSegmentSynthesisResult
            {
                OutputFlowId = outputFlow.Id,
                ResponseId = requestWithProvider.ResponseId,
                SegmentId = segmentId,
                SegmentIndex = requestWithProvider.SegmentIndex,
                Disposition = TtsSynthesisDisposition.Synthesized,
                Text = requestWithProvider.Text,
                MediaType = mediaType,
                Ledger = ledger,
                Trace = trace
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var error = ToErrorInfo(ex);
            _ledgerTraceWriter.AppendTtsResult(
                ledger,
                trace,
                context.SessionId,
                context.Correlation,
                outputFlow.Id,
                requestWithProvider.ResponseId,
                requestWithProvider,
                providerKey,
                modelId,
                context.Options,
                TtsSynthesisDisposition.Failed,
                null,
                null,
                null,
                error);
            return FailedResult(
                outputFlow.Id,
                requestWithProvider,
                TtsSynthesisDisposition.Failed,
                error,
                ledger,
                trace);
        }
    }

    private static TextToSpeechSegmentSynthesisResult FailedResult(
        OutputFlowId outputFlowId,
        TextToSpeechSegmentRequest request,
        TtsSynthesisDisposition disposition,
        AudioErrorInfo error,
        IReadOnlyList<RealtimeLedgerRecord> ledger,
        IReadOnlyList<RealtimeAudioTraceRecord> trace)
    {
        return new TextToSpeechSegmentSynthesisResult
        {
            OutputFlowId = outputFlowId,
            ResponseId = request.ResponseId,
            SegmentId = request.SegmentId ?? new OutputSegmentId($"{outputFlowId.Value}:audio-{request.SegmentIndex + 1:D4}"),
            SegmentIndex = request.SegmentIndex,
            Disposition = disposition,
            Text = request.Text,
            Error = error,
            Ledger = ledger,
            Trace = trace
        };
    }

    private static string ResolveProviderKey(AssistantTextToSpeechOutputOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ProviderKey))
        {
            return options.ProviderKey!;
        }

        var metadata = options.TextToSpeechClient.GetService(typeof(TextToSpeechClientMetadata)) as TextToSpeechClientMetadata;
        return FirstNonWhiteSpace(metadata?.ProviderName, "unknown")!;
    }

    private static bool RequiresContentStoreArtifact(AssistantTextToSpeechOutputOptions options) =>
        options.ArtifactCapturePolicy == AssistantAudioArtifactCapturePolicy.ContentStoreArtifact;

    private static AudioErrorInfo ToErrorInfo(Exception exception)
    {
        return new AudioErrorInfo
        {
            Code = exception.GetType().Name,
            Message = exception.Message,
            Category = "TextToSpeech",
            IsRetryable = false
        };
    }

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static async ValueTask<SynthesizedAudioData> GetCompletedAudioDataAsync(
        ITextToSpeechClient client,
        string text,
        TextToSpeechOptions options,
        CancellationToken cancellationToken)
    {
        var response = await client.GetAudioAsync(text, options, cancellationToken)
            .ConfigureAwait(false);
        var audio = response.Contents.OfType<DataContent>().FirstOrDefault(content =>
            content.HasTopLevelMediaType("audio"));

        if (audio is null || audio.Data.IsEmpty)
        {
            throw new InvalidOperationException("Text-to-speech response did not contain audio data.");
        }

        return new SynthesizedAudioData(audio.Data, audio.MediaType, response.ModelId);
    }

    private async ValueTask<TextToSpeechSegmentSynthesisResult> SynthesizeStreamingAudioAsync(
        IOutputFlow outputFlow,
        TextToSpeechSegmentRequest request,
        TextToSpeechSynthesisContext context,
        string providerKey,
        string? modelId,
        TextToSpeechOptions ttsOptions,
        List<RealtimeLedgerRecord> ledger,
        List<RealtimeAudioTraceRecord> trace,
        CancellationToken cancellationToken)
    {
        var segmentId = request.SegmentId ??
            new OutputSegmentId($"{outputFlow.Id.Value}:audio-{request.SegmentIndex + 1:D4}");
        using var audioBuffer = new MemoryStream();
        string? mediaType = null;
        OutputAudioStreamStart? streamStart = null;
        DateTimeOffset? providerFirstAudioAt = null;
        var sequence = 0;
        var totalDuration = TimeSpan.Zero;

        await foreach (var update in context.Options.TextToSpeechClient
            .GetStreamingAudioAsync(request.Text, ttsOptions, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            modelId = FirstNonWhiteSpace(update.ModelId, modelId);
            foreach (var audio in update.Contents.OfType<DataContent>().Where(content =>
                content.HasTopLevelMediaType("audio")))
            {
                if (audio.Data.IsEmpty)
                {
                    continue;
                }

                providerFirstAudioAt ??= DateTimeOffset.UtcNow;
                mediaType = FirstNonWhiteSpace(mediaType, audio.MediaType, request.ContentType, OutputArtifactWriter.ToMediaType(request.OutputFormat))
                    ?? "application/octet-stream";
                var bytes = audio.Data.ToArray();
                var observedAt = DateTimeOffset.UtcNow;
                var payload = OutputAudioPayloadFactory.Create(
                    bytes,
                    mediaType,
                    request.OutputFormat,
                    sequence,
                    observedAt);
                streamStart ??= await StartAudioStreamAsync(
                    outputFlow,
                    request,
                    context,
                    segmentId,
                    providerKey,
                    modelId,
                    payload.MediaType,
                    payload.Kind,
                    cancellationToken).ConfigureAwait(false);

                audioBuffer.Write(bytes);
                var chunk = new OutputAudioChunk
                {
                    OutputFlowId = outputFlow.Id,
                    ResponseId = request.ResponseId,
                    SegmentId = segmentId,
                    SegmentIndex = request.SegmentIndex,
                    Sequence = sequence++,
                    Payload = payload,
                    ObservedAt = observedAt,
                    IsFinalChunk = false
                };
                if (chunk.Duration is { } chunkDuration)
                {
                    totalDuration += chunkDuration;
                }

                await outputFlow.AppendAudioChunkAsync(chunk, cancellationToken)
                    .ConfigureAwait(false);
                if (streamStart.SinkAccepted)
                {
                    await context.OutputSink!.WriteAsync(chunk, cancellationToken)
                        .ConfigureAwait(false);
                }

                context.EmitEvent?.Invoke(new AssistantAudioOutputChunkReadyEvent(
                    context.SessionId.Value,
                    outputFlow.Id.Value,
                    request.ResponseId.Value,
                    segmentId.Value,
                    request.SegmentIndex,
                    chunk.Sequence,
                    providerKey,
                    modelId,
                    request.VoiceId,
                    request.Language,
                    request.OutputFormat,
                    chunk.MediaType,
                    chunk.SizeBytes,
                    chunk.Duration,
                    chunk.IsFinalChunk,
                    chunk.Payload.Kind.ToString()));
            }
        }

        if (audioBuffer.Length == 0 || streamStart is null || mediaType is null)
        {
            throw new InvalidOperationException("Streaming text-to-speech response did not contain audio data.");
        }

        var completion = new OutputAudioStreamCompletion
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = request.ResponseId,
            SegmentId = segmentId,
            SegmentIndex = request.SegmentIndex,
            Disposition = OutputAudioStreamDisposition.Completed,
            ChunkCount = sequence,
            SizeBytes = audioBuffer.Length,
            Duration = totalDuration > TimeSpan.Zero ? totalDuration : null,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await outputFlow.CompleteAudioStreamAsync(completion, cancellationToken).ConfigureAwait(false);
        if (streamStart.SinkAccepted)
        {
            await context.OutputSink!.CompleteAsync(completion, cancellationToken)
                .ConfigureAwait(false);
        }

        context.EmitEvent?.Invoke(new AssistantAudioOutputStreamCompletedEvent(
            context.SessionId.Value,
            outputFlow.Id.Value,
            request.ResponseId.Value,
            segmentId.Value,
            request.SegmentIndex,
            OutputAudioStreamDisposition.Completed.ToString(),
            sequence,
            audioBuffer.Length,
            completion.Duration));

        _ledgerTraceWriter.AppendTtsResult(
            ledger,
            trace,
            context.SessionId,
            context.Correlation,
            outputFlow.Id,
            request.ResponseId,
            request,
            providerKey,
            modelId,
            context.Options,
            TtsSynthesisDisposition.Synthesized,
            streamStart.Stream.MediaType,
            audioBuffer.Length,
            completion.Duration,
            null,
            providerFirstAudioAt);

        if (RequiresContentStoreArtifact(context.Options))
        {
            var artifact = await _artifactWriter.WriteAssistantAudioArtifactAsync(
                context.Options.ContentStore!,
                context.SessionId,
                outputFlow.Id,
                request.ResponseId,
                providerKey,
                modelId,
                context.Options,
                streamStart.Stream.MediaType,
                audioBuffer.ToArray(),
                cancellationToken).ConfigureAwait(false);

            await outputFlow.AttachAudioArtifactAsync(new OutputAudioArtifact
            {
                OutputFlowId = outputFlow.Id,
                ResponseId = request.ResponseId,
                SegmentId = segmentId,
                SegmentIndex = request.SegmentIndex,
                Artifact = artifact.Artifact,
                MediaType = streamStart.Stream.MediaType,
                SizeBytes = artifact.SizeBytes,
                Sha256 = artifact.Sha256,
                Duration = completion.Duration,
                CapturedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            context.EmitEvent?.Invoke(new AssistantAudioOutputArtifactCapturedEvent(
                context.SessionId.Value,
                outputFlow.Id.Value,
                request.ResponseId.Value,
                segmentId.Value,
                request.SegmentIndex,
                streamStart.Stream.MediaType,
                artifact.Artifact,
                artifact.SizeBytes,
                artifact.Sha256,
                completion.Duration));
            _ledgerTraceWriter.AppendOutputArtifact(
                ledger,
                trace,
                context.SessionId,
                context.Correlation,
                outputFlow.Id,
                request.ResponseId,
                request,
                artifact);
        }

        return new TextToSpeechSegmentSynthesisResult
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = request.ResponseId,
            SegmentId = segmentId,
            SegmentIndex = request.SegmentIndex,
            Disposition = TtsSynthesisDisposition.Synthesized,
            Text = request.Text,
            MediaType = mediaType,
            Ledger = ledger,
            Trace = trace
        };
    }

    private static async ValueTask<OutputAudioStreamStart> StartAudioStreamAsync(
        IOutputFlow outputFlow,
        TextToSpeechSegmentRequest request,
        TextToSpeechSynthesisContext context,
        OutputSegmentId segmentId,
        string providerKey,
        string? modelId,
        string mediaType,
        OutputAudioPayloadKind payloadKind,
        CancellationToken cancellationToken)
    {
        var stream = new OutputAudioStream
        {
            OutputFlowId = outputFlow.Id,
            ResponseId = request.ResponseId,
            SegmentId = segmentId,
            SegmentIndex = request.SegmentIndex,
            IsFinalSegment = request.IsFinalSegment,
            SourceTextStart = request.SourceTextStart,
            SourceTextLength = request.SourceTextLength,
            ProviderKey = providerKey,
            ModelId = modelId,
            VoiceId = request.VoiceId,
            Language = request.Language,
            OutputFormat = request.OutputFormat,
            MediaType = mediaType,
            PayloadKind = payloadKind,
            StartedAt = DateTimeOffset.UtcNow
        };

        await outputFlow.StartAudioStreamAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        var sinkAccepted = await TryStartOutputSinkAsync(stream, context, cancellationToken)
            .ConfigureAwait(false);

        context.EmitEvent?.Invoke(new AssistantAudioOutputStreamStartedEvent(
            context.SessionId.Value,
            outputFlow.Id.Value,
            request.ResponseId.Value,
            segmentId.Value,
            request.SegmentIndex,
            providerKey,
            modelId,
            request.VoiceId,
            request.Language,
            request.OutputFormat,
            mediaType,
            stream.PayloadKind.ToString()));

        return new OutputAudioStreamStart(stream, sinkAccepted);
    }

    private static async ValueTask<bool> TryStartOutputSinkAsync(
        OutputAudioStream stream,
        TextToSpeechSynthesisContext context,
        CancellationToken cancellationToken)
    {
        if (!context.EnablePlayback || context.OutputSink is null)
        {
            return false;
        }

        var result = await context.OutputSink.StartAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        context.RecordPlaybackStartFailure?.Invoke(result);
        return result.Disposition == OutputSinkStartDisposition.Accepted;
    }
}

internal sealed record OutputAudioStreamStart(
    OutputAudioStream Stream,
    bool SinkAccepted);

internal sealed record SynthesizedAudioData(
    ReadOnlyMemory<byte> Data,
    string? MediaType,
    string? ModelId);

internal sealed record TextToSpeechSynthesisContext
{
    public required AudioSessionId SessionId { get; init; }

    public required ThreadRef Thread { get; init; }

    public required AudioCorrelation Correlation { get; init; }

    public required AssistantTextToSpeechOutputOptions Options { get; init; }

    public Action<AgentEvent>? EmitEvent { get; init; }

    public IAudioOutputSink? OutputSink { get; init; }

    public bool EnablePlayback { get; init; }

    public Action<OutputSinkStartResult>? RecordPlaybackStartFailure { get; init; }
}

internal sealed record TextToSpeechSegmentSynthesisResult
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public required TtsSynthesisDisposition Disposition { get; init; }

    public string Text { get; init; } = string.Empty;

    public string? MediaType { get; init; }

    public AudioErrorInfo? Error { get; init; }

    public required IReadOnlyList<RealtimeLedgerRecord> Ledger { get; init; }

    public required IReadOnlyList<RealtimeAudioTraceRecord> Trace { get; init; }

    public AssistantTextToSpeechOutputResult ToOutputResult(AudioSessionId sessionId)
    {
        return new AssistantTextToSpeechOutputResult
        {
            SessionId = sessionId,
            OutputFlowId = OutputFlowId,
            ResponseId = ResponseId,
            Status = Disposition == TtsSynthesisDisposition.Synthesized
                ? AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed
                : AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly,
            SegmentId = SegmentId,
            SegmentIndex = SegmentIndex,
            Text = Text,
            MediaType = MediaType,
            Error = Error,
            Ledger = Ledger,
            Trace = Trace
        };
    }
}
