using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Audio.Runtime.Thread;
using HPD.Agent.Audio.Runtime.Ledger;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Audio.Runtime.Replay;
using HPD.Agent.Audio.Runtime.Trace;
using HPD.Agent.Audio.Runtime.Transports;
using HPD.Agent.Audio.Runtime.Turns;
using HPD.Agent.Audio.Trace;
using HPD.Agent.Audio.Transports;
using HPD.Agent.Audio.Turns;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Runtime.Scenarios;

public sealed class AudioInteractionRuntimeRunner
{
    private readonly RuntimeClock _clock;
    private readonly RuntimeIdFactory _ids;

    public AudioInteractionRuntimeRunner(RuntimeClock? clock = null, RuntimeIdFactory? ids = null)
    {
        _clock = clock ?? new RuntimeClock();
        _ids = ids ?? new RuntimeIdFactory();
    }

    public async ValueTask<AudioInteractionRuntimeResult> RunAsync(
        AudioInteractionRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ledger = request.Ledger ?? new InMemoryRealtimeConversationLedger();
        var trace = request.Trace ?? new InMemoryRealtimeAudioTraceStore();
        var inMemoryThread = new InMemoryThreadProjectionSink();
        var thread = request.ThreadProjectionSink ?? inMemoryThread;
        var policy = request.PolicySet ?? new AudioPolicySet();
        var correlation = new AudioCorrelation
        {
            SessionId = request.SessionId,
            RequestId = request.RequestId
        };
        var inputMedia = request.InputContentRefs.ToArray();
        var inputBindings = request.InputBindings.Count > 0
            ? request.InputBindings
            : inputMedia
                .Select(content => new TransportBinding
                {
                    Kind = TransportBindingKind.ContentInput,
                    SessionId = request.SessionId,
                    Content = content,
                    Thread = request.ThreadRef,
                    Correlation = correlation
                })
                .ToArray();

        await TraceAsync(trace, new AudioPolicyTraceRecord
        {
            Id = _ids.NextTraceRecordId(),
            SessionId = request.SessionId,
            Family = RealtimeAudioTraceRecordFamily.Policy,
            RecordedAt = _clock.Tick(),
            PolicySet = policy,
            Correlation = correlation
        }, cancellationToken);

        foreach (var inputContent in inputMedia)
        {
            var inputLedger = new InputContentLedgerRecord
            {
                Id = _ids.NextLedgerRecordId(),
                SessionId = request.SessionId,
                Family = LedgerRecordFamily.InputContent,
                RecordedAt = _clock.Tick(),
                Content = inputContent,
                Disposition = InputMediaDisposition.Received,
                Correlation = correlation
            };

            await ledger.AppendAsync(inputLedger, cancellationToken);
            await TraceLedgerAsync(trace, request.SessionId, inputLedger, correlation, cancellationToken);
            await TraceAsync(trace, new AudioInputContentTraceRecord
            {
                Id = _ids.NextTraceRecordId(),
                SessionId = request.SessionId,
                Family = RealtimeAudioTraceRecordFamily.InputContent,
                RecordedAt = _clock.Tick(),
                Content = inputContent,
                Disposition = InputMediaDisposition.Received,
                Correlation = correlation
            }, cancellationToken);
        }

        if (inputMedia.Length == 0)
        {
            return await BuildResultAsync(
                request.SessionId,
                ledger,
                trace,
                thread,
                [],
                null,
                null,
                null,
                cancellationToken);
        }

        if (policy.InputMedia.HandlingMode is InputMediaHandlingMode.Reject)
        {
            foreach (var inputContent in inputMedia)
            {
                await AppendInputDispositionAsync(
                    ledger,
                    trace,
                    request.SessionId,
                    inputContent,
                    InputMediaDisposition.RejectedByPolicy,
                    "input-media-policy-rejected",
                    correlation,
                    cancellationToken);
            }

            return await BuildResultAsync(request.SessionId, ledger, trace, thread, [], null, null, null, cancellationToken);
        }

        var envelopes = new List<CanonicalMediaEnvelope>();
        var transportContext = new AudioTransportContext
        {
            SessionId = request.SessionId,
            Thread = request.ThreadRef,
            PolicySet = policy,
            Correlation = correlation
        };
        var transportAdapters = request.TransportAdapters ??
            new DefaultTransportAdapterRegistry(new ContentInputTransportAdapterFactory(_clock, _ids));

        foreach (var binding in inputBindings)
        {
            await using var adapter = await transportAdapters
                .CreateAsync(binding, transportContext, cancellationToken)
                .ConfigureAwait(false);

            await adapter.StartAsync(cancellationToken: cancellationToken);
            await foreach (var transportEvent in adapter.ReadEventsAsync(cancellationToken))
            {
                await TraceAsync(trace, new AudioTransportEventTraceRecord
                {
                    Id = _ids.NextTraceRecordId(),
                    SessionId = request.SessionId,
                    Family = RealtimeAudioTraceRecordFamily.Transport,
                    RecordedAt = _clock.Tick(),
                    Event = transportEvent,
                    Correlation = transportEvent.Correlation
                }, cancellationToken);
            }

            await foreach (var envelope in adapter.ReadMediaAsync(cancellationToken))
            {
                envelopes.Add(envelope);
            }
        }

        if (policy.InputMedia.HandlingMode is InputMediaHandlingMode.ReferenceOnly)
        {
            foreach (var inputContent in inputMedia)
            {
                await AppendInputDispositionAsync(
                    ledger,
                    trace,
                    request.SessionId,
                    inputContent,
                    InputMediaDisposition.ReferenceOnly,
                    "input-media-reference-only",
                    correlation,
                    cancellationToken);
            }

            return await BuildResultAsync(request.SessionId, ledger, trace, thread, envelopes, null, null, null, cancellationToken);
        }

        var route = request.ProviderRoute ?? new FakeProviderRoute(_ids, _clock);
        var turnController = request.TurnController ?? new InputTurnController(request.SessionId, _ids, _clock);
        var turnId = _ids.NextTurnId();
        TurnDecision? finalDecision = null;

        if (envelopes.Count > 0)
        {
            var inputContentEvidence = new TurnEvidence
            {
                Id = _ids.NextTurnEvidenceId(),
                SessionId = request.SessionId,
                TurnId = turnId,
                Kind = TurnEvidenceKind.InputMediaContent,
                Source = TurnEvidenceSource.InputContent,
                ObservedAt = _clock.Tick(),
                Detail = new InputContentEvidenceDetail
                {
                    Content = inputMedia[0]
                },
                Correlation = correlation with { TurnId = turnId }
            };

            finalDecision = await turnController.ObserveAsync(inputContentEvidence, cancellationToken);
            await TraceAsync(trace, new AudioTurnDecisionTraceRecord
            {
                Id = _ids.NextTraceRecordId(),
                SessionId = request.SessionId,
                Family = RealtimeAudioTraceRecordFamily.TurnDecision,
                RecordedAt = _clock.Tick(),
                Decision = finalDecision,
                Correlation = inputContentEvidence.Correlation
            }, cancellationToken);
        }

        var routeDecision = await route.SelectAsync(new ProviderRouteRequest
        {
            SessionId = request.SessionId,
            Inputs = envelopes,
            HasTextInput = request.Inputs
                .OfType<TextContent>()
                .Any(content => !string.IsNullOrWhiteSpace(content.Text)),
            PolicySet = policy,
            Candidates = request.ProviderCandidates
        }, cancellationToken);

        await TraceAsync(trace, new AudioRouteTraceRecord
        {
            Id = _ids.NextTraceRecordId(),
            SessionId = request.SessionId,
            Family = RealtimeAudioTraceRecordFamily.Route,
            RecordedAt = _clock.Tick(),
            Decision = routeDecision,
            Correlation = correlation
        }, cancellationToken);

        if (routeDecision.Kind is ProviderRouteDecisionKind.ReferenceOnly)
        {
            foreach (var inputContent in inputMedia)
            {
                await AppendInputDispositionAsync(
                    ledger,
                    trace,
                    request.SessionId,
                    inputContent,
                    InputMediaDisposition.ReferenceOnly,
                    routeDecision.Reason,
                    correlation,
                    cancellationToken);
            }

            return await BuildResultAsync(request.SessionId, ledger, trace, thread, envelopes, routeDecision, finalDecision, turnController.Snapshot, cancellationToken);
        }

        if (routeDecision.Kind is ProviderRouteDecisionKind.Reject or ProviderRouteDecisionKind.Fail ||
            routeDecision.Plan is null)
        {
            var failedDisposition = routeDecision.Kind is ProviderRouteDecisionKind.Reject
                ? InputMediaDisposition.RejectedByPolicy
                : InputMediaDisposition.Failed;
            foreach (var inputContent in inputMedia)
            {
                await AppendInputDispositionAsync(
                    ledger,
                    trace,
                    request.SessionId,
                    inputContent,
                    failedDisposition,
                    routeDecision.Reason,
                    correlation,
                    cancellationToken);
            }

            return await BuildResultAsync(request.SessionId, ledger, trace, thread, envelopes, routeDecision, finalDecision, turnController.Snapshot, cancellationToken);
        }

        var plan = routeDecision.Plan;

        var interactionFactory = request.InteractionSessionFactory
            ?? new FakeAudioInteractionSessionFactory(_ids, _clock);
        var interaction = request.InteractionSession
            ?? await interactionFactory.CreateAsync(routeDecision, cancellationToken);

        await interaction.OpenAsync(plan, cancellationToken);

        foreach (var text in request.Inputs.OfType<TextContent>())
        {
            if (!string.IsNullOrWhiteSpace(text.Text))
            {
                await interaction.SendAsync(new InteractionInputText(text.Text) { Correlation = correlation }, cancellationToken);
            }
        }

        foreach (var envelope in envelopes)
        {
            await interaction.SendAsync(new InteractionInputMedia(envelope) { Correlation = correlation }, cancellationToken);
        }

        var outputFlowIds = new Dictionary<ResponseId, OutputFlowId>();

        await foreach (var update in interaction.Updates.WithCancellation(cancellationToken))
        {
            await TraceAsync(trace, new AudioInteractionUpdateTraceRecord
            {
                Id = _ids.NextTraceRecordId(),
                SessionId = request.SessionId,
                Family = RealtimeAudioTraceRecordFamily.InteractionUpdate,
                RecordedAt = _clock.Tick(),
                Update = update,
                Correlation = update.Correlation
            }, cancellationToken);

            if (update is TranscriptUpdate transcriptUpdate &&
                transcriptUpdate.Stage is TranscriptStage.Final)
            {
                if (policy.InputMedia.AllowDerivedTextPersistence)
                {
                    var transcriptLedger = new TranscriptLedgerRecord
                    {
                        Id = _ids.NextLedgerRecordId(),
                        SessionId = request.SessionId,
                        Family = LedgerRecordFamily.Transcript,
                        RecordedAt = _clock.Tick(),
                        TurnId = turnId,
                        Text = transcriptUpdate.Text,
                        IsFinal = true,
                        InputContentId = transcriptUpdate.InputContentId,
                        Correlation = update.Correlation with { TurnId = turnId }
                    };
                    await ledger.AppendAsync(transcriptLedger, cancellationToken);
                    await TraceLedgerAsync(trace, request.SessionId, transcriptLedger, transcriptLedger.Correlation, cancellationToken);
                }

                await AppendInputDispositionAsync(
                    ledger,
                    trace,
                    request.SessionId,
                    FindInputContent(inputMedia, transcriptUpdate.InputContentId),
                    InputMediaDisposition.Transcribed,
                    "input-media-transcribed",
                    update.Correlation,
                    cancellationToken);

                var transcriptEvidence = new TurnEvidence
                {
                    Id = _ids.NextTurnEvidenceId(),
                    SessionId = request.SessionId,
                    TurnId = turnId,
                    Kind = TurnEvidenceKind.InputMediaTranscribed,
                    Source = TurnEvidenceSource.InputContent,
                    ObservedAt = _clock.Tick(),
                    Detail = new TranscriptEvidenceDetail
                    {
                        Text = transcriptUpdate.Text,
                        Confidence = transcriptUpdate.Confidence,
                        IsFinal = true
                    },
                    Correlation = update.Correlation with { TurnId = turnId }
                };

                finalDecision = await turnController.ObserveAsync(transcriptEvidence, cancellationToken);
                await TraceAsync(trace, new AudioTurnDecisionTraceRecord
                {
                    Id = _ids.NextTraceRecordId(),
                    SessionId = request.SessionId,
                    Family = RealtimeAudioTraceRecordFamily.TurnDecision,
                    RecordedAt = _clock.Tick(),
                    Decision = finalDecision,
                    Correlation = transcriptEvidence.Correlation
                }, cancellationToken);

                if (finalDecision.Commit is null)
                {
                    continue;
                }

                var turnLedger = new UserTurnLedgerRecord
                {
                    Id = _ids.NextLedgerRecordId(),
                    SessionId = request.SessionId,
                    Family = LedgerRecordFamily.UserTurn,
                    RecordedAt = _clock.Tick(),
                    TurnId = finalDecision.Commit.TurnId,
                    Text = finalDecision.Commit.Text,
                    EvidenceIds = finalDecision.Commit.EvidenceIds,
                    CommitReason = finalDecision.Commit.Reason,
                    Correlation = transcriptEvidence.Correlation
                };
                await ledger.AppendAsync(turnLedger, cancellationToken);
                await TraceLedgerAsync(trace, request.SessionId, turnLedger, transcriptEvidence.Correlation, cancellationToken);

                if (policy.ThreadProjection.ProjectCommittedUserTurns)
                {
                    await ProjectThreadAsync(
                        thread,
                        ledger,
                        trace,
                        request,
                        finalDecision.Commit.TurnId,
                        finalDecision.Commit.Text,
                        ThreadProjectionKind.UserTurn,
                        ThreadProjectionRole.User,
                        transcriptUpdate.InputContentId,
                        null,
                        null,
                        transcriptEvidence.Correlation,
                        cancellationToken);
                }

                continue;
            }

            if (update is OutputTextUpdate outputTextUpdate)
            {
                var outputFlowId = GetOutputFlowId(outputFlowIds, outputTextUpdate.ResponseId);
                var outputLedger = new AssistantOutputLedgerRecord
                {
                    Id = _ids.NextLedgerRecordId(),
                    SessionId = request.SessionId,
                    Family = LedgerRecordFamily.AssistantOutput,
                    RecordedAt = _clock.Tick(),
                    OutputFlowId = outputFlowId,
                    ResponseId = outputTextUpdate.ResponseId,
                    Text = outputTextUpdate.Delta,
                    Disposition = outputTextUpdate.IsFinal ? OutputDisposition.TextOnly : OutputDisposition.Draft,
                    Correlation = update.Correlation
                };
                await ledger.AppendAsync(outputLedger, cancellationToken);
                await TraceLedgerAsync(trace, request.SessionId, outputLedger, update.Correlation, cancellationToken);

                if (outputTextUpdate.IsFinal && policy.ThreadProjection.ProjectCommittedAssistantOutputs)
                {
                    await ProjectThreadAsync(
                        thread,
                        ledger,
                        trace,
                        request,
                        turnId,
                        outputTextUpdate.Delta,
                        ThreadProjectionKind.AssistantOutput,
                        ThreadProjectionRole.Assistant,
                        null,
                        outputFlowId,
                        outputTextUpdate.ResponseId,
                        update.Correlation,
                        cancellationToken);
                }

                continue;
            }

            if (update is OutputAudioUpdate outputAudioUpdate)
            {
                var outputLedger = new AssistantOutputLedgerRecord
                {
                    Id = _ids.NextLedgerRecordId(),
                    SessionId = request.SessionId,
                    Family = LedgerRecordFamily.AssistantOutput,
                    RecordedAt = _clock.Tick(),
                    OutputFlowId = GetOutputFlowId(outputFlowIds, outputAudioUpdate.ResponseId),
                    ResponseId = outputAudioUpdate.ResponseId,
                    Text = string.Empty,
                    Disposition = OutputDisposition.SegmentSynthesized,
                    Correlation = update.Correlation
                };
                await ledger.AppendAsync(outputLedger, cancellationToken);
                await TraceLedgerAsync(trace, request.SessionId, outputLedger, update.Correlation, cancellationToken);
                continue;
            }

            if (update is ResponseLifecycleUpdate responseLifecycleUpdate &&
                IsTerminalResponseState(responseLifecycleUpdate.State))
            {
                break;
            }
        }

        return await BuildResultAsync(request.SessionId, ledger, trace, thread, envelopes, routeDecision, finalDecision, turnController.Snapshot, cancellationToken);
    }

    private async ValueTask<AudioInteractionRuntimeResult> BuildResultAsync(
        AudioSessionId sessionId,
        IRealtimeConversationLedger ledger,
        IRealtimeAudioTraceStore trace,
        IThreadProjectionSink thread,
        IReadOnlyList<CanonicalMediaEnvelope>? envelopes,
        ProviderRouteDecision? routeDecision,
        TurnDecision? turnDecision,
        TurnSnapshot? turnSnapshot,
        CancellationToken cancellationToken)
    {
        var replay = await new PrivacySafeAudioTraceMaterializer(trace).MaterializeAsync(sessionId, cancellationToken);
        return new AudioInteractionRuntimeResult(
            Ledger: ledger,
            Trace: trace,
            Thread: thread,
            Envelopes: envelopes ?? [],
            RouteDecision: routeDecision,
            TurnDecision: turnDecision,
            TurnSnapshot: turnSnapshot,
            Replay: replay);
    }

    private async ValueTask AppendInputDispositionAsync(
        IRealtimeConversationLedger ledger,
        IRealtimeAudioTraceStore trace,
        AudioSessionId sessionId,
        InputContentRef content,
        InputMediaDisposition disposition,
        string? reason,
        AudioCorrelation correlation,
        CancellationToken cancellationToken)
    {
        var record = new InputContentLedgerRecord
        {
            Id = _ids.NextLedgerRecordId(),
            SessionId = sessionId,
            Family = LedgerRecordFamily.InputContent,
            RecordedAt = _clock.Tick(),
            Content = content,
            Disposition = disposition,
            Reason = reason,
            Correlation = correlation
        };

        await ledger.AppendAsync(record, cancellationToken);
        await TraceLedgerAsync(trace, sessionId, record, correlation, cancellationToken);
        await TraceAsync(trace, new AudioInputContentTraceRecord
        {
            Id = _ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.InputContent,
            RecordedAt = _clock.Tick(),
            Content = content,
            Disposition = disposition,
            Correlation = correlation
        }, cancellationToken);
    }

    private async ValueTask ProjectThreadAsync(
        IThreadProjectionSink thread,
        IRealtimeConversationLedger ledger,
        IRealtimeAudioTraceStore trace,
        AudioInteractionRuntimeRequest request,
        AudioTurnId turnId,
        string text,
        ThreadProjectionKind kind,
        ThreadProjectionRole role,
        InputContentId? inputContentId,
        OutputFlowId? outputFlowId,
        ResponseId? responseId,
        AudioCorrelation correlation,
        CancellationToken cancellationToken)
    {
        var projectionId = _ids.NextThreadProjectionId();
        var projection = new ThreadProjectionRecord
        {
            TurnId = turnId,
            Text = text,
            Kind = kind,
            Role = role,
            InputContentId = inputContentId,
            OutputFlowId = outputFlowId,
            ResponseId = responseId
        };
        var projectedEvent = await thread.ProjectAsync(request.ThreadRef, projection, cancellationToken);
        var projectionLedger = new ThreadProjectionLedgerRecord
        {
            Id = _ids.NextLedgerRecordId(),
            SessionId = request.SessionId,
            Family = LedgerRecordFamily.ThreadProjection,
            RecordedAt = _clock.Tick(),
            ProjectionId = projectionId,
            Thread = request.ThreadRef,
            Projection = projection,
            ProjectedEvent = projectedEvent,
            Correlation = correlation
        };
        await ledger.AppendAsync(projectionLedger, cancellationToken);
        await TraceLedgerAsync(trace, request.SessionId, projectionLedger, correlation, cancellationToken);
        await TraceAsync(trace, new AudioThreadProjectionTraceRecord
        {
            Id = _ids.NextTraceRecordId(),
            SessionId = request.SessionId,
            Family = RealtimeAudioTraceRecordFamily.ThreadProjection,
            RecordedAt = _clock.Tick(),
            ProjectionId = projectionId,
            ProjectedEvent = projectedEvent,
            Correlation = correlation
        }, cancellationToken);
    }

    private static InputContentRef FindInputContent(
        IReadOnlyList<InputContentRef> inputMedia,
        InputContentId? inputContentId)
    {
        return inputMedia.FirstOrDefault(content => inputContentId is not null && content.Id == inputContentId)
            ?? inputMedia.First();
    }

    private OutputFlowId GetOutputFlowId(
        Dictionary<ResponseId, OutputFlowId> outputFlowIds,
        ResponseId responseId)
    {
        if (outputFlowIds.TryGetValue(responseId, out var outputFlowId))
        {
            return outputFlowId;
        }

        outputFlowId = _ids.NextOutputFlowId();
        outputFlowIds.Add(responseId, outputFlowId);
        return outputFlowId;
    }

    private ValueTask TraceLedgerAsync(
        IRealtimeAudioTraceStore trace,
        AudioSessionId sessionId,
        RealtimeLedgerRecord ledgerRecord,
        AudioCorrelation correlation,
        CancellationToken cancellationToken)
    {
        return TraceAsync(trace, new AudioLedgerTraceRecord
        {
            Id = _ids.NextTraceRecordId(),
            SessionId = sessionId,
            Family = RealtimeAudioTraceRecordFamily.Ledger,
            RecordedAt = _clock.Tick(),
            LedgerRecordId = ledgerRecord.Id,
            LedgerFamily = ledgerRecord.Family,
            Correlation = correlation
        }, cancellationToken);
    }

    private static ValueTask TraceAsync(
        IRealtimeAudioTraceStore trace,
        RealtimeAudioTraceRecord record,
        CancellationToken cancellationToken)
    {
        return trace.AppendAsync(record, cancellationToken);
    }

    private static bool IsTerminalResponseState(ResponseLifecycleState state)
        => state is ResponseLifecycleState.Completed
            or ResponseLifecycleState.Incomplete
            or ResponseLifecycleState.Cancelled
            or ResponseLifecycleState.Failed;

}

public sealed record AudioInteractionRuntimeRequest
{
    public required AudioSessionId SessionId { get; init; }

    public required IReadOnlyList<AIContent> Inputs { get; init; }

    public IReadOnlyList<InputContentRef> InputContentRefs { get; init; } = [];

    public IReadOnlyList<TransportBinding> InputBindings { get; init; } = [];

    public ThreadRef ThreadRef { get; init; } = new("agent", "session", "main");

    public string? RequestId { get; init; }

    public AudioPolicySet? PolicySet { get; init; }

    public ITransportAdapterRegistry? TransportAdapters { get; init; }

    public IProviderRoute? ProviderRoute { get; init; }

    public IReadOnlyList<ProviderCapabilityProfile> ProviderCandidates { get; init; } = [];

    public IAudioInteractionSession? InteractionSession { get; init; }

    public IAudioInteractionSessionFactory? InteractionSessionFactory { get; init; }

    public ITurnController? TurnController { get; init; }

    public IRealtimeConversationLedger? Ledger { get; init; }

    public IRealtimeAudioTraceStore? Trace { get; init; }

    public IThreadProjectionSink? ThreadProjectionSink { get; init; }
}

public sealed record AudioInteractionRuntimeResult(
    IRealtimeConversationLedger Ledger,
    IRealtimeAudioTraceStore Trace,
    IThreadProjectionSink Thread,
    IReadOnlyList<CanonicalMediaEnvelope> Envelopes,
    ProviderRouteDecision? RouteDecision,
    TurnDecision? TurnDecision,
    TurnSnapshot? TurnSnapshot,
    AudioReplayScenario Replay);
