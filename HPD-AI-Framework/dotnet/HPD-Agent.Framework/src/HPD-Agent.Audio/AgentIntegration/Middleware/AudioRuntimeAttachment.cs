using HPD.Agent.Audio.AgentIntegration.Detection;
using HPD.Agent.Audio.AgentIntegration.Input;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.AgentIntegration.SourceResolution;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Audio.Runtime.Ledger;
using HPD.Agent.Audio.Runtime.Output;
using HPD.Agent.Audio.Runtime.Scenarios;
using HPD.Agent.Audio.Runtime.Trace;
using HPD.Agent.Audio.Trace;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Audio.AgentIntegration.Middleware;

#pragma warning disable MEAI001

public sealed class AudioRuntimeAttachment : IAgentMiddleware
{
    public const string AudioInteractionInputsMetadataKey = "hpd.audio.inputs";
    public const string AudioInteractionRuntimeResultsKey = "hpd.audio.interactionRuntime";
    public const string AssistantOutputRuntimeResultsKey = "hpd.audio.assistantOutputRuntime";

    private readonly InputContentDetector _detector;
    private readonly AudioInteractionRuntimeRunner _runner;
    private readonly AssistantTextExtractor _assistantTextExtractor;
    private readonly AssistantFinalTextToSpeechOutputService _assistantOutputService;
    private readonly AudioRuntimeAttachmentOptions _options;
    private readonly object _outputGate = new();
    private readonly HashSet<string> _progressiveSynthesizedResponseIds = new(StringComparer.Ordinal);
    private AudioInteractionRuntimeResult[] _lastResults = [];
    private AssistantTextToSpeechOutputResult[] _lastOutputResults = [];
    private RealtimeLedgerRecord[] _lastOutputLedger = [];
    private RealtimeAudioTraceRecord[] _lastOutputTrace = [];

    public AudioRuntimeAttachment()
        : this(new AudioRuntimeAttachmentOptions())
    {
    }

    public AudioRuntimeAttachment(AudioRuntimeAttachmentOptions options)
        : this(options, new InputContentDetector(), new AudioInteractionRuntimeRunner())
    {
    }

    public AudioRuntimeAttachment(
        AudioRuntimeAttachmentOptions options,
        InputContentDetector detector,
        AudioInteractionRuntimeRunner runner)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _assistantTextExtractor = new AssistantTextExtractor();
        _assistantOutputService = new AssistantFinalTextToSpeechOutputService();
    }

    public IReadOnlyList<AudioInteractionRuntimeResult> LastResults => _lastResults;

    public IReadOnlyList<AssistantTextToSpeechOutputResult> LastOutputResults => _lastOutputResults;

    public IReadOnlyList<RealtimeLedgerRecord> LastOutputLedger => _lastOutputLedger;

    public IReadOnlyList<RealtimeAudioTraceRecord> LastOutputTrace => _lastOutputTrace;

    private AudioRuntimeAttachmentOptions EffectiveOptions(AgentRunConfig? runConfig)
        => AudioRuntimeOptionsCompiler.Compile(_options, runAudio: runConfig?.Audio);

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        lock (_outputGate)
        {
            _progressiveSynthesizedResponseIds.Clear();
            _lastResults = [];
            _lastOutputResults = [];
            _lastOutputLedger = [];
            _lastOutputTrace = [];
        }

        var options = EffectiveOptions(context.RunConfig);

        if (!options.Enabled || context.UserMessage is null)
        {
            return;
        }

        var detections = _detector.Detect(context.UserMessage);
        if (detections.Count == 0)
        {
            return;
        }

        if (IsRealtimeTransport(context))
        {
            context.UserMessage = RealtimeInputAudioPreparer.PrepareMessage(context.UserMessage);
        }

        var sourceResolver = new AgentInputContentSourceResolver(
            detections,
            context.ContentStore,
            context.SessionId ?? context.ConversationId);
        if (!context.RuntimeCapabilities.IsSealed)
        {
            context.RuntimeCapabilities.Set<IInputContentSourceResolver>(sourceResolver);
        }

        var interactionSessionFactory = ResolveInteractionSessionFactory(
            options,
            sourceResolver,
            context.ClientSet,
            out var ownsInteractionSessionFactory);
        var providerRoute = ResolveProviderRoute(
            options,
            sourceResolver,
            out var ownsProviderRoute);

        var sessionId = new AudioSessionId(
            context.SessionId
            ?? context.ConversationId
            ?? context.TraceId
            ?? $"audio-session-{Guid.NewGuid():N}");

        var branchRef = new BranchRef(
            context.SessionId ?? context.ConversationId ?? "session",
            context.BranchId ?? "main");

        var results = new List<AudioInteractionRuntimeResult>();
        try
        {
            if (ShouldRunAudioInteractionRuntime(context, options))
            {
                var result = await _runner.RunAsync(new AudioInteractionRuntimeRequest
                {
                    SessionId = sessionId,
                    Inputs = context.UserMessage.Contents.ToArray(),
                    InputContentRefs = detections
                        .Select(detection => detection.InputContent)
                        .ToArray(),
                    BranchRef = branchRef,
                    RequestId = context.TraceId,
                    PolicySet = options.PolicySet,
                    ProviderRoute = providerRoute,
                    ProviderCandidates = options.ProviderCandidates,
                    InteractionSessionFactory = interactionSessionFactory,
                    BranchProjectionSink = options.BranchProjectionSink
                }, cancellationToken).ConfigureAwait(false);

                results.Add(result);
            }
        }
        finally
        {
            if (ownsProviderRoute && providerRoute is not null)
            {
                await providerRoute.DisposeAsync().ConfigureAwait(false);
            }

            if (ownsInteractionSessionFactory)
            {
                await DisposeInteractionSessionFactoryAsync(interactionSessionFactory)
                    .ConfigureAwait(false);
            }
        }

        _lastResults = [.. results];

        if (options.AnnotateAudioInputMetadata ||
            options.ProjectCommittedTranscriptsIntoUserMessage)
        {
            context.UserMessage = AnnotateMessage(
                context.UserMessage,
                detections,
                results,
                options.AnnotateAudioInputMetadata,
                options.ProjectCommittedTranscriptsIntoUserMessage);
        }
    }

    private static bool ShouldRunAudioInteractionRuntime(
        BeforeMessageTurnContext context,
        AudioRuntimeAttachmentOptions options)
    {
        if (!options.RunAudioInteractionRuntime)
        {
            return false;
        }

        return !IsRealtimeTransport(context) ||
            options.RunAudioInteractionRuntimeForRealtimeTransport;
    }

    private static bool IsRealtimeTransport(BeforeMessageTurnContext context)
        => context.RunConfig?.ModelTransport is AgentModelTransportMode.Realtime;

    public async Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var options = EffectiveOptions(context.RunConfig);

        if (!options.Enabled || EffectiveOutputSynthesisMode(options) is AssistantOutputSynthesisMode.Disabled)
        {
            return;
        }

        var text = _assistantTextExtractor.Extract(context);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var sessionId = new AudioSessionId(
            context.SessionId
            ?? context.ConversationId
            ?? context.TraceId
            ?? $"audio-session-{Guid.NewGuid():N}");
        var branchRef = new BranchRef(
            context.SessionId ?? context.ConversationId ?? "session",
            context.BranchId ?? "main");
        var responseId = new ResponseId(
            context.FinalResponse.ResponseId
            ?? $"response-{Guid.NewGuid():N}");

        if (ShouldSkipFinalSynthesis(responseId, options))
        {
            return;
        }

        var outputOptions = ResolveAssistantOutputOptions(options, context.Services, context.ClientSet, context.ContentStore);
        var result = await _assistantOutputService.RunAsync(
            new AssistantFinalTextToSpeechOutputRequest
            {
                SessionId = sessionId,
                Branch = branchRef,
                Text = text,
                RequestId = context.TraceId,
                ResponseId = responseId,
                Options = outputOptions,
                EmitEvent = agentEvent => context.TryEmit(agentEvent)
            },
            cancellationToken).ConfigureAwait(false);

        SetLastOutputResults([result]);
        EmitAssistantOutputEvent(context, result);
    }

    public IAsyncEnumerable<AgentModelUpdate>? WrapModelTurnStreamingAsync(
        AgentModelTurnRequest request,
        Func<AgentModelTurnRequest, IAsyncEnumerable<AgentModelUpdate>> handler,
        CancellationToken cancellationToken)
    {
        var options = EffectiveOptions(request.RunConfig);
        var mode = EffectiveOutputSynthesisMode(options);
        return mode is AssistantOutputSynthesisMode.Progressive or AssistantOutputSynthesisMode.ProgressiveWithFinalFallback
            ? WrapProgressiveOutputAsync(request, options, handler, cancellationToken)
            : null;
    }

    private async IAsyncEnumerable<AgentModelUpdate> WrapProgressiveOutputAsync(
        AgentModelTurnRequest request,
        AudioRuntimeAttachmentOptions options,
        Func<AgentModelTurnRequest, IAsyncEnumerable<AgentModelUpdate>> handler,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionId = new AudioSessionId(
            request.Session?.Id
            ?? request.State.ConversationId
            ?? request.State.RunId
            ?? $"audio-session-{Guid.NewGuid():N}");
        var branchRef = new BranchRef(sessionId.Value, "main");
        var responseId = new ResponseId($"response-{Guid.NewGuid():N}");
        var outputFlowId = new OutputFlowId($"output-{Guid.NewGuid():N}");
        var outputOptions = ResolveAssistantOutputOptions(options, null, request.ClientSet, request.ContentStore);
        var eventFlowHandle = options.EnableAssistantOutputPlayback
            ? request.EventFlows?.Create(outputFlowId.Value)
            : null;
        var coordinator = new ProgressiveOutputCoordinator(new ProgressiveOutputCoordinatorOptions
        {
            SessionId = sessionId,
            Branch = branchRef,
            OutputFlowId = outputFlowId,
            InitialResponseId = responseId,
            PacingOptions = options.AssistantOutputPacingOptions,
            OutputOptions = outputOptions,
            RouteMode = options.AssistantOutputProgressiveRouteMode,
            PushTextAggregationMode = options.AssistantOutputPushTextAggregationMode,
            RequestId = request.State.RunId,
            EmitEvent = agentEvent => request.EventCoordinator?.Emit(agentEvent),
            OutputSink = options.AssistantAudioOutputSink,
            EnablePlayback = options.EnableAssistantOutputPlayback,
            EventFlowHandle = eventFlowHandle,
            StructEvents = request.StructEvents,
            CaptureStructEventSamplesInTrace = options.PolicySet.Trace.CaptureStructEventSamples
        });
        coordinator.Start(cancellationToken);

        await foreach (var modelUpdate in handler(request).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var responseIdValue = ExtractResponseId(modelUpdate);
            if (!string.IsNullOrWhiteSpace(responseIdValue))
            {
                responseId = new ResponseId(responseIdValue);
            }

            yield return modelUpdate;

            var delta = ExtractTextDelta(modelUpdate);
            if (!string.IsNullOrEmpty(delta))
            {
                await coordinator.WriteTextDeltaAsync(delta, responseId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var completion = await coordinator.CompleteInputAsync(responseId, cancellationToken)
            .ConfigureAwait(false);
        eventFlowHandle?.Complete();
        if (completion.Results.Count > 0)
        {
            var ledger = completion.Ledger.ToList();
            var trace = completion.Trace.ToList();
            await ProjectCommittedAssistantOutputAsync(
                options,
                completion,
                branchRef,
                new AudioTurnId(request.State.RunId ?? outputFlowId.Value),
                ledger,
                trace,
                cancellationToken).ConfigureAwait(false);

            AppendLastOutputResults(completion.Results);
            AppendLastOutputEvidence(ledger, trace);
            foreach (var result in completion.Results)
            {
                TrackProgressiveSynthesis(responseId, result);
            }
        }
    }

    private static void EmitAssistantOutputEvent(
        AfterMessageTurnContext context,
        AssistantTextToSpeechOutputResult result)
    {
        switch (result.Status)
        {
            case AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly:
            {
                var synthesis = result.Trace
                    .OfType<AudioTtsSynthesisTraceRecord>()
                    .LastOrDefault(t => t.Disposition == TtsSynthesisDisposition.Failed);

                context.TryEmit(new AssistantAudioOutputFailedEvent(
                    result.SessionId.Value,
                    result.OutputFlowId.Value,
                    result.ResponseId.Value,
                    synthesis?.ProviderKey ?? "unknown",
                    synthesis?.ModelId,
                    synthesis?.VoiceId,
                    synthesis?.Language,
                    synthesis?.OutputFormat,
                    result.Error ?? synthesis?.Error,
                    result.Status.ToString()));
                break;
            }
        }
    }

    private IAudioInteractionSessionFactory? ResolveInteractionSessionFactory(
        AudioRuntimeAttachmentOptions options,
        IInputContentSourceResolver sourceResolver,
        AgentClientSet? clientSet,
        out bool ownsFactory)
    {
        if (options.InteractionSessionFactory is not null)
        {
            ownsFactory = false;
            return options.InteractionSessionFactory;
        }

        var factory = options.InteractionSessionFactoryResolver?.Invoke(sourceResolver);
        if (factory is not null)
        {
            ownsFactory = true;
            return factory;
        }

        ownsFactory = false;
        return null;
    }

    private IProviderRoute? ResolveProviderRoute(
        AudioRuntimeAttachmentOptions options,
        IInputContentSourceResolver sourceResolver,
        out bool ownsRoute)
    {
        if (options.ProviderRoute is not null)
        {
            ownsRoute = false;
            return options.ProviderRoute;
        }

        var route = options.ProviderRouteResolver?.Invoke(sourceResolver);
        ownsRoute = route is not null;
        return route;
    }

    private static async ValueTask DisposeInteractionSessionFactoryAsync(
        IAudioInteractionSessionFactory? factory)
    {
        switch (factory)
        {
            case null:
                return;
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            case IDisposable disposable:
                disposable.Dispose();
                return;
        }
    }

    private AssistantTextToSpeechOutputOptions? ResolveAssistantOutputOptions(
        AudioRuntimeAttachmentOptions options,
        IServiceProvider? services,
        AgentClientSet? clientSet,
        IContentStore? contentStore)
    {
        var client = options.AssistantOutputTextToSpeechClient
            ?? options.AssistantOutputTextToSpeechClientFactory?.Invoke(services)
            ?? clientSet?.TextToSpeech;
        if (client is null)
        {
            return null;
        }

        return new AssistantTextToSpeechOutputOptions
        {
            TextToSpeechClient = client,
            ContentStore = contentStore,
            ArtifactCapturePolicy = options.AssistantOutputArtifactCapturePolicy,
            ProviderKey = options.AssistantOutputProviderKey,
            ModelId = options.AssistantOutputModelId,
            VoiceId = options.AssistantOutputVoiceId,
            Language = options.AssistantOutputLanguage,
            OutputFormat = options.AssistantOutputFormat,
            ContentType = options.AssistantOutputContentType,
            Speed = options.AssistantOutputSpeed
        };
    }

    private static AssistantOutputSynthesisMode EffectiveOutputSynthesisMode(AudioRuntimeAttachmentOptions options)
        => options.AssistantOutputSynthesisMode;

    private bool ShouldSkipFinalSynthesis(ResponseId responseId, AudioRuntimeAttachmentOptions options)
    {
        var mode = EffectiveOutputSynthesisMode(options);
        if (mode == AssistantOutputSynthesisMode.Progressive)
        {
            return true;
        }

        if (mode != AssistantOutputSynthesisMode.ProgressiveWithFinalFallback)
        {
            return false;
        }

        lock (_outputGate)
        {
            return _progressiveSynthesizedResponseIds.Contains(responseId.Value);
        }
    }

    private void TrackProgressiveSynthesis(
        ResponseId responseId,
        AssistantTextToSpeechOutputResult result)
    {
        if (result.Status != AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed)
        {
            return;
        }

        lock (_outputGate)
        {
            _progressiveSynthesizedResponseIds.Add(responseId.Value);
        }
    }

    private void SetLastOutputResults(IReadOnlyList<AssistantTextToSpeechOutputResult> results)
    {
        lock (_outputGate)
        {
            _lastOutputResults = [.. results];
        }
    }

    private void AppendLastOutputResults(IReadOnlyList<AssistantTextToSpeechOutputResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        lock (_outputGate)
        {
            _lastOutputResults = [.. _lastOutputResults, .. results];
        }
    }

    private void AppendLastOutputEvidence(
        IReadOnlyList<RealtimeLedgerRecord> ledger,
        IReadOnlyList<RealtimeAudioTraceRecord> trace)
    {
        if (ledger.Count == 0 && trace.Count == 0)
        {
            return;
        }

        lock (_outputGate)
        {
            _lastOutputLedger = [.. _lastOutputLedger, .. ledger];
            _lastOutputTrace = [.. _lastOutputTrace, .. trace];
        }
    }

    private async ValueTask ProjectCommittedAssistantOutputAsync(
        AudioRuntimeAttachmentOptions options,
        ProgressiveOutputCompletion completion,
        BranchRef branchRef,
        AudioTurnId turnId,
        List<RealtimeLedgerRecord> ledger,
        List<RealtimeAudioTraceRecord> trace,
        CancellationToken cancellationToken)
    {
        if (options.BranchProjectionSink is null ||
            !options.PolicySet.BranchProjection.ProjectCommittedAssistantOutputs ||
            completion.Commit is not { } commit ||
            !ShouldProjectAssistantOutput(commit) ||
            string.IsNullOrWhiteSpace(commit.Text))
        {
            return;
        }

        var projection = new BranchProjectionRecord
        {
            TurnId = turnId,
            Text = commit.Text,
            Kind = BranchProjectionKind.AssistantOutput,
            Role = BranchProjectionRole.Assistant,
            OutputFlowId = commit.OutputFlowId,
            ResponseId = commit.ResponseId
        };
        var projectedEvent = await options.BranchProjectionSink
            .ProjectAsync(branchRef, projection, cancellationToken)
            .ConfigureAwait(false);
        var projectionId = new BranchProjectionId($"branch-projection-{Guid.NewGuid():N}");
        var correlation = new AudioCorrelation
        {
            ConversationId = branchRef.SessionId,
            RequestId = turnId.Value,
            SessionId = completion.SessionId,
            OutputFlowId = commit.OutputFlowId
        };

        ledger.Add(new BranchProjectionLedgerRecord
        {
            Id = new LedgerRecordId($"ledger-{Guid.NewGuid():N}"),
            SessionId = completion.SessionId,
            Family = LedgerRecordFamily.BranchProjection,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            ProjectionId = projectionId,
            Branch = branchRef,
            Projection = projection,
            ProjectedEvent = projectedEvent
        });
        trace.Add(new AudioBranchProjectionTraceRecord
        {
            Id = new TraceRecordId($"trace-{Guid.NewGuid():N}"),
            SessionId = completion.SessionId,
            Family = RealtimeAudioTraceRecordFamily.BranchProjection,
            RecordedAt = DateTimeOffset.UtcNow,
            Correlation = correlation,
            ProjectionId = projectionId,
            ProjectedEvent = projectedEvent
        });
    }

    private static bool ShouldProjectAssistantOutput(OutputCommitRecord commit)
    {
        return commit.Disposition is OutputCommitDisposition.PlayedComplete
            or OutputCommitDisposition.Interrupted;
    }

    private static string? ExtractResponseId(AgentModelUpdate update)
        => update switch
        {
            AgentTextDeltaUpdate text => text.ResponseId,
            AgentReasoningDeltaUpdate reasoning => reasoning.ResponseId,
            AgentAudioDeltaUpdate audio => audio.ResponseId,
            AgentToolCallUpdate toolCall => toolCall.ResponseId,
            AgentResponseLifecycleUpdate lifecycle => lifecycle.ResponseId,
            _ => update.ChatUpdate?.ResponseId
        };

    private static string ExtractTextDelta(AgentModelUpdate update)
        => update switch
        {
            AgentTextDeltaUpdate text => text.Text,
            AgentChatModelUpdate chat => ExtractTextDelta(chat.Update),
            _ => update.ChatUpdate is null ? string.Empty : ExtractTextDelta(update.ChatUpdate)
        };

    private static string ExtractTextDelta(ChatResponseUpdate update)
    {
        return string.Concat(update.Contents
            .OfType<TextContent>()
            .Select(content => content.Text));
    }

    private static string ResolveProviderKey(AssistantTextToSpeechOutputOptions? options)
    {
        if (!string.IsNullOrWhiteSpace(options?.ProviderKey))
        {
            return options.ProviderKey!;
        }

        var metadata = options?.TextToSpeechClient.GetService(typeof(TextToSpeechClientMetadata)) as TextToSpeechClientMetadata;
        return string.IsNullOrWhiteSpace(metadata?.ProviderName) ? "unknown" : metadata!.ProviderName!;
    }

    private static ChatMessage AnnotateMessage(
        ChatMessage message,
        IReadOnlyList<InputContentDetection> detections,
        IReadOnlyList<AudioInteractionRuntimeResult> results,
        bool annotateInputMetadata,
        bool projectCommittedTranscripts)
    {
        var additionalProperties = message.AdditionalProperties?.Clone()
            ?? [];

        if (annotateInputMetadata)
        {
            additionalProperties[AudioInteractionInputsMetadataKey] = detections
                .Select(d => new AudioInteractionInputMetadata(
                    d.ContentIndex,
                    d.InputContent.Id.Value,
                    d.InputContent.SourceKind.ToString(),
                    d.InputContent.MediaType,
                    d.InputContent.Name,
                    d.InputContent.SizeBytes,
                    d.InputContent.Sha256))
                .ToArray();

            if (results.Count > 0)
            {
                additionalProperties[AudioInteractionRuntimeResultsKey] = results
                    .Select(CreateRuntimeMetadata)
                    .ToArray();
            }
        }

        var contents = projectCommittedTranscripts
            ? AddCommittedTranscripts(
                message.Contents,
                results)
            : message.Contents.ToArray();

        return new ChatMessage(message.Role, contents)
        {
            AuthorName = message.AuthorName,
            AdditionalProperties = additionalProperties,
            CreatedAt = message.CreatedAt,
            MessageId = message.MessageId,
            RawRepresentation = message.RawRepresentation
        };
    }

    private static AudioInteractionRuntimeMetadata CreateRuntimeMetadata(
        AudioInteractionRuntimeResult result)
    {
        var ledger = result.Ledger is InMemoryRealtimeConversationLedger inMemoryLedger
            ? inMemoryLedger.ToArray()
            : [];
        var trace = result.Trace is InMemoryRealtimeAudioTraceStore inMemoryTrace
            ? inMemoryTrace.ToArray()
            : result.Replay.Records;
        var plan = result.RouteDecision?.Plan;
        var assistantOutputTexts = ledger
            .OfType<AssistantOutputLedgerRecord>()
            .Select(record => record.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new AudioInteractionRuntimeMetadata(
            ledger.Count,
            trace.Count,
            ledger.OfType<BranchProjectionLedgerRecord>().Count(),
            result.TurnDecision?.Commit?.Text,
            plan?.RouteEpoch.ProviderKey ?? result.RouteDecision?.Epoch.ProviderKey,
            result.RouteDecision?.Kind.ToString(),
            plan?.Topology.ToString(),
            plan?.ResponseOwnership.ToString(),
            assistantOutputTexts);
    }

    private static AIContent[] AddCommittedTranscripts(
        IList<AIContent> contents,
        IReadOnlyList<AudioInteractionRuntimeResult> results)
    {
        var transcripts = results
            .Select(r => r.TurnDecision?.Commit?.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (transcripts.Length == 0)
        {
            return contents.ToArray();
        }

        var updated = contents.ToList();
        var existingTexts = updated
            .OfType<TextContent>()
            .Select(content => content.Text)
            .ToHashSet(StringComparer.Ordinal);

        var insertIndex = updated.Any(content => content is TextContent) ? updated.Count : 0;
        foreach (var transcript in transcripts)
        {
            if (existingTexts.Add(transcript))
            {
                updated.Insert(insertIndex++, new TextContent(transcript));
            }
        }

        return [.. updated];
    }
}
