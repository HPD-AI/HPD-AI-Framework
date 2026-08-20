using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Audio.Runtime;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.Audio.Runtime.Scenarios;
using HPD.Agent.Audio.Runtime.Trace;
using HPD.Agent.Audio.Trace;
using HPD.Agent.Audio.Turns;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class InputMediaSliceTests
{
    [Fact]
    public async Task InputMedia_PreservesIdentity_AndCommitsTranscript()
    {
        var content = TestInputContent.Audio(name: "question.wav", mediaType: "audio/wav");

        var result = await RunScenarioAsync(content);
        var envelope = Assert.Single(result.Envelopes);
        var ledgerRecords = result.LedgerRecords.ToArray();
        Assert.NotNull(result.EndpointSnapshotProjectionV1);
        var turnSnapshot = result.EndpointSnapshotProjectionV1!;

        Assert.Equal(content.Id, envelope.PayloadInputContent().Id);
        Assert.Equal(MediaCaptureDisposition.MetadataOnly, envelope.CaptureDisposition);
        Assert.Contains(ledgerRecords.OfType<InputContentLedgerRecord>(), r =>
            r.Content.Id == content.Id &&
            r.Disposition == InputMediaDisposition.Transcribed);
        Assert.Contains(ledgerRecords.OfType<TranscriptLedgerRecord>(), r =>
            r.InputContentId == content.Id &&
            r.Text == "transcript:question.wav" &&
            r.IsFinal);
        Assert.Contains(ledgerRecords.OfType<UserTurnLedgerRecord>(), r =>
            r.Text == "transcript:question.wav" &&
            r.CommitReason == EndpointCommitProjectionReasonV1.InputMediaTranscript);
        Assert.Contains(turnSnapshot.Evidence, e =>
            e.Kind == EndpointEvidenceProjectionKindV1.InputMediaContent &&
            e.Source == EndpointEvidenceProjectionSourceV1.InputContent);
        Assert.Contains(turnSnapshot.Evidence, e =>
            e.Kind == EndpointEvidenceProjectionKindV1.InputMediaTranscribed &&
            e.Detail is TranscriptEvidenceProjectionDetailV1 { Text: "transcript:question.wav", IsFinal: true });
    }

    [Fact]
    public async Task InputMedia_PolicyRejects_WithoutTranscript()
    {
        var content = TestInputContent.Audio(name: "blocked.mp3", mediaType: "audio/mpeg");
        var policy = new AudioPolicySet
        {
            InputMedia = new InputMediaPolicy
            {
                HandlingMode = InputMediaHandlingMode.Reject,
                AllowDerivedTextPersistence = true
            }
        };

        var result = await RunScenarioAsync(content, policySet: policy);
        var ledgerRecords = result.LedgerRecords.ToArray();
        var traceRecords = result.TraceRecords.ToArray();

        Assert.Empty(result.Envelopes);
        Assert.Contains(ledgerRecords.OfType<InputContentLedgerRecord>(), r =>
            r.Content.Id == content.Id &&
            r.Disposition == InputMediaDisposition.RejectedByPolicy);
        Assert.DoesNotContain(ledgerRecords, r => r is TranscriptLedgerRecord);
        Assert.DoesNotContain(ledgerRecords, r => r is UserTurnLedgerRecord);
        Assert.Empty(result.Thread.AsInMemoryThread().ProjectedTurns);
        Assert.Contains(traceRecords.OfType<AudioInputContentTraceRecord>(), r =>
            r.Content.Id == content.Id &&
            r.Disposition == InputMediaDisposition.RejectedByPolicy);
    }

    [Fact]
    public async Task InputMedia_MetadataOnly_DoesNotRetainRawAudio()
    {
        var content = TestInputContent.Audio(name: "private.webm", mediaType: "audio/webm", sizeBytes: 4096);

        var result = await RunScenarioAsync(content);
        var envelope = Assert.Single(result.Envelopes);
        var ledgerRecords = result.LedgerRecords.ToArray();
        var traceRecords = result.TraceRecords.ToArray();

        Assert.NotEqual(MediaCaptureDisposition.RawRetained, envelope.CaptureDisposition);
        Assert.All(ledgerRecords.OfType<InputContentLedgerRecord>(), r =>
        {
            Assert.Equal(content.Id, r.Content.Id);
            Assert.Null(r.Content.Artifact);
            Assert.Null(r.Content.ProviderRef);
            Assert.NotNull(r.Content.Source);
        });
        Assert.All(traceRecords.OfType<AudioInputContentTraceRecord>(), r =>
        {
            Assert.Equal(content.Id, r.Content.Id);
            Assert.Null(r.Content.Artifact);
            Assert.Null(r.Content.ProviderRef);
        });
    }

    [Fact]
    public async Task InputMedia_Replay_IsPrivacySafe()
    {
        var content = TestInputContent.Audio(name: "replay.flac", mediaType: "audio/flac", sha256: "sha256-replay");

        var result = await RunScenarioAsync(content);
        var records = result.TraceRecords;

        Assert.All(records, record => Assert.Equal(TestSessionId, record.SessionId));
        Assert.Contains(records.OfType<AudioInputContentTraceRecord>(), r =>
            r.Content.Id == content.Id &&
            r.Content.Sha256 == "sha256-replay" &&
            r.Content.Source is not null);
        Assert.DoesNotContain(records.OfType<AudioInputContentTraceRecord>(), r =>
            r.Content.Artifact is not null ||
            r.Content.ProviderRef is not null);
    }

    [Fact]
    public async Task InputMedia_FakeProviderOutput_ProjectsThread()
    {
        var content = TestInputContent.Audio(name: "thread.ogg", mediaType: "audio/ogg");

        var result = await RunScenarioAsync(content);
        var ledgerRecords = result.LedgerRecords.ToArray();

        var projection = Assert.Single(result.Thread.AsInMemoryThread().ProjectedTurns);
        Assert.Equal("session-1", projection.Thread.SessionId);
        Assert.Equal("main", projection.Thread.ThreadId);
        Assert.Equal(content.Id, projection.Record.InputContentId);
        Assert.Equal("transcript:thread.ogg", projection.Record.Text);
        Assert.Contains(ledgerRecords.OfType<ThreadProjectionLedgerRecord>(), r =>
            r.Projection.InputContentId == content.Id &&
            r.ProjectedEvent is not null);
    }

    [Fact]
    public async Task InputMedia_TextAndMultipleAudio_SendsTextOnceBeforeAudio()
    {
        var first = TestInputContent.Audio(name: "first.wav", mediaType: "audio/wav");
        var second = TestInputContent.Audio(name: "second.wav", mediaType: "audio/wav");
        var session = new FakeAudioInteractionSession(
            new InteractionSessionId("fake-text-audio-order"),
            new FakeAudioInteractionSessionOptions
            {
                ScriptedTranscript = "ignored"
            });

        await RunScenarioAsync(
            first,
            additionalContents: [second],
            inputs: [new TextContent("answer briefly")],
            providerRoute: new FakeProviderRoute(
                providerKey: "stt",
                capabilities: ProviderCapabilityFlag.SpeechToText),
            interactionSessionFactory: new StaticInteractionSessionFactory(session));

        Assert.Collection(
            session.ReceivedInputs,
            input =>
            {
                var text = Assert.IsType<InteractionInputText>(input);
                Assert.Equal("answer briefly", text.Text);
            },
            input => Assert.IsType<InteractionInputMedia>(input),
            input => Assert.IsType<InteractionInputMedia>(input));
    }

    [Fact]
    public async Task InputMedia_UsesInteractionFactory_ForTranscriptGeneration()
    {
        var content = TestInputContent.Audio(name: "factory.wav", mediaType: "audio/wav");
        var factory = new FakeAudioInteractionSessionFactory(
            options: new FakeAudioInteractionSessionOptions
            {
                TranscriptFactory = inputContent => $"factory transcript:{inputContent.Name ?? inputContent.Id.Value}"
            });

        var result = await RunScenarioAsync(content, interactionSessionFactory: factory);

        Assert.Contains(result.LedgerRecords.ToArray().OfType<TranscriptLedgerRecord>(), r =>
            r.InputContentId == content.Id &&
            r.Text == "factory transcript:factory.wav");
    }

    [Fact]
    public async Task InputMedia_RouteRejected_DoesNotCreateInteractionSessionOrTranscript()
    {
        var content = TestInputContent.Audio(name: "route-rejected.wav", mediaType: "audio/wav");
        var route = new ScriptedProviderRoute(ProviderRouteDecisionKind.Reject, "route-rejected");
        var factory = new CountingInteractionSessionFactory();

        var result = await RunScenarioAsync(content, providerRoute: route, interactionSessionFactory: factory);

        Assert.Equal(0, factory.CreateCount);
        Assert.Equal(ProviderRouteDecisionKind.Reject, result.RouteDecision?.Kind);
        Assert.Null(result.RouteDecision?.Plan);
        Assert.DoesNotContain(result.LedgerRecords.ToArray(), r => r is TranscriptLedgerRecord);
        Assert.DoesNotContain(result.TraceRecords.ToArray(), r => r is AudioInteractionUpdateTraceRecord);
    }

    [Fact]
    public async Task InputMedia_RouteReferenceOnly_DoesNotCreateInteractionSessionOrTranscript()
    {
        var content = TestInputContent.Audio(name: "route-metadata.wav", mediaType: "audio/wav");
        var route = new ScriptedProviderRoute(ProviderRouteDecisionKind.ReferenceOnly, "route-reference-only");
        var factory = new CountingInteractionSessionFactory();

        var result = await RunScenarioAsync(content, providerRoute: route, interactionSessionFactory: factory);

        Assert.Equal(0, factory.CreateCount);
        Assert.Equal(ProviderRouteDecisionKind.ReferenceOnly, result.RouteDecision?.Kind);
        Assert.Null(result.RouteDecision?.Plan);
        Assert.DoesNotContain(result.LedgerRecords.ToArray(), r => r is TranscriptLedgerRecord);
        Assert.DoesNotContain(result.TraceRecords.ToArray(), r => r is AudioInteractionUpdateTraceRecord);
    }

    [Fact]
    public async Task InputMedia_TranscriptCommit_ComesFromFinalInteractionUpdate()
    {
        var content = TestInputContent.Audio(name: "final-update.wav", mediaType: "audio/wav");

        var result = await RunScenarioAsync(content);

        var interactionTrace = Assert.Single(result.TraceRecords.ToArray().OfType<AudioInteractionUpdateTraceRecord>());
        var transcriptUpdate = Assert.IsType<TranscriptUpdate>(interactionTrace.Update);
        Assert.Equal(TranscriptProjectionStageV1.Final, transcriptUpdate.Stage);
        Assert.Equal("transcript:final-update.wav", transcriptUpdate.Text);
        Assert.Contains(result.LedgerRecords.ToArray().OfType<UserTurnLedgerRecord>(), r =>
            r.Text == transcriptUpdate.Text &&
            r.CommitReason == EndpointCommitProjectionReasonV1.InputMediaTranscript);
    }

    [Fact]
    public async Task InputMedia_RealtimeToolCall_IsObservedButNotExecutedByInteractionRuntime()
    {
        var content = TestInputContent.Audio(name: "tool-call.wav", mediaType: "audio/wav");
        var session = new FakeAudioInteractionSession(
            new InteractionSessionId("tool-call-session"),
            new FakeAudioInteractionSessionOptions
            {
                TranscriptFactory = inputContent => $"transcript:{inputContent.Name ?? inputContent.Id.Value}",
                ScriptedToolCall = new FakeRealtimeToolCall(
                    "call-1",
                    "lookup",
                    "{\"city\":\"Seattle\"}")
            });
        var result = await RunScenarioAsync(
            content,
            interactionSessionFactory: new StaticInteractionSessionFactory(session));

        Assert.Empty(session.ReceivedToolResults);
        Assert.Contains(result.TraceRecords.ToArray().OfType<AudioInteractionUpdateTraceRecord>(), trace =>
            trace.Update is ToolCallUpdate toolCall &&
            toolCall.ToolCallId == "call-1" &&
            toolCall.Name == "lookup" &&
            toolCall.ArgumentsDelta == "{\"city\":\"Seattle\"}");
    }

    private static readonly AudioSessionId TestSessionId = new("session-1");

    private static ValueTask<AudioInteractionRuntimeResult> RunScenarioAsync(
        InputContentRef content,
        IReadOnlyList<InputContentRef>? additionalContents = null,
        IReadOnlyList<AIContent>? inputs = null,
        AudioPolicySet? policySet = null,
        IProviderRoute? providerRoute = null,
        IAudioInteractionSessionFactory? interactionSessionFactory = null)
    {
        var runner = new AudioInteractionRuntimeRunner();
        return runner.RunAsync(new AudioInteractionRuntimeRequest
        {
            SessionId = TestSessionId,
            ThreadRef = new ThreadRef("audio-test-agent", "session-1", "main"),
            Inputs = inputs ?? [],
            InputContentRefs = additionalContents is null
                ? [content]
                : [content, .. additionalContents],
            PolicySet = policySet,
            ProviderRoute = providerRoute,
            InteractionSessionFactory = interactionSessionFactory ?? new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"transcript:{inputContent.Name ?? inputContent.Id.Value}"
                })
        });
    }

    private sealed class StaticInteractionSessionFactory : IAudioInteractionSessionFactory
    {
        private readonly IAudioInteractionSession _session;

        public StaticInteractionSessionFactory(IAudioInteractionSession session)
        {
            _session = session;
        }

        public ValueTask<IAudioInteractionSession> CreateAsync(
            ProviderRouteDecision decision,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_session);
    }

    private sealed class CountingInteractionSessionFactory : IAudioInteractionSessionFactory
    {
        public int CreateCount { get; private set; }

        public ValueTask<IAudioInteractionSession> CreateAsync(
            ProviderRouteDecision decision,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            return ValueTask.FromResult<IAudioInteractionSession>(
                new FakeAudioInteractionSession(
                    new InteractionSessionId($"counted-{CreateCount}"),
                    new FakeAudioInteractionSessionOptions()));
        }
    }

    private sealed class ScriptedProviderRoute : IProviderRoute
    {
        private readonly RuntimeClock _clock = new();
        private readonly ProviderRouteDecisionKind _kind;
        private readonly string _reason;

        public ScriptedProviderRoute(ProviderRouteDecisionKind kind, string reason)
        {
            _kind = kind;
            _reason = reason;
            CurrentEpoch = new ProviderRouteEpoch
            {
                Id = new ProviderRouteEpochId("route-epoch-0000"),
                ProviderKey = "scripted-route",
                StartedAt = _clock.UtcNow
            };
        }

        public ProviderRouteId Id { get; } = new("scripted-route");

        public ProviderRouteState State { get; private set; } = ProviderRouteState.Ready;

        public ProviderRouteEpoch CurrentEpoch { get; private set; }

        public async IAsyncEnumerable<ProviderRouteDecision> ReadDecisionsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask<ProviderRouteDecision> SelectAsync(
            ProviderRouteRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = ProviderRouteState.Active;
            CurrentEpoch = new ProviderRouteEpoch
            {
                Id = new ProviderRouteEpochId($"route-epoch-{_kind}"),
                ProviderKey = "scripted-route",
                StartedAt = _clock.Tick()
            };

            return ValueTask.FromResult(new ProviderRouteDecision
            {
                RouteId = Id,
                Kind = _kind,
                Epoch = CurrentEpoch,
                Plan = _kind is ProviderRouteDecisionKind.OpenCandidate
                    ? new InteractionExecutionPlan
                    {
                        Topology = AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech,
                        RouteEpoch = CurrentEpoch,
                        Capabilities = new ProviderCapabilityProfile
                        {
                            ProviderKey = "scripted-route",
                            Declared = new ProviderDeclaredCapabilities
                            {
                                Flags = ProviderCapabilityFlag.SpeechToText
                            }
                        }
                    }
                    : null,
                Reason = _reason
            });
        }

        public ValueTask DisposeAsync()
        {
            State = ProviderRouteState.Stopped;
            return ValueTask.CompletedTask;
        }
    }
}
