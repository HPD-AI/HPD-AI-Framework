using System.Threading.Channels;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Trace;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.AgentIntegration.Output;

#pragma warning disable MEAI001

internal interface IProgressiveTextToSpeechEngine
{
    OutputFlowId OutputFlowId { get; }

    IOutputProjectionSinkV2 Flow { get; }

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask PushTextAsync(
        string textDelta,
        ResponseId responseId,
        CancellationToken cancellationToken = default);

    ValueTask<ProgressiveTextToSpeechEngineCompletion> CompleteAsync(
        ResponseId responseId,
        CancellationToken cancellationToken = default);

    void Cancel(Exception? exception = null);
}

internal sealed record ProgressiveTextToSpeechEngineCompletion
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required IReadOnlyList<AssistantTextToSpeechOutputResult> Results { get; init; }

    public IReadOnlyList<OutputPlaybackFailedEvent> PlaybackStartFailures { get; init; } = [];
}

internal sealed class ProgressiveTextToSpeechEngineFactory
{
    public IProgressiveTextToSpeechEngine Create(
        S6ProgressiveOutputParticipantOptionsV2 options,
        IOutputProjectionSinkV2 flow)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(flow);

        var client = options.OutputOptions?.TextToSpeechClient;
        var profile = client?.GetService(typeof(TextToSpeechCapabilityProfile)) as TextToSpeechCapabilityProfile
            ?? new TextToSpeechCapabilityProfile();
        var pushFactory = client?.GetService(typeof(IPushTextToSpeechStreamFactory)) as IPushTextToSpeechStreamFactory;
        var canPushText = profile.SupportsPushTextAudioStreaming && pushFactory is not null;

        if (options.RouteMode == ProgressiveTextToSpeechRouteMode.ForceSegment)
        {
            return CreateSegment(options, flow);
        }

        if (canPushText)
        {
            return new PushTextToSpeechEngine(options, flow, pushFactory!);
        }

        if (options.RouteMode == ProgressiveTextToSpeechRouteMode.ForcePushText)
        {
            return new UnsupportedPushTextToSpeechEngine(options, flow, profile, pushFactory is not null);
        }

        return CreateSegment(options, flow);
    }

    private static IProgressiveTextToSpeechEngine CreateSegment(
        S6ProgressiveOutputParticipantOptionsV2 options,
        IOutputProjectionSinkV2 flow) =>
        new SegmentTextToSpeechEngine(
            options,
            flow,
            new SentenceTtsPacer(),
            new TextToSpeechTextSanitizer(),
            new TextToSpeechSegmentSynthesizer());
}

internal abstract class ProgressiveTextToSpeechEngineBase : IProgressiveTextToSpeechEngine
{
    protected readonly S6ProgressiveOutputParticipantOptionsV2 Options;
    protected readonly IOutputProjectionSinkV2 OutputFlow;
    protected readonly OutputLedgerTraceWriter LedgerTraceWriter = new();
    private readonly List<AssistantTextToSpeechOutputResult> _results = [];
    private readonly List<OutputPlaybackFailedEvent> _playbackStartFailures = [];
    private readonly object _gate = new();
    private bool _started;

    protected ProgressiveTextToSpeechEngineBase(
        S6ProgressiveOutputParticipantOptionsV2 options,
        IOutputProjectionSinkV2 outputFlow)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        OutputFlow = outputFlow ?? throw new ArgumentNullException(nameof(outputFlow));
    }

    public OutputFlowId OutputFlowId => OutputFlow.Id;

    public IOutputProjectionSinkV2 Flow => OutputFlow;

    public abstract ValueTask StartAsync(CancellationToken cancellationToken = default);

    public abstract ValueTask PushTextAsync(
        string textDelta,
        ResponseId responseId,
        CancellationToken cancellationToken = default);

    public abstract ValueTask<ProgressiveTextToSpeechEngineCompletion> CompleteAsync(
        ResponseId responseId,
        CancellationToken cancellationToken = default);

    public abstract void Cancel(Exception? exception = null);

    protected void AddResult(AssistantTextToSpeechOutputResult result)
    {
        lock (_gate)
        {
            _results.Add(result);
        }
    }

    protected IReadOnlyList<AssistantTextToSpeechOutputResult> SnapshotResults()
    {
        lock (_gate)
        {
            return _results
                .OrderBy(result => result.SegmentIndex ?? int.MaxValue)
                .ToArray();
        }
    }

    protected void AddPlaybackStartFailure(OutputSinkStartResult result)
    {
        if (result.Disposition == OutputSinkStartDisposition.Accepted)
        {
            return;
        }

        var error = result.Error ?? new AudioErrorInfo
        {
            Code = "OutputSinkStartRejected",
            Message = $"The output sink returned {result.Disposition} while starting playback.",
            Category = "Playback"
        };

        lock (_gate)
        {
            _playbackStartFailures.Add(new OutputPlaybackFailedEvent
            {
                OutputFlowId = result.OutputFlowId,
                ResponseId = result.ResponseId,
                SegmentId = result.SegmentId,
                SegmentIndex = result.SegmentIndex,
                Error = error,
                ObservedAt = DateTimeOffset.UtcNow
            });
        }
    }

    protected IReadOnlyList<OutputPlaybackFailedEvent> SnapshotPlaybackStartFailures()
    {
        lock (_gate)
        {
            return _playbackStartFailures
                .OrderBy(failure => failure.SegmentIndex)
                .ToArray();
        }
    }

    protected async ValueTask EnsureStartedAsync(
        ResponseId responseId,
        CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await PublishAsync(new AssistantAudioOutputStartedEvent(
            Options.SessionId.Value,
            OutputFlow.Id.Value,
            responseId.Value,
            ResolveProviderKey(Options.OutputOptions),
            Options.OutputOptions?.ModelId,
            Options.OutputOptions?.VoiceId,
            Options.OutputOptions?.Language,
            Options.OutputOptions?.OutputFormat), cancellationToken).ConfigureAwait(false);
    }

    protected async ValueTask PublishSegmentEventAsync(
        AssistantTextToSpeechOutputResult result,
        OutputSegmentId segmentId,
        int segmentIndex,
        bool isFinalSegment,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly:
            {
                var synthesis = result.Trace
                    .OfType<AudioTtsSynthesisTraceRecord>()
                    .LastOrDefault(t => t.Disposition is TtsSynthesisDisposition.Failed or TtsSynthesisDisposition.Unsupported);
                await PublishAsync(new AssistantAudioOutputSegmentFailedEvent(
                    result.SessionId.Value,
                    result.OutputFlowId.Value,
                    result.ResponseId.Value,
                    segmentId.Value,
                    segmentIndex,
                    synthesis?.ProviderKey ?? ResolveProviderKey(Options.OutputOptions),
                    synthesis?.ModelId,
                    synthesis?.VoiceId,
                    synthesis?.Language,
                    synthesis?.OutputFormat,
                    result.Error ?? synthesis?.Error,
                    result.Status.ToString(),
                    isFinalSegment), cancellationToken).ConfigureAwait(false);
                break;
            }
        }
    }

    protected async ValueTask PublishAsync(AgentEvent evt, CancellationToken cancellationToken)
    {
        if (Options.PublishEventAsync is { } publish)
        {
            await publish(evt, cancellationToken).ConfigureAwait(false);
        }
    }

    protected AudioCorrelation CreateCorrelation()
    {
        return new AudioCorrelation
        {
            ConversationId = Options.Thread.SessionId,
            RequestId = Options.RequestId,
            SessionId = Options.SessionId,
            OutputFlowId = OutputFlow.Id
        };
    }

    protected static string ResolveProviderKey(AssistantTextToSpeechOutputOptions? options)
    {
        if (!string.IsNullOrWhiteSpace(options?.ProviderKey))
        {
            return options.ProviderKey!;
        }

        var metadata = options?.TextToSpeechClient.GetService(typeof(TextToSpeechClientMetadata)) as TextToSpeechClientMetadata;
        return string.IsNullOrWhiteSpace(metadata?.ProviderName) ? "unknown" : metadata!.ProviderName!;
    }

    protected static AudioErrorInfo ToErrorInfo(Exception exception)
    {
        return new AudioErrorInfo
        {
            Code = exception.GetType().Name,
            Message = exception.Message,
            Category = "TextToSpeech",
            IsRetryable = false
        };
    }
}

internal sealed class SegmentTextToSpeechEngine : ProgressiveTextToSpeechEngineBase
{
    private readonly Channel<ProgressiveTextDelta> _deltas = Channel.CreateUnbounded<ProgressiveTextDelta>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });
    private readonly ITextToSpeechSegmentSynthesizer _synthesizer;
    private readonly TextToSpeechTextSanitizer _sanitizer;
    private readonly ITtsPacer _pacer;
    private readonly bool _appendGeneratedText;
    private readonly bool _emitStartedEvent;
    private Task? _worker;
    private ResponseId _responseId;
    private int _generatedTextLength;

    public SegmentTextToSpeechEngine(
        S6ProgressiveOutputParticipantOptionsV2 options,
        IOutputProjectionSinkV2 outputFlow,
        ITtsPacer pacer,
        TextToSpeechTextSanitizer sanitizer,
        ITextToSpeechSegmentSynthesizer synthesizer,
        bool appendGeneratedText = true,
        bool emitStartedEvent = true)
        : base(options, outputFlow)
    {
        _pacer = pacer ?? throw new ArgumentNullException(nameof(pacer));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _synthesizer = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
        _appendGeneratedText = appendGeneratedText;
        _emitStartedEvent = emitStartedEvent;
        _responseId = options.InitialResponseId;
    }

    public override ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _worker ??= Task.Run(() => ProcessAsync(cancellationToken), cancellationToken);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask PushTextAsync(
        string textDelta,
        ResponseId responseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textDelta);
        cancellationToken.ThrowIfCancellationRequested();

        if (textDelta.Length == 0)
        {
            return;
        }

        _responseId = responseId;
        _generatedTextLength += textDelta.Length;
        if (_appendGeneratedText)
        {
            await OutputFlow.AppendTextAsync(responseId, textDelta, isFinal: false, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_emitStartedEvent)
        {
            await EnsureStartedAsync(responseId, cancellationToken).ConfigureAwait(false);
        }

        await _deltas.Writer.WriteAsync(
            new ProgressiveTextDelta(textDelta, responseId, _generatedTextLength),
            cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<ProgressiveTextToSpeechEngineCompletion> CompleteAsync(
        ResponseId responseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _responseId = responseId;
        _deltas.Writer.TryComplete();

        if (_worker is not null)
        {
            await _worker.ConfigureAwait(false);
        }

        return new ProgressiveTextToSpeechEngineCompletion
        {
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Results = SnapshotResults(),
            PlaybackStartFailures = SnapshotPlaybackStartFailures()
        };
    }

    public override void Cancel(Exception? exception = null)
    {
        _deltas.Writer.TryComplete(exception);
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var synthesisTasks = new List<Task>();
        await foreach (var delta in _deltas.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var segment in _pacer.PushText(
                delta.Text,
                CreatePacingContext(delta.ResponseId, delta.GeneratedTextLength, isFinalInput: false)))
            {
                await QueueSynthesisAsync(synthesisTasks, segment, delta.ResponseId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        foreach (var segment in _pacer.Flush(
            CreatePacingContext(_responseId, _generatedTextLength, isFinalInput: true)))
        {
            await QueueSynthesisAsync(synthesisTasks, segment, _responseId, cancellationToken)
                .ConfigureAwait(false);
        }

        await Task.WhenAll(synthesisTasks).ConfigureAwait(false);
    }

    private async ValueTask QueueSynthesisAsync(
        List<Task> synthesisTasks,
        TextToSpeechSegment segment,
        ResponseId responseId,
        CancellationToken cancellationToken)
    {
        await RemoveCompletedSynthesisAsync(synthesisTasks).ConfigureAwait(false);

        var maxInFlight = Math.Max(1, Options.PacingOptions.Continuation.MaxInFlightSynthesisRequests);
        while (synthesisTasks.Count >= maxInFlight)
        {
            var completed = await Task.WhenAny(synthesisTasks).ConfigureAwait(false);
            synthesisTasks.Remove(completed);
            await completed.ConfigureAwait(false);
        }

        synthesisTasks.Add(SynthesizeSegmentAsync(segment, responseId, cancellationToken).AsTask());
    }

    private static async ValueTask RemoveCompletedSynthesisAsync(List<Task> synthesisTasks)
    {
        for (var i = synthesisTasks.Count - 1; i >= 0; i--)
        {
            var task = synthesisTasks[i];
            if (!task.IsCompleted)
            {
                continue;
            }

            synthesisTasks.RemoveAt(i);
            await task.ConfigureAwait(false);
        }
    }

    private async ValueTask SynthesizeSegmentAsync(
        TextToSpeechSegment segment,
        ResponseId responseId,
        CancellationToken cancellationToken)
    {
        segment = _sanitizer.Sanitize(segment, Options.PacingOptions.Filtering);
        if (string.IsNullOrWhiteSpace(segment.Text))
        {
            return;
        }

        var result = Options.OutputOptions is null
            ? CreateMissingOptionsResult(segment, responseId)
            : (await _synthesizer.SynthesizeAsync(
                OutputFlow,
                CreateTextToSpeechRequest(segment, responseId, Options.OutputOptions),
                new TextToSpeechSynthesisContext
                {
                    MessageTurnId = Options.MessageTurnId,
                    SessionId = Options.SessionId,
                    Thread = Options.Thread,
                    Correlation = CreateCorrelation(),
                    Options = Options.OutputOptions,
                    PublishEventAsync = Options.PublishEventAsync,
                    OutputSink = Options.OutputSink,
                    EnablePlayback = Options.EnablePlayback,
                    RecordPlaybackStartFailure = AddPlaybackStartFailure
                },
                cancellationToken).ConfigureAwait(false)).ToOutputResult(Options.SessionId);

        AddResult(result);
        await PublishSegmentEventAsync(
            result,
            segment.SegmentId,
            segment.SegmentIndex,
            segment.IsFinalSegment,
            cancellationToken).ConfigureAwait(false);
    }

    private TextToSpeechSegmentRequest CreateTextToSpeechRequest(
        TextToSpeechSegment segment,
        ResponseId responseId,
        AssistantTextToSpeechOutputOptions outputOptions)
    {
        return new TextToSpeechSegmentRequest
        {
            ResponseId = responseId,
            Text = segment.Text,
            SegmentId = segment.SegmentId,
            SegmentIndex = segment.SegmentIndex,
            IsFinalSegment = segment.IsFinalSegment,
            SourceTextStart = segment.SourceTextStart,
            SourceTextLength = segment.SourceTextLength,
            ProviderKey = ResolveProviderKey(outputOptions),
            ModelId = outputOptions.ModelId,
            VoiceId = outputOptions.VoiceId,
            Language = outputOptions.Language,
            OutputFormat = outputOptions.OutputFormat,
            ContentType = outputOptions.ContentType
        };
    }

    private AssistantTextToSpeechOutputResult CreateMissingOptionsResult(
        TextToSpeechSegment segment,
        ResponseId responseId)
    {
        return new AssistantTextToSpeechOutputResult
        {
            SessionId = Options.SessionId,
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Status = AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly,
            SegmentId = segment.SegmentId,
            SegmentIndex = segment.SegmentIndex,
            Text = segment.Text,
            Error = new AudioErrorInfo
            {
                Code = "MissingTextToSpeechClient",
                Message = "Progressive assistant TTS requires ITextToSpeechClient; no text-to-speech client is configured.",
                Category = "TextToSpeech",
                IsRetryable = false
            },
            Ledger = [],
            Trace = []
        };
    }

    private TextToSpeechPacingContext CreatePacingContext(
        ResponseId responseId,
        int generatedTextLength,
        bool isFinalInput)
    {
        return new TextToSpeechPacingContext
        {
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Options = Options.PacingOptions,
            GeneratedTextLength = generatedTextLength,
            IsFinalInput = isFinalInput
        };
    }
}

internal sealed class PushTextToSpeechEngine : ProgressiveTextToSpeechEngineBase
{
    private readonly IPushTextToSpeechStreamFactory _streamFactory;
    private readonly OutputArtifactWriter _artifactWriter = new();
    private readonly MemoryStream _audioBuffer = new();
    private readonly List<ProgressiveTextDelta> _textDeltas = [];
    private readonly OutputSegmentId _segmentId;
    private IPushTextToSpeechStream? _stream;
    private SegmentTextToSpeechEngine? _fallbackEngine;
    private Task? _reader;
    private string? _mediaType;
    private string? _streamMediaType;
    private string? _modelId;
    private DateTimeOffset? _providerFirstAudioAt;
    private bool _audioStreamStarted;
    private bool _audioStreamCompleted;
    private bool _audioSinkAccepted;
    private bool _firstOutputChunkEmitted;
    private int _chunkSequence;
    private TimeSpan _audioDuration;
    private Exception? _terminalPushException;
    private ResponseId _responseId;
    private int _generatedTextLength;

    public PushTextToSpeechEngine(
        S6ProgressiveOutputParticipantOptionsV2 options,
        IOutputProjectionSinkV2 outputFlow,
        IPushTextToSpeechStreamFactory streamFactory)
        : base(options, outputFlow)
    {
        _streamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
        _responseId = options.InitialResponseId;
        _segmentId = new OutputSegmentId($"{OutputFlow.Id.Value}:push-text-0001");
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return;
    }

    public override async ValueTask PushTextAsync(
        string textDelta,
        ResponseId responseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textDelta);
        cancellationToken.ThrowIfCancellationRequested();

        if (textDelta.Length == 0)
        {
            return;
        }

        _responseId = responseId;
        var sourceStart = _generatedTextLength;
        _generatedTextLength += textDelta.Length;
        var delta = new ProgressiveTextDelta(textDelta, responseId, _generatedTextLength);
        _textDeltas.Add(delta);
        await OutputFlow.AppendTextAsync(responseId, textDelta, isFinal: false, cancellationToken)
            .ConfigureAwait(false);
        await EnsureStartedAsync(responseId, cancellationToken).ConfigureAwait(false);

        if (_fallbackEngine is not null)
        {
            await _fallbackEngine.PushTextAsync(textDelta, responseId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_terminalPushException is not null)
        {
            return;
        }

        if (_stream is null)
        {
            try
            {
                await OpenPushStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && CanFallbackToSegments())
            {
                await ActivateSegmentFallbackAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _terminalPushException = ex;
                return;
            }
        }

        if (_stream is not null)
        {
            try
            {
                await _stream.PushTextAsync(
                    new PushTextToSpeechInput
                    {
                        ResponseId = responseId,
                        Text = textDelta,
                        SourceTextStart = sourceStart,
                        SourceTextLength = textDelta.Length
                    },
                    cancellationToken).ConfigureAwait(false);
                await PublishPushTextInputSentAsync(
                    responseId,
                    sourceStart,
                    textDelta.Length,
                    isFinalInput: false,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && CanFallbackToSegments())
            {
                await ActivateSegmentFallbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _terminalPushException = ex;
            }
        }
    }

    public override async ValueTask<ProgressiveTextToSpeechEngineCompletion> CompleteAsync(
        ResponseId responseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _responseId = responseId;

        if (_fallbackEngine is not null)
        {
            return await _fallbackEngine.CompleteAsync(responseId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_terminalPushException is not null)
        {
            var failed = CreateFailedResult(
                responseId,
                "PushTextTtsFailed",
                _terminalPushException.Message,
                TtsSynthesisDisposition.Failed);
            AddResult(failed);
                await PublishSegmentEventAsync(
                    failed,
                    failed.SegmentId ?? new OutputSegmentId($"{OutputFlow.Id.Value}:push-text-0001"),
                    failed.SegmentIndex ?? 0,
                    isFinalSegment: true,
                    cancellationToken).ConfigureAwait(false);
            return CreateCompletion(responseId);
        }

        if (_stream is null)
        {
            AddResult(CreateFailedResult(
                responseId,
                "MissingPushTextToSpeechStream",
                "Push-text TTS route was selected, but no push-text stream was opened.",
                TtsSynthesisDisposition.Unsupported));
            return CreateCompletion(responseId);
        }

        try
        {
            await _stream.PushTextAsync(
                new PushTextToSpeechInput
                {
                    ResponseId = responseId,
                    Text = string.Empty,
                    SourceTextStart = _generatedTextLength,
                    IsFinalInput = true
                },
                cancellationToken).ConfigureAwait(false);
            await PublishPushTextInputSentAsync(
                responseId,
                _generatedTextLength,
                0,
                isFinalInput: true,
                cancellationToken).ConfigureAwait(false);
            await _stream.CompleteInputAsync(cancellationToken).ConfigureAwait(false);

            if (_reader is not null)
            {
                await _reader.ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && CanFallbackToSegments())
        {
            await ActivateSegmentFallbackAsync(cancellationToken).ConfigureAwait(false);
            return await _fallbackEngine!.CompleteAsync(responseId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = CreateFailedResult(
                responseId,
                "PushTextTtsFailed",
                ex.Message,
                TtsSynthesisDisposition.Failed);
            AddResult(failed);
            await PublishSegmentEventAsync(
                failed,
                failed.SegmentId ?? new OutputSegmentId($"{OutputFlow.Id.Value}:push-text-0001"),
                failed.SegmentIndex ?? 0,
                isFinalSegment: true,
                cancellationToken).ConfigureAwait(false);
            return CreateCompletion(responseId);
        }

        await _stream.DisposeAsync().ConfigureAwait(false);
        _stream = null;

        if (!_firstOutputChunkEmitted && CanFallbackToSegments())
        {
            await ActivateSegmentFallbackAsync(cancellationToken).ConfigureAwait(false);
            return await _fallbackEngine!.CompleteAsync(responseId, cancellationToken)
                .ConfigureAwait(false);
        }

        await CompletePushAudioStreamAsync(responseId, cancellationToken).ConfigureAwait(false);

        var result = await StoreSynthesizedStreamAsync(responseId, cancellationToken)
            .ConfigureAwait(false);
        AddResult(result);
        await PublishSegmentEventAsync(
            result,
            result.SegmentId ?? new OutputSegmentId($"{OutputFlow.Id.Value}:audio-0001"),
            result.SegmentIndex ?? 0,
            isFinalSegment: true,
            cancellationToken).ConfigureAwait(false);
        return CreateCompletion(responseId);
    }

    public override void Cancel(Exception? exception = null)
    {
        if (_stream is not null)
        {
            _ = _stream.CancelAsync();
        }

        _fallbackEngine?.Cancel(exception);
    }

    private async ValueTask OpenPushStreamAsync(CancellationToken cancellationToken)
    {
        if (Options.OutputOptions is null)
        {
            return;
        }

        var providerKey = ResolveProviderKey(Options.OutputOptions);
        await PublishAsync(new AssistantAudioPushTextStreamOpeningEvent(
            Options.SessionId.Value,
            OutputFlow.Id.Value,
            Options.InitialResponseId.Value,
            providerKey,
            Options.OutputOptions.ModelId,
            Options.OutputOptions.VoiceId,
            Options.OutputOptions.Language,
            Options.OutputOptions.OutputFormat,
            Options.PushTextAggregationMode.ToString()), cancellationToken).ConfigureAwait(false);
        _stream ??= await _streamFactory.OpenStreamAsync(
            new PushTextToSpeechStreamRequest
            {
                SessionId = Options.SessionId,
                OutputFlowId = OutputFlow.Id,
                ResponseId = Options.InitialResponseId,
                ProviderKey = providerKey,
                ModelId = Options.OutputOptions.ModelId,
                VoiceId = Options.OutputOptions.VoiceId,
                Language = Options.OutputOptions.Language,
                OutputFormat = Options.OutputOptions.OutputFormat,
                ContentType = Options.OutputOptions.ContentType,
                Speed = Options.OutputOptions.Speed,
                PacingOptions = Options.PacingOptions,
                InputAggregationMode = Options.PushTextAggregationMode
            },
            cancellationToken).ConfigureAwait(false);
        await PublishAsync(new AssistantAudioPushTextStreamOpenedEvent(
            Options.SessionId.Value,
            OutputFlow.Id.Value,
            Options.InitialResponseId.Value,
            providerKey,
            Options.OutputOptions.ModelId,
            Options.OutputOptions.VoiceId,
            Options.OutputOptions.Language,
            Options.OutputOptions.OutputFormat,
            Options.PushTextAggregationMode.ToString()), cancellationToken).ConfigureAwait(false);
        _reader ??= Task.Run(() => ReadAudioAsync(_stream, cancellationToken), cancellationToken);
    }

    private ValueTask PublishPushTextInputSentAsync(
        ResponseId responseId,
        int sourceTextStart,
        int sourceTextLength,
        bool isFinalInput,
        CancellationToken cancellationToken)
    {
        return PublishAsync(new AssistantAudioPushTextInputSentEvent(
            Options.SessionId.Value,
            OutputFlow.Id.Value,
            responseId.Value,
            sourceTextStart,
            sourceTextLength,
            isFinalInput,
            Options.PushTextAggregationMode.ToString()), cancellationToken);
    }

    private async ValueTask ActivateSegmentFallbackAsync(CancellationToken cancellationToken)
    {
        if (_fallbackEngine is not null)
        {
            return;
        }

        if (_stream is not null)
        {
            await _stream.CancelAsync(cancellationToken).ConfigureAwait(false);
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _fallbackEngine = new SegmentTextToSpeechEngine(
            Options,
            OutputFlow,
            new SentenceTtsPacer(),
            new TextToSpeechTextSanitizer(),
            new TextToSpeechSegmentSynthesizer(),
            appendGeneratedText: false,
            emitStartedEvent: false);
        await _fallbackEngine.StartAsync(cancellationToken).ConfigureAwait(false);
        foreach (var delta in _textDeltas)
        {
            await _fallbackEngine.PushTextAsync(delta.Text, delta.ResponseId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private bool CanFallbackToSegments() =>
        Options.RouteMode == ProgressiveTextToSpeechRouteMode.Auto &&
        !_firstOutputChunkEmitted;

    private async Task ReadAudioAsync(
        IPushTextToSpeechStream stream,
        CancellationToken cancellationToken)
    {
        await foreach (var update in stream.ReadAudioAsync(cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            _modelId = FirstNonWhiteSpace(update.ModelId, _modelId);
            if (!update.AudioData.IsEmpty)
            {
                _providerFirstAudioAt ??= DateTimeOffset.UtcNow;
                _mediaType = FirstNonWhiteSpace(_mediaType, update.MediaType);
                var mediaType = FirstNonWhiteSpace(_mediaType, update.MediaType) ?? "application/octet-stream";
                var audioData = update.AudioData.ToArray();
                var sequence = _chunkSequence++;
                var observedAt = DateTimeOffset.UtcNow;
                var payload = OutputAudioPayloadFactory.Create(
                    audioData,
                    mediaType,
                    Options.OutputOptions?.OutputFormat,
                    sequence,
                    observedAt);
                _streamMediaType ??= payload.MediaType;
                _audioBuffer.Write(audioData);
                await EnsurePushAudioStreamStartedAsync(payload.MediaType, payload.Kind, cancellationToken)
                    .ConfigureAwait(false);
                var chunk = new OutputAudioChunk
                {
                    OutputFlowId = OutputFlow.Id,
                    ResponseId = _responseId,
                    SegmentId = _segmentId,
                    SegmentIndex = 0,
                    Sequence = sequence,
                    Payload = payload,
                    ObservedAt = observedAt,
                    IsFinalChunk = false
                };
                _firstOutputChunkEmitted = true;
                if (chunk.Duration is { } chunkDuration)
                {
                    _audioDuration += chunkDuration;
                }
                await OutputFlow.AppendAudioChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (_audioSinkAccepted)
                {
                    await Options.OutputSink!.WriteAsync(chunk, cancellationToken)
                        .ConfigureAwait(false);
                }
                await PublishAsync(new AssistantAudioOutputChunkReadyEvent(
                    Options.SessionId.Value,
                    OutputFlow.Id.Value,
                    _responseId.Value,
                    _segmentId.Value,
                    0,
                    chunk.Sequence,
                    ResolveProviderKey(Options.OutputOptions),
                    FirstNonWhiteSpace(_modelId, Options.OutputOptions?.ModelId),
                    Options.OutputOptions?.VoiceId,
                    Options.OutputOptions?.Language,
                    Options.OutputOptions?.OutputFormat,
                    chunk.MediaType,
                    chunk.SizeBytes,
                    chunk.Duration,
                    chunk.IsFinalChunk,
                    chunk.Payload.Kind.ToString()), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask EnsurePushAudioStreamStartedAsync(
        string mediaType,
        OutputAudioPayloadKind payloadKind,
        CancellationToken cancellationToken)
    {
        if (_audioStreamStarted)
        {
            return;
        }

        _audioStreamStarted = true;
        var stream = new OutputAudioStream
        {
            SessionId = Options.SessionId.Value,
            OutputFlowId = OutputFlow.Id,
            ResponseId = _responseId,
            SegmentId = _segmentId,
            SegmentIndex = 0,
            IsFinalSegment = true,
            SourceTextStart = 0,
            SourceTextLength = _generatedTextLength,
            ProviderKey = ResolveProviderKey(Options.OutputOptions),
            ModelId = FirstNonWhiteSpace(_modelId, Options.OutputOptions?.ModelId),
            VoiceId = Options.OutputOptions?.VoiceId,
            Language = Options.OutputOptions?.Language,
            OutputFormat = Options.OutputOptions?.OutputFormat,
            MediaType = mediaType,
            PayloadKind = payloadKind,
            StartedAt = DateTimeOffset.UtcNow
        };
        await OutputFlow.StartAudioStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        if (Options.EnablePlayback && Options.OutputSink is not null)
        {
            var result = await Options.OutputSink.StartAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            _audioSinkAccepted = result.Disposition == OutputSinkStartDisposition.Accepted;
            AddPlaybackStartFailure(result);
        }
        await PublishAsync(new AssistantAudioOutputStreamStartedEvent(
            Options.SessionId.Value,
            OutputFlow.Id.Value,
            _responseId.Value,
            _segmentId.Value,
            0,
            ResolveProviderKey(Options.OutputOptions),
            FirstNonWhiteSpace(_modelId, Options.OutputOptions?.ModelId),
            Options.OutputOptions?.VoiceId,
            Options.OutputOptions?.Language,
            Options.OutputOptions?.OutputFormat,
            mediaType,
            payloadKind.ToString()), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask CompletePushAudioStreamAsync(
        ResponseId responseId,
        CancellationToken cancellationToken)
    {
        if (!_audioStreamStarted || _audioStreamCompleted)
        {
            return;
        }

        _audioStreamCompleted = true;
        var completion = new OutputAudioStreamCompletion
        {
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            SegmentId = _segmentId,
            SegmentIndex = 0,
            Disposition = OutputAudioStreamDisposition.Completed,
            ChunkCount = _chunkSequence,
            SizeBytes = _audioBuffer.Length,
            Duration = _audioDuration > TimeSpan.Zero ? _audioDuration : null,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await OutputFlow.CompleteAudioStreamAsync(completion, cancellationToken).ConfigureAwait(false);
        if (_audioSinkAccepted)
        {
            await Options.OutputSink!.CompleteAsync(completion, cancellationToken)
                .ConfigureAwait(false);
        }
        await PublishAsync(new AssistantAudioOutputStreamCompletedEvent(
            Options.SessionId.Value,
            OutputFlow.Id.Value,
            responseId.Value,
            _segmentId.Value,
            0,
            OutputAudioStreamDisposition.Completed.ToString(),
            _chunkSequence,
            _audioBuffer.Length,
            completion.Duration), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<AssistantTextToSpeechOutputResult> StoreSynthesizedStreamAsync(
        ResponseId responseId,
        CancellationToken cancellationToken)
    {
        var request = CreateTextToSpeechRequest(responseId, _segmentId);
        var ledger = new List<RealtimeLedgerRecord>();
        var trace = new List<RealtimeAudioTraceRecord>();
        var providerKey = ResolveProviderKey(Options.OutputOptions);
        var modelId = FirstNonWhiteSpace(_modelId, Options.OutputOptions?.ModelId);

        LedgerTraceWriter.AppendTtsRequested(
            ledger,
            trace,
            Options.SessionId,
            CreateCorrelation(),
            OutputFlow.Id,
            request,
            providerKey);

        if (RequiresContentStoreArtifact(Options.OutputOptions) && Options.OutputOptions?.ContentStore is null)
        {
            var missingStore = new AudioErrorInfo
            {
                Code = "MissingContentStore",
                Message = "Push-text assistant TTS output synthesis requires IContentStore; no content store is configured.",
                Category = "TextToSpeech",
                IsRetryable = false
            };
            LedgerTraceWriter.AppendTtsResult(
                ledger,
                trace,
                Options.SessionId,
                CreateCorrelation(),
                OutputFlow.Id,
                responseId,
                request,
                providerKey,
                modelId,
                Options.OutputOptions!,
                TtsSynthesisDisposition.Failed,
                null,
                null,
                null,
                missingStore);
            return FailedOutputResult(responseId, _segmentId, request.Text, missingStore, ledger, trace);
        }

        if (_audioBuffer.Length == 0)
        {
            var missingAudio = new AudioErrorInfo
            {
                Code = "PushTextTtsNoAudio",
                Message = "Push-text text-to-speech stream completed without audio data.",
                Category = "TextToSpeech",
                IsRetryable = false
            };
            LedgerTraceWriter.AppendTtsResult(
                ledger,
                trace,
                Options.SessionId,
                CreateCorrelation(),
                OutputFlow.Id,
                responseId,
                request,
                providerKey,
                modelId,
                Options.OutputOptions,
                TtsSynthesisDisposition.Failed,
                null,
                null,
                null,
                missingAudio);
            return FailedOutputResult(responseId, _segmentId, request.Text, missingAudio, ledger, trace);
        }

        var mediaType = FirstNonWhiteSpace(
                _streamMediaType,
                _mediaType,
                request.ContentType,
                OutputArtifactWriter.ToMediaType(request.OutputFormat))
            ?? "application/octet-stream";
        LedgerTraceWriter.AppendTtsResult(
            ledger,
            trace,
            Options.SessionId,
            CreateCorrelation(),
            OutputFlow.Id,
            responseId,
            request,
            providerKey,
            modelId,
            Options.OutputOptions,
            TtsSynthesisDisposition.Synthesized,
            mediaType,
            _audioBuffer.Length,
            _audioDuration > TimeSpan.Zero ? _audioDuration : null,
            null,
            _providerFirstAudioAt);

        if (RequiresContentStoreArtifact(Options.OutputOptions))
        {
            var artifact = await _artifactWriter.WriteAssistantAudioArtifactAsync(
                Options.OutputOptions!.ContentStore!,
                Options.SessionId,
                OutputFlow.Id,
                responseId,
                providerKey,
                modelId,
                Options.OutputOptions,
                mediaType,
                _audioBuffer.ToArray(),
                cancellationToken).ConfigureAwait(false);
            await OutputFlow.AttachAudioArtifactAsync(new OutputAudioArtifact
            {
                OutputFlowId = OutputFlow.Id,
                ResponseId = responseId,
                SegmentId = _segmentId,
                SegmentIndex = 0,
                Artifact = artifact.Artifact,
                MediaType = mediaType,
                SizeBytes = artifact.SizeBytes,
                Sha256 = artifact.Sha256,
                Duration = _audioDuration > TimeSpan.Zero ? _audioDuration : null,
                CapturedAt = DateTimeOffset.UtcNow
            }, cancellationToken).ConfigureAwait(false);
            await PublishAsync(new AssistantAudioOutputArtifactCapturedEvent(
                Options.SessionId.Value,
                OutputFlow.Id.Value,
                responseId.Value,
                _segmentId.Value,
                0,
                mediaType,
                artifact.Artifact,
                artifact.SizeBytes,
                artifact.Sha256,
                _audioDuration > TimeSpan.Zero ? _audioDuration : null), cancellationToken).ConfigureAwait(false);
            LedgerTraceWriter.AppendOutputArtifact(
                ledger,
                trace,
                Options.SessionId,
                CreateCorrelation(),
                OutputFlow.Id,
                responseId,
                request,
                artifact);
        }

        return new AssistantTextToSpeechOutputResult
        {
            SessionId = Options.SessionId,
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Status = AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed,
            SegmentId = _segmentId,
            SegmentIndex = 0,
            Text = request.Text,
            MediaType = mediaType,
            Ledger = ledger,
            Trace = trace
        };
    }

    private TextToSpeechSegmentRequest CreateTextToSpeechRequest(
        ResponseId responseId,
        OutputSegmentId segmentId)
    {
        return new TextToSpeechSegmentRequest
        {
            ResponseId = responseId,
            Text = OutputFlow.Snapshot.Text,
            SegmentId = segmentId,
            SegmentIndex = 0,
            IsFinalSegment = true,
            SourceTextStart = 0,
            SourceTextLength = _generatedTextLength,
            ProviderKey = ResolveProviderKey(Options.OutputOptions),
            ModelId = Options.OutputOptions?.ModelId,
            VoiceId = Options.OutputOptions?.VoiceId,
            Language = Options.OutputOptions?.Language,
            OutputFormat = Options.OutputOptions?.OutputFormat,
            ContentType = Options.OutputOptions?.ContentType
        };
    }

    private AssistantTextToSpeechOutputResult CreateFailedResult(
        ResponseId responseId,
        string code,
        string message,
        TtsSynthesisDisposition disposition)
    {
        var request = CreateTextToSpeechRequest(responseId, _segmentId);
        var ledger = new List<RealtimeLedgerRecord>();
        var trace = new List<RealtimeAudioTraceRecord>();
        var providerKey = ResolveProviderKey(Options.OutputOptions);
        var error = new AudioErrorInfo
        {
            Code = code,
            Message = message,
            Category = "TextToSpeech",
            IsRetryable = false
        };

        if (Options.OutputOptions is not null)
        {
            LedgerTraceWriter.AppendTtsResult(
                ledger,
                trace,
                Options.SessionId,
                CreateCorrelation(),
                OutputFlow.Id,
                responseId,
                request,
                providerKey,
                Options.OutputOptions.ModelId,
                Options.OutputOptions,
                disposition,
                null,
                null,
                null,
                error);
        }

        return FailedOutputResult(responseId, _segmentId, request.Text, error, ledger, trace);
    }

    private AssistantTextToSpeechOutputResult FailedOutputResult(
        ResponseId responseId,
        OutputSegmentId segmentId,
        string text,
        AudioErrorInfo error,
        IReadOnlyList<RealtimeLedgerRecord> ledger,
        IReadOnlyList<RealtimeAudioTraceRecord> trace)
    {
        return new AssistantTextToSpeechOutputResult
        {
            SessionId = Options.SessionId,
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Status = AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Text = text,
            Error = error,
            Ledger = ledger,
            Trace = trace
        };
    }

    private ProgressiveTextToSpeechEngineCompletion CreateCompletion(ResponseId responseId)
    {
        return new ProgressiveTextToSpeechEngineCompletion
        {
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Results = SnapshotResults(),
            PlaybackStartFailures = SnapshotPlaybackStartFailures()
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

    private static bool RequiresContentStoreArtifact(AssistantTextToSpeechOutputOptions? options) =>
        options?.ArtifactCapturePolicy == AssistantAudioArtifactCapturePolicy.ContentStoreArtifact;
}

internal sealed class UnsupportedPushTextToSpeechEngine : ProgressiveTextToSpeechEngineBase
{
    private ResponseId _responseId;
    private int _generatedTextLength;
    private readonly TextToSpeechCapabilityProfile _profile;
    private readonly bool _hasFactory;

    public UnsupportedPushTextToSpeechEngine(
        S6ProgressiveOutputParticipantOptionsV2 options,
        IOutputProjectionSinkV2 outputFlow,
        TextToSpeechCapabilityProfile profile,
        bool hasFactory)
        : base(options, outputFlow)
    {
        _responseId = options.InitialResponseId;
        _profile = profile;
        _hasFactory = hasFactory;
    }

    public override ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask PushTextAsync(
        string textDelta,
        ResponseId responseId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(textDelta);
        cancellationToken.ThrowIfCancellationRequested();

        if (textDelta.Length == 0)
        {
            return;
        }

        _responseId = responseId;
        _generatedTextLength += textDelta.Length;
        await OutputFlow.AppendTextAsync(responseId, textDelta, isFinal: false, cancellationToken)
            .ConfigureAwait(false);
        await EnsureStartedAsync(responseId, cancellationToken).ConfigureAwait(false);
    }

    public override ValueTask<ProgressiveTextToSpeechEngineCompletion> CompleteAsync(
        ResponseId responseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _responseId = responseId;
        var segmentId = new OutputSegmentId($"{OutputFlow.Id.Value}:push-text-0001");
        var reason = !_profile.SupportsPushTextAudioStreaming
            ? "The configured text-to-speech client does not advertise push-text input support."
            : "The configured text-to-speech client advertises push-text input but does not expose IPushTextToSpeechStreamFactory.";
        if (_hasFactory && !_profile.SupportsPushTextAudioStreaming)
        {
            reason = "The configured text-to-speech client exposes IPushTextToSpeechStreamFactory but does not advertise push-text input support.";
        }

        AddResult(new AssistantTextToSpeechOutputResult
        {
            SessionId = Options.SessionId,
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Status = AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly,
            SegmentId = segmentId,
            SegmentIndex = 0,
            Text = OutputFlow.Snapshot.Text,
            Error = new AudioErrorInfo
            {
                Code = "PushTextTtsUnsupported",
                Message = reason,
                Category = "TextToSpeech",
                IsRetryable = false
            },
            Ledger = [],
            Trace = []
        });

        return ValueTask.FromResult(new ProgressiveTextToSpeechEngineCompletion
        {
            OutputFlowId = OutputFlow.Id,
            ResponseId = responseId,
            Results = SnapshotResults(),
            PlaybackStartFailures = SnapshotPlaybackStartFailures()
        });
    }

    public override void Cancel(Exception? exception = null)
    {
    }
}

internal sealed record ProgressiveTextDelta(
    string Text,
    ResponseId ResponseId,
    int GeneratedTextLength);
