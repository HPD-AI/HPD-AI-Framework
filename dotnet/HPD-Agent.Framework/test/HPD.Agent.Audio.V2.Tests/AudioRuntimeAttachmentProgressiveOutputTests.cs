using System.Reflection;
using System.Threading.Channels;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.AgentIntegration.Middleware;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Runtime.Thread;
using HPD.Agent.Audio.Trace;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable MEAI001

public sealed class AudioRuntimeAttachmentProgressiveOutputTests
{
    [Fact]
    public void WrapModelTurnStreamingAsync_ProgressiveWithoutPreparedAuthority_FailsClosedBeforeEffect()
    {
        var tts = new FakeTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            AssistantOutputTextToSpeechClient = tts
        });
        var request = CreateModelRequest(new InMemoryContentStore(), new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);

        Assert.Null(stream);
        Assert.Empty(tts.Texts);
        Assert.Empty(attachment.LastOutputResults);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_ProgressiveMode_YieldsOriginalUpdatesAndStoresSegments()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.ProgressiveWithFinalFallback,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantOutputModelId = "fake-model",
            AssistantOutputVoiceId = "voice-1",
            AssistantOutputFormat = "mp3",
            AssistantOutputPacingOptions = new TextToSpeechPacingOptions
            {
                Continuation = new TextToSpeechContinuationOptions
                {
                    MaxCharacters = 80
                }
            }
        });
        var coordinator = new EventCoordinator();
        var readyEvents = new List<AssistantAudioOutputArtifactCapturedEvent>();
        var completedEvents = new List<AssistantAudioOutputCompletedEvent>();
        using var readySub = coordinator.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(evt =>
        {
            readyEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var completedSub = coordinator.Subscribe<AssistantAudioOutputCompletedEvent>(evt =>
        {
            completedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });

        var request = CreateModelRequest(contentStore, coordinator);
        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);

        Assert.NotNull(stream);
        var updates = new List<AgentModelUpdate>();
        await foreach (var update in stream!)
        {
            updates.Add(update);
        }

        Assert.Equal(["Hello there. ", "Second sentence."], updates.Select(ExtractText).Where(static text => text.Length > 0));
        Assert.Equal(2, tts.Texts.Count);
        Assert.Contains("Hello there.", tts.Texts);
        Assert.Contains("Second sentence.", tts.Texts);
        Assert.Equal(2, attachment.LastOutputResults.Count);
        Assert.All(attachment.LastOutputResults, result =>
            Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status));
        await WaitUntilAsync(() => readyEvents.Count == 2 && completedEvents.Count == 1);
        Assert.Equal(2, readyEvents.Count);
        Assert.All(readyEvents, evt =>
        {
            Assert.Equal("audio/mpeg", evt.MediaType);
            Assert.True(evt.SizeBytes > 0);
            Assert.Equal("hpd-content", evt.Artifact.Store);
            Assert.NotNull(evt.Artifact.ArtifactId);
        });
        Assert.Equal(2, (await contentStore.QueryAsync(ContentScope.Create("session-progressive"))).Count);
        var completed = Assert.Single(completedEvents);
        Assert.Equal(2, completed.SegmentCount);
        Assert.False(completed.Played);
        Assert.False(completed.HeardByUser);
    }

    [Fact]
    public async Task AfterMessageTurn_ProgressiveWithFinalFallback_DoesNotDuplicateFinalSynthesisAfterSegment()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.ProgressiveWithFinalFallback,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts"
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        var afterContext = CreateAfterMessageTurnContext("Hello there. Second sentence.", contentStore);
        await attachment.BeforeMessageTurnAccountingCloseAsync(afterContext, CancellationToken.None);

        Assert.Equal(2, tts.Texts.Count);
        Assert.Contains("Hello there.", tts.Texts);
        Assert.Contains("Second sentence.", tts.Texts);
        Assert.Equal(2, attachment.LastOutputResults.Count);
    }

    [Fact]
    public async Task AfterMessageTurn_ProgressiveWithFinalFallback_SynthesizesFinalTextForDifferentUnsynthesizedResponse()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.ProgressiveWithFinalFallback,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts"
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        var afterContext = CreateAfterMessageTurnContext(
            "Final response was produced without progressive synthesis.",
            contentStore,
            responseId: "response-final-unsynthesized");
        await attachment.BeforeMessageTurnAccountingCloseAsync(afterContext, CancellationToken.None);

        Assert.Equal(3, tts.Texts.Count);
        Assert.Contains("Hello there.", tts.Texts);
        Assert.Contains("Second sentence.", tts.Texts);
        Assert.Contains("Final response was produced without progressive synthesis.", tts.Texts);
        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal("response-final-unsynthesized", result.ResponseId.Value);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_MissingContentStore_EmitsSegmentFailureWithoutAudioBytes()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(),
            AssistantOutputProviderKey = "fake-tts"
        });
        var coordinator = new EventCoordinator();
        var failedEvents = new List<AssistantAudioOutputSegmentFailedEvent>();
        using var failedSub = coordinator.Subscribe<AssistantAudioOutputSegmentFailedEvent>(evt =>
        {
            failedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore: null, coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() => failedEvents.Count > 0);
        Assert.NotEmpty(failedEvents);
        Assert.All(failedEvents, failed =>
        {
            Assert.Equal("MissingContentStore", failed.Error?.Code);
            Assert.Equal("fake-tts", failed.ProviderKey);
            Assert.Equal(nameof(OutputCommitDisposition.SynthesisFailedTextOnly), failed.Disposition);
            Assert.DoesNotContain("base64", failed.ToString(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_DisabledArtifactCaptureWithoutContentStore_SynthesizesChunksWithoutArtifact()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(),
            AssistantOutputProviderKey = "fake-tts",
            AssistantOutputArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.Disabled
        });
        var coordinator = new EventCoordinator();
        var failedEvents = new List<AssistantAudioOutputSegmentFailedEvent>();
        var chunkEvents = new List<AssistantAudioOutputChunkReadyEvent>();
        var artifactEvents = new List<AssistantAudioOutputArtifactCapturedEvent>();
        var completedEvents = new List<AssistantAudioOutputCompletedEvent>();
        using var failedSub = coordinator.Subscribe<AssistantAudioOutputSegmentFailedEvent>(evt =>
        {
            failedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var chunkSub = coordinator.Subscribe<AssistantAudioOutputChunkReadyEvent>(evt =>
        {
            chunkEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var artifactSub = coordinator.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(evt =>
        {
            artifactEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var completedSub = coordinator.Subscribe<AssistantAudioOutputCompletedEvent>(evt =>
        {
            completedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore: null, coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() => completedEvents.Count == 1);

        Assert.Empty(failedEvents);
        Assert.Empty(artifactEvents);
        Assert.Equal(2, chunkEvents.Count);
        Assert.All(chunkEvents, evt =>
        {
            Assert.Equal("EncodedBytes", evt.PayloadKind);
            Assert.True(evt.SizeBytes > 0);
        });
        Assert.Equal(2, attachment.LastOutputResults.Count);
        Assert.All(attachment.LastOutputResults, result =>
        {
            Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status);
            Assert.Empty(result.Ledger.OfType<OutputArtifactLedgerRecord>());
        });
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlaybackEnabled_EmitsSinkPlaybackTruth()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var sink = new CompletingAudioOutputSink();
        var structEvents = new StructEventHub();
        using var playoutInbox = structEvents.Route<AudioOutputPlayoutSample>().CreateInbox();
        using var queueDepthInbox = structEvents.Route<AudioOutputQueueDepthSample>().CreateInbox();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true
        });
        var coordinator = new EventCoordinator();
        var queuedEvents = new List<AssistantAudioPlaybackQueuedEvent>();
        var startedEvents = new List<AssistantAudioPlaybackStartedEvent>();
        var progressEvents = new List<AssistantAudioPlaybackProgressEvent>();
        var completedEvents = new List<AssistantAudioPlaybackCompletedEvent>();
        var outputCompletedEvents = new List<AssistantAudioOutputCompletedEvent>();
        using var queuedSub = coordinator.Subscribe<AssistantAudioPlaybackQueuedEvent>(evt =>
        {
            queuedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var startedSub = coordinator.Subscribe<AssistantAudioPlaybackStartedEvent>(evt =>
        {
            startedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var progressSub = coordinator.Subscribe<AssistantAudioPlaybackProgressEvent>(evt =>
        {
            progressEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var completedSub = coordinator.Subscribe<AssistantAudioPlaybackCompletedEvent>(evt =>
        {
            completedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var outputCompletedSub = coordinator.Subscribe<AssistantAudioOutputCompletedEvent>(evt =>
        {
            outputCompletedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore, coordinator, structEvents);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() =>
            queuedEvents.Count == 2 &&
            startedEvents.Count == 2 &&
            progressEvents.Count > 0 &&
            completedEvents.Count == 1 &&
            outputCompletedEvents.Count == 1);
        Assert.Equal(2, queuedEvents.Count);
        Assert.Equal(2, startedEvents.Count);
        var completed = Assert.Single(completedEvents);
        Assert.NotEmpty(progressEvents);
        Assert.All(progressEvents, progress =>
        {
            Assert.True(progress.CanInterrupt);
            Assert.Equal(completed.EventFlowId, progress.EventFlowId);
            Assert.False(progress.Played);
            Assert.False(progress.HeardByUser);
            Assert.True(progress.PlayedTextLength > 0);
        });
        Assert.True(completed.Played);
        Assert.True(completed.HeardByUser);
        Assert.Equal("response-progressive-1", completed.ResponseId);
        Assert.False(completed.CanInterrupt);
        Assert.NotNull(completed.EventFlowId);
        var outputCompleted = Assert.Single(outputCompletedEvents);
        Assert.Equal(nameof(OutputCommitDisposition.PlayedComplete), outputCompleted.Disposition);
        Assert.True(outputCompleted.Played);
        Assert.True(outputCompleted.HeardByUser);
        Assert.All(queuedEvents, queued =>
        {
            Assert.True(queued.CanInterrupt);
            Assert.Equal(completed.EventFlowId, queued.EventFlowId);
            Assert.False(queued.Played);
            Assert.False(queued.HeardByUser);
        });
        Assert.All(startedEvents, started =>
        {
            Assert.True(started.CanInterrupt);
            Assert.Equal(completed.EventFlowId, started.EventFlowId);
        });

        var playbackLedger = attachment.LastOutputLedger.OfType<OutputPlaybackLedgerRecord>().ToArray();
        var playbackTrace = attachment.LastOutputTrace.OfType<AudioOutputPlaybackTraceRecord>().ToArray();
        Assert.Contains(playbackLedger, record => record.Disposition == OutputPlaybackDisposition.Queued);
        Assert.Contains(playbackLedger, record => record.Disposition == OutputPlaybackDisposition.Started);
        Assert.Contains(playbackLedger, record => record.Disposition == OutputPlaybackDisposition.Progress);
        var playedLedger = Assert.Single(playbackLedger, record =>
            record.Disposition == OutputPlaybackDisposition.PlayedComplete);
        Assert.Equal("response-progressive-1", playedLedger.ResponseId.Value);
        Assert.True(playedLedger.PlayedTextLength > 0);
        Assert.Equal(playbackLedger.Length, playbackTrace.Length);
        Assert.Empty(attachment.LastOutputLedger.OfType<ThreadProjectionLedgerRecord>());
        Assert.Empty(attachment.LastOutputTrace.OfType<AudioThreadProjectionTraceRecord>());

        Assert.True(queueDepthInbox.TryRead(out var queueDepthSample));
        Assert.Equal("session-progressive", queueDepthSample.SessionId);
        Assert.Equal(1, queueDepthSample.SequenceNumber);
        Assert.True(queueDepthSample.QueuedSegments > 0);
        Assert.True(playoutInbox.TryRead(out var playoutSample));
        Assert.Equal("session-progressive", playoutSample.SessionId);
        Assert.Equal(1, playoutSample.SequenceNumber);
        Assert.True(playoutSample.PlayedTextLength > 0);
        Assert.DoesNotContain(attachment.LastOutputLedger.OfType<object>(), record =>
            record is AudioOutputPlayoutSample or AudioOutputQueueDepthSample);
        Assert.Empty(attachment.LastOutputTrace.OfType<AudioStructEventSampleTraceRecord>());
        Assert.Equal(2, sink.WriteCount);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlaybackSinkRejectsStream_DoesNotWriteOrCompleteRejectedSink()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var sink = new RejectingAudioOutputSink();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true
        });
        var coordinator = new EventCoordinator();
        var failedEvents = new List<AssistantAudioPlaybackFailedEvent>();
        using var failedSub = coordinator.Subscribe<AssistantAudioPlaybackFailedEvent>(evt =>
        {
            failedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore, coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() => failedEvents.Count > 0);
        Assert.True(sink.StartCount > 0);
        Assert.Equal(0, sink.WriteCount);
        Assert.Equal(0, sink.CompleteCount);
        Assert.All(failedEvents, evt =>
        {
            Assert.Equal("sink_rejected", evt.Error.Code);
            Assert.False(evt.Played);
            Assert.False(evt.HeardByUser);
        });
        Assert.Contains(attachment.LastOutputLedger.OfType<OutputPlaybackLedgerRecord>(), record =>
            record.Disposition == OutputPlaybackDisposition.PlaybackFailed);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlaybackSinkRejectsWithoutEvent_StillCommitsPlaybackFailed()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var sink = new RejectingAudioOutputSink(emitFailureEvent: false);
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true
        });
        var coordinator = new EventCoordinator();
        var failedEvents = new List<AssistantAudioPlaybackFailedEvent>();
        var outputCompletedEvents = new List<AssistantAudioOutputCompletedEvent>();
        using var failedSub = coordinator.Subscribe<AssistantAudioPlaybackFailedEvent>(evt =>
        {
            failedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var outputCompletedSub = coordinator.Subscribe<AssistantAudioOutputCompletedEvent>(evt =>
        {
            outputCompletedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore, coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() => failedEvents.Count > 0 && outputCompletedEvents.Count == 1);
        Assert.True(sink.StartCount > 0);
        Assert.Equal(0, sink.WriteCount);
        Assert.Equal(0, sink.CompleteCount);

        var failedEvent = Assert.Single(failedEvents);
        Assert.Equal("sink_rejected", failedEvent.Error.Code);
        Assert.False(failedEvent.Played);
        Assert.False(failedEvent.HeardByUser);
        var outputCompleted = Assert.Single(outputCompletedEvents);
        Assert.Equal(nameof(OutputCommitDisposition.PlaybackFailed), outputCompleted.Disposition);
        Assert.False(outputCompleted.Played);
        Assert.False(outputCompleted.HeardByUser);
        Assert.Contains(attachment.LastOutputLedger.OfType<OutputPlaybackLedgerRecord>(), record =>
            record.Disposition == OutputPlaybackDisposition.PlaybackFailed);
        Assert.Contains(attachment.LastOutputLedger.OfType<AssistantOutputLedgerRecord>(), record =>
            record.Disposition == OutputDisposition.PlaybackFailed);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_StructSampleTraceCapture_RequiresPolicy()
    {
        var contentStore = new InMemoryContentStore();
        var structEvents = new StructEventHub();
        using var playoutInbox = structEvents.Route<AudioOutputPlayoutSample>().CreateInbox();
        using var queueDepthInbox = structEvents.Route<AudioOutputQueueDepthSample>().CreateInbox();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(),
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = new CompletingAudioOutputSink(),
            EnableAssistantOutputPlayback = true,
            PolicySet = new AudioPolicySet
            {
                Trace = new TraceCapturePolicy
                {
                    CaptureStructEventSamples = true
                }
            }
        });

        var stream = attachment.WrapModelTurnStreamingAsync(
            CreateModelRequest(contentStore, new EventCoordinator(), structEvents),
            StreamingHandler,
            CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.True(queueDepthInbox.TryRead(out var queueDepthSample));
        Assert.True(playoutInbox.TryRead(out var playoutSample));
        var sampleTrace = attachment.LastOutputTrace.OfType<AudioStructEventSampleTraceRecord>().ToArray();
        Assert.Contains(sampleTrace, record =>
            record.StructEventType == nameof(AudioOutputQueueDepthSample) &&
            record.SequenceNumber == queueDepthSample.SequenceNumber);
        Assert.Contains(sampleTrace, record =>
            record.StructEventType == nameof(AudioOutputPlayoutSample) &&
            record.SequenceNumber == playoutSample.SequenceNumber);
        Assert.Empty(attachment.LastOutputLedger.OfType<ThreadProjectionLedgerRecord>());
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlayedComplete_ProjectsCommittedAssistantOutputWhenPolicyAllows()
    {
        var contentStore = new InMemoryContentStore();
        var thread = new InMemoryThreadProjectionSink();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(),
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = new CompletingAudioOutputSink(),
            EnableAssistantOutputPlayback = true,
            ThreadProjectionSink = thread
        });

        var stream = attachment.WrapModelTurnStreamingAsync(
            CreateModelRequest(contentStore, new EventCoordinator()),
            StreamingHandler,
            CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        var projectedTurn = Assert.Single(thread.ProjectedTurns);
        Assert.Equal(ThreadProjectionKind.AssistantOutput, projectedTurn.Record.Kind);
        Assert.Equal(ThreadProjectionRole.Assistant, projectedTurn.Record.Role);
        Assert.Equal("Hello there. Second sentence.", projectedTurn.Record.Text);
        Assert.NotNull(projectedTurn.Record.OutputFlowId);
        Assert.NotNull(projectedTurn.Record.ResponseId);

        var projectionLedger = Assert.Single(attachment.LastOutputLedger.OfType<ThreadProjectionLedgerRecord>());
        Assert.Equal(projectedTurn.ProjectedEvent, projectionLedger.ProjectedEvent);
        Assert.Contains(attachment.LastOutputLedger.OfType<AssistantOutputLedgerRecord>(), record =>
            record.Disposition == OutputDisposition.PlayedComplete);
        Assert.Contains(attachment.LastOutputTrace.OfType<AudioThreadProjectionTraceRecord>(), record =>
            record.ProjectedEvent == projectedTurn.ProjectedEvent);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlayedComplete_DoesNotProjectWhenPolicyDisablesAssistantOutputProjection()
    {
        var contentStore = new InMemoryContentStore();
        var thread = new InMemoryThreadProjectionSink();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(),
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = new CompletingAudioOutputSink(),
            EnableAssistantOutputPlayback = true,
            ThreadProjectionSink = thread,
            PolicySet = new AudioPolicySet
            {
                ThreadProjection = new ThreadProjectionPolicy
                {
                    ProjectCommittedAssistantOutputs = false
                }
            }
        });

        var stream = attachment.WrapModelTurnStreamingAsync(
            CreateModelRequest(contentStore, new EventCoordinator()),
            StreamingHandler,
            CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Empty(thread.ProjectedTurns);
        Assert.Empty(attachment.LastOutputLedger.OfType<ThreadProjectionLedgerRecord>());
        Assert.Empty(attachment.LastOutputTrace.OfType<AudioThreadProjectionTraceRecord>());
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlaybackFailure_CommitsFailureTruth()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var error = new AudioErrorInfo
        {
            Code = "sink_failed",
            Message = "The sink failed.",
            Category = "Playback"
        };
        var sink = new ScriptedAudioOutputSink(request =>
            request.IsFinalSegment
                ?
                [
                    CreateQueuedEvent(request),
                    new OutputPlaybackFailedEvent
                    {
                        OutputFlowId = request.OutputFlowId,
                        ResponseId = request.ResponseId,
                        SegmentId = request.SegmentId,
                        SegmentIndex = request.SegmentIndex,
                        Error = error
                    }
                ]
                :
                [
                    CreateQueuedEvent(request),
                    CreateStartedEvent(request),
                    CreateCompletedEvent(request)
                ]);
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true
        });
        var coordinator = new EventCoordinator();
        var failedEvents = new List<AssistantAudioPlaybackFailedEvent>();
        var outputCompletedEvents = new List<AssistantAudioOutputCompletedEvent>();
        using var failedSub = coordinator.Subscribe<AssistantAudioPlaybackFailedEvent>(evt =>
        {
            failedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var outputCompletedSub = coordinator.Subscribe<AssistantAudioOutputCompletedEvent>(evt =>
        {
            outputCompletedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore, coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() => failedEvents.Count == 1 && outputCompletedEvents.Count == 1);
        var failed = Assert.Single(failedEvents);
        Assert.Equal("sink_failed", failed.Error.Code);
        Assert.False(failed.Played);
        Assert.False(failed.HeardByUser);
        Assert.False(failed.CanInterrupt);
        Assert.NotNull(failed.EventFlowId);
        var outputCompleted = Assert.Single(outputCompletedEvents);
        Assert.Equal(nameof(OutputCommitDisposition.PlaybackFailed), outputCompleted.Disposition);
        Assert.False(outputCompleted.Played);
        Assert.False(outputCompleted.HeardByUser);

        var playbackLedger = attachment.LastOutputLedger.OfType<OutputPlaybackLedgerRecord>().ToArray();
        Assert.Contains(playbackLedger, record => record.Disposition == OutputPlaybackDisposition.PlaybackFailed);
        Assert.Contains(attachment.LastOutputTrace.OfType<AudioOutputPlaybackTraceRecord>(), record =>
            record.Disposition == OutputPlaybackDisposition.PlaybackFailed);
        Assert.Empty(attachment.LastOutputLedger.OfType<ThreadProjectionLedgerRecord>());
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlaybackInterrupted_CommitsPlayedPrefixTruth()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var thread = new InMemoryThreadProjectionSink();
        var sink = new ScriptedAudioOutputSink(request =>
        {
            if (!request.IsFinalSegment)
            {
                return
                [
                    CreateQueuedEvent(request),
                    CreateStartedEvent(request),
                    CreateCompletedEvent(request)
                ];
            }

            var boundary = new OutputPlaybackBoundary
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                PlayedDuration = TimeSpan.FromMilliseconds(300),
                PlayedTextLength = request.SourceTextStart + 6,
                Precision = OutputAlignmentPrecision.LocalOnly
            };
            return
            [
                CreateQueuedEvent(request),
                CreateStartedEvent(request),
                new OutputPlaybackInterruptedEvent
                {
                    OutputFlowId = request.OutputFlowId,
                    ResponseId = request.ResponseId,
                    SegmentId = request.SegmentId,
                    SegmentIndex = request.SegmentIndex,
                    Boundary = boundary
                }
            ];
        });
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true,
            ThreadProjectionSink = thread
        });
        var coordinator = new EventCoordinator();
        var interruptedEvents = new List<AssistantAudioPlaybackInterruptedEvent>();
        using var interruptedSub = coordinator.Subscribe<AssistantAudioPlaybackInterruptedEvent>(evt =>
        {
            interruptedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore, coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() => interruptedEvents.Count == 1);
        var interrupted = Assert.Single(interruptedEvents);
        Assert.True(interrupted.Played);
        Assert.True(interrupted.HeardByUser);
        Assert.Equal("LocalOnly", interrupted.Precision);
        Assert.False(interrupted.CanInterrupt);
        Assert.NotNull(interrupted.EventFlowId);

        var playbackLedger = attachment.LastOutputLedger.OfType<OutputPlaybackLedgerRecord>().ToArray();
        var interruptedLedger = Assert.Single(playbackLedger, record =>
            record.Disposition == OutputPlaybackDisposition.Interrupted);
        Assert.Equal(interrupted.PlayedTextLength, interruptedLedger.PlayedTextLength);
        Assert.Contains(attachment.LastOutputTrace.OfType<AudioOutputPlaybackTraceRecord>(), record =>
            record.Disposition == OutputPlaybackDisposition.Interrupted);
        var projectedTurn = Assert.Single(thread.ProjectedTurns);
        Assert.Equal(interrupted.PlayedTextLength, projectedTurn.Record.Text.Length);
        Assert.Equal(projectedTurn.Record.Text, "Hello there. Second sentence."[..interrupted.PlayedTextLength]);
        Assert.Single(attachment.LastOutputLedger.OfType<ThreadProjectionLedgerRecord>());
        Assert.Single(attachment.LastOutputTrace.OfType<AudioThreadProjectionTraceRecord>());
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_PlaybackCleared_CommitsQueuedUnplayedWithoutThreadProjection()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var thread = new InMemoryThreadProjectionSink();
        var sink = new ScriptedAudioOutputSink(request =>
        {
            if (!request.IsFinalSegment)
            {
                return
                [
                    CreateQueuedEvent(request),
                    CreateStartedEvent(request),
                    CreateProgressEvent(request),
                    CreateCompletedEvent(request)
                ];
            }

            var boundary = new OutputPlaybackBoundary
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                PlayedDuration = TimeSpan.Zero,
                PlayedTextLength = 0,
                Precision = OutputAlignmentPrecision.LocalOnly
            };
            return
            [
                CreateQueuedEvent(request),
                new OutputPlaybackClearedEvent
                {
                    OutputFlowId = request.OutputFlowId,
                    ResponseId = request.ResponseId,
                    SegmentId = request.SegmentId,
                    SegmentIndex = request.SegmentIndex,
                    Boundary = boundary
                }
            ];
        });
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true,
            ThreadProjectionSink = thread
        });

        var stream = attachment.WrapModelTurnStreamingAsync(
            CreateModelRequest(contentStore, new EventCoordinator()),
            StreamingHandler,
            CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        var playbackLedger = attachment.LastOutputLedger.OfType<OutputPlaybackLedgerRecord>().ToArray();
        var queuedUnplayed = Assert.Single(playbackLedger, record =>
            record.Disposition == OutputPlaybackDisposition.QueuedUnplayed);
        Assert.Equal(0, queuedUnplayed.PlayedTextLength);
        Assert.Equal(TimeSpan.Zero, queuedUnplayed.PlayedDuration);
        Assert.Contains(attachment.LastOutputTrace.OfType<AudioOutputPlaybackTraceRecord>(), record =>
            record.Disposition == OutputPlaybackDisposition.QueuedUnplayed);
        Assert.Empty(thread.ProjectedTurns);
        Assert.Empty(attachment.LastOutputLedger.OfType<ThreadProjectionLedgerRecord>());
        Assert.Empty(attachment.LastOutputTrace.OfType<AudioThreadProjectionTraceRecord>());
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_OutOfOrderFinalCompletion_WaitsForEarlierSegments()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        OutputPlaybackRequest? firstRequest = null;
        var sink = new ScriptedAudioOutputSink(request =>
        {
            if (!request.IsFinalSegment)
            {
                firstRequest = request;
                return
                [
                    CreateQueuedEvent(request),
                    CreateStartedEvent(request)
                ];
            }

            return
            [
                CreateQueuedEvent(request),
                CreateStartedEvent(request),
                CreateCompletedEvent(request),
                CreateCompletedEvent(firstRequest ?? throw new InvalidOperationException("Missing earlier segment."))
            ];
        });
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true
        });

        var stream = attachment.WrapModelTurnStreamingAsync(
            CreateModelRequest(contentStore, new EventCoordinator()),
            StreamingHandler,
            CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        var playbackLedger = attachment.LastOutputLedger.OfType<OutputPlaybackLedgerRecord>().ToArray();
        var earlierProgressIndex = Array.FindIndex(playbackLedger, record =>
            record.SegmentIndex == 0 &&
            record.Disposition == OutputPlaybackDisposition.Progress);
        var finalCompleteIndex = Array.FindIndex(playbackLedger, record =>
            record.SegmentIndex == 1 &&
            record.Disposition == OutputPlaybackDisposition.PlayedComplete);

        Assert.True(earlierProgressIndex >= 0);
        Assert.True(finalCompleteIndex > earlierProgressIndex);
        Assert.Single(playbackLedger, record => record.Disposition == OutputPlaybackDisposition.PlayedComplete);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_SlowTts_DoesNotBlockNextModelUpdate()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new BlockingTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts"
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());
        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);

        Assert.NotNull(stream);
        await using var enumerator = stream!.GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("Hello there. ", ExtractText(enumerator.Current));

        var secondMove = enumerator.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(secondMove, Task.Delay(100));

        Assert.Same(secondMove, completed);
        Assert.True(await secondMove);
        Assert.Equal("Second sentence.", ExtractText(enumerator.Current));

        tts.Release();
        while (await enumerator.MoveNextAsync())
        {
        }

        Assert.Equal(["Hello there.", "Second sentence."], tts.Texts);
        Assert.Equal([0, 1], attachment.LastOutputResults.Select(result => result.SegmentIndex));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_MaxInFlight_AllowsConcurrentSynthesis()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new ConcurrentBlockingTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantOutputPacingOptions = new TextToSpeechPacingOptions
            {
                Continuation = new TextToSpeechContinuationOptions
                {
                    MaxInFlightSynthesisRequests = 2
                }
            }
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());
        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);

        Assert.NotNull(stream);
        var enumerateTask = Task.Run(async () =>
        {
            await foreach (var _ in stream!)
            {
            }
        });

        Exception? waitException = null;
        try
        {
            await tts.WaitForStartedCountAsync(2);
        }
        catch (Exception ex)
        {
            waitException = ex;
        }
        finally
        {
            tts.Release();
        }

        await enumerateTask.WaitAsync(TimeSpan.FromSeconds(1));
        if (waitException is not null)
        {
            throw waitException;
        }

        Assert.Equal(2, tts.Texts.Count);
        Assert.Contains("Hello there.", tts.Texts);
        Assert.Contains("Second sentence.", tts.Texts);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_UnsupportedTtsCapability_EmitsTextOnlyFailure()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = new UnsupportedTextToSpeechClient(),
            AssistantOutputProviderKey = "unsupported-tts"
        });
        var coordinator = new EventCoordinator();
        var failedEvents = new List<AssistantAudioOutputSegmentFailedEvent>();
        using var failedSub = coordinator.Subscribe<AssistantAudioOutputSegmentFailedEvent>(evt =>
        {
            failedEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(new InMemoryContentStore(), coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        await WaitUntilAsync(() => failedEvents.Count > 0);
        Assert.NotEmpty(failedEvents);
        Assert.All(failedEvents, failed =>
        {
            Assert.Equal("UnsupportedTextToSpeechCapability", failed.Error?.Code);
            Assert.Equal("unsupported-tts", failed.ProviderKey);
            Assert.Equal(nameof(OutputCommitDisposition.SynthesisFailedTextOnly), failed.Disposition);
        });
        Assert.All(attachment.LastOutputResults, result =>
            Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_StreamingSpanTts_AssemblesAudioArtifact()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new StreamingOnlyTextToSpeechClient();
        var sink = new CompletingAudioOutputSink();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "streaming-tts",
            AssistantAudioOutputSink = sink,
            EnableAssistantOutputPlayback = true
        });
        var coordinator = new EventCoordinator();
        var chunkEvents = new List<AssistantAudioOutputChunkReadyEvent>();
        using var chunkSub = coordinator.Subscribe<AssistantAudioOutputChunkReadyEvent>(evt =>
        {
            chunkEvents.Add(evt);
            return ValueTask.CompletedTask;
        });
        var request = CreateModelRequest(contentStore, coordinator);

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Equal(["Hello there.", "Second sentence."], tts.Texts);
        Assert.All(attachment.LastOutputResults, result =>
            Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status));
        var artifacts = await contentStore.QueryAsync(ContentScope.Create("session-progressive"));
        Assert.Equal(2, artifacts.Count);
        await WaitUntilAsync(() => chunkEvents.Count == 4);
        Assert.Equal(4, chunkEvents.Count);
        Assert.Equal(4, sink.WriteCount);
        Assert.Equal([0, 1], chunkEvents.Take(2).Select(evt => evt.ChunkSequence));
        Assert.Equal([0, 1], chunkEvents.Skip(2).Select(evt => evt.ChunkSequence));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_AutoRoute_UsesPushTextWhenCapabilityAndFactoryExist()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakePushTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "push-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.Auto
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Empty(tts.Texts);
        Assert.Equal(["Hello there. ", "Second sentence.", ""], tts.Stream.PushedInputs.Select(input => input.Text));
        Assert.True(tts.Stream.PushedInputs.Last().IsFinalInput);
        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status);
        Assert.Equal("Hello there. Second sentence.", result.Text);
        var synthesizedTrace = Assert.Single(result.Trace
            .OfType<AudioTtsSynthesisTraceRecord>()
            .Where(trace => trace.Disposition == TtsSynthesisDisposition.Synthesized));
        Assert.NotNull(synthesizedTrace.ProviderFirstAudioAt);
        Assert.Equal(1, (await contentStore.QueryAsync(ContentScope.Create("session-progressive"))).Count);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_ForceSegment_IgnoresAvailablePushTextFactory()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakePushTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "push-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForceSegment
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Equal(["Hello there.", "Second sentence."], tts.Texts);
        Assert.Empty(tts.Stream.PushedInputs);
        Assert.Equal(2, attachment.LastOutputResults.Count);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_ForcePushText_FailsWhenProviderOnlySupportsSegments()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FakeTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "fake-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForcePushText
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Empty(tts.Texts);
        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status);
        Assert.Equal("PushTextTtsUnsupported", result.Error?.Code);
        Assert.Empty(await contentStore.QueryAsync(ContentScope.Create("session-progressive")));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_AutoRoute_FallsBackWhenPushCapabilityLacksFactory()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new LyingPushTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "lying-push-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.Auto
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Equal(["Hello there.", "Second sentence."], tts.Texts);
        Assert.All(attachment.LastOutputResults, result =>
            Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_AutoRoute_FallsBackWhenFactoryLacksPushCapability()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FactoryOnlyPushTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "factory-only-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.Auto
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Equal(["Hello there.", "Second sentence."], tts.Texts);
        Assert.Empty(tts.Stream.PushedInputs);
        Assert.All(attachment.LastOutputResults, result =>
            Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_ForcePushText_FailsWhenPushCapabilityLacksFactory()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new LyingPushTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "lying-push-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForcePushText
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Empty(tts.Texts);
        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status);
        Assert.Equal("PushTextTtsUnsupported", result.Error?.Code);
        Assert.Contains("does not expose IPushTextToSpeechStreamFactory", result.Error?.Message);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_ForcePushText_FailsWhenFactoryLacksPushCapability()
    {
        var contentStore = new InMemoryContentStore();
        var tts = new FactoryOnlyPushTextToSpeechClient();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "factory-only-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForcePushText
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Empty(tts.Texts);
        Assert.Empty(tts.Stream.PushedInputs);
        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status);
        Assert.Equal("PushTextTtsUnsupported", result.Error?.Code);
        Assert.Contains("does not advertise push-text input support", result.Error?.Message);
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_AutoRoute_FallsBackToSegmentsWhenPushFailsBeforeAudio()
    {
        var contentStore = new InMemoryContentStore();
        var pushStream = new FakePushTextToSpeechStream
        {
            ThrowOnPushText = true
        };
        var tts = new FakePushTextToSpeechClient(pushStream);
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "push-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.Auto
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Equal(["Hello there.", "Second sentence."], tts.Texts);
        Assert.All(attachment.LastOutputResults, result =>
            Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status));
        Assert.Equal(2, await CountArtifactsAsync(contentStore));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_ForcePushText_DoesNotFallbackWhenPushFailsBeforeAudio()
    {
        var contentStore = new InMemoryContentStore();
        var pushStream = new FakePushTextToSpeechStream
        {
            ThrowOnPushText = true
        };
        var tts = new FakePushTextToSpeechClient(pushStream);
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "push-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.ForcePushText
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Empty(tts.Texts);
        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status);
        Assert.Equal("PushTextTtsFailed", result.Error?.Code);
        Assert.Equal(0, await CountArtifactsAsync(contentStore));
    }

    [Fact]
    public async Task WrapModelTurnStreamingAsync_AutoRoute_DoesNotFallbackWhenPushFailsAfterAudio()
    {
        var contentStore = new InMemoryContentStore();
        var pushStream = new FakePushTextToSpeechStream
        {
            ThrowAfterAudio = true
        };
        var tts = new FakePushTextToSpeechClient(pushStream);
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.Progressive,
            PreparedOutputResolver = _ => PreparedOutputExecutionTestFixture.Create(),
            AssistantOutputTextToSpeechClient = tts,
            AssistantOutputProviderKey = "push-tts",
            AssistantOutputProgressiveRouteMode = ProgressiveTextToSpeechRouteMode.Auto
        });
        var request = CreateModelRequest(contentStore, new EventCoordinator());

        var stream = attachment.WrapModelTurnStreamingAsync(request, StreamingHandler, CancellationToken.None);
        await foreach (var _ in stream!)
        {
        }

        Assert.Empty(tts.Texts);
        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status);
        Assert.Equal("PushTextTtsFailed", result.Error?.Code);
        Assert.Equal(0, await CountArtifactsAsync(contentStore));
    }

    private static async IAsyncEnumerable<AgentModelUpdate> StreamingHandler(AgentModelTurnRequest request)
    {
        await Task.Yield();
        yield return new AgentChatModelUpdate(new ChatResponseUpdate
        {
            ResponseId = "response-progressive-1",
            Contents = [new TextContent("Hello there. ")]
        });
        yield return new AgentChatModelUpdate(new ChatResponseUpdate
        {
            ResponseId = "response-progressive-1",
            Contents = [new TextContent("Second sentence.")]
        });
        yield return new AgentChatModelUpdate(new ChatResponseUpdate
        {
            ResponseId = "response-progressive-1",
            FinishReason = ChatFinishReason.Stop
        });
    }

    private static AgentModelTurnRequest CreateModelRequest(
        IContentStore? contentStore,
        EventCoordinator coordinator,
        IStructEventHub? structEvents = null)
    {
        return new AgentModelTurnRequest
        {
            Transport = AgentModelTransport.Chat,
            ChatModel = new FakeChatClient(),
            Messages = [],
            Options = new ChatOptions(),
            State = AgentLoopState.InitialSafe([], "run-progressive", "conversation-progressive", "audio-test-agent"),
            Iteration = 0,
            Session = CreateSession("session-progressive"),
            ContentStore = contentStore,
            EventCoordinator = coordinator,
            EventPublisher = async (evt, ct) =>
            {
                await coordinator.EmitAsync(evt, ct);
                return evt;
            },
            EventFlows = coordinator.EventFlows,
            StructEvents = structEvents
        };
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(
        string assistantText,
        IContentStore? contentStore,
        string responseId = "response-progressive-1")
    {
        var state = AgentLoopState.InitialSafe([], "run-progressive", "conversation-progressive", "audio-test-agent");
        var agentContext = new AgentContext(
            "audio-test-agent",
            "conversation-progressive",
            state,
            new EventCoordinator(),
            session: CreateSession("session-progressive"),
            thread: null,
            CancellationToken.None,
            traceId: "00000000000000000000000000000003",
            contentStore: contentStore,
            structEvents: new HPD.Events.Struct.StructEventHub());
        var assistant = new ChatMessage(ChatRole.Assistant, assistantText);
        var finalResponse = new ChatResponse([assistant])
        {
            ResponseId = responseId
        };
        var factory = typeof(AgentContext).GetMethod(
            "AsAfterMessageTurn",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(AgentContext), "AsAfterMessageTurn");

        return (AfterMessageTurnContext)factory.Invoke(
            agentContext,
            [finalResponse, new List<ChatMessage> { assistant }, new AgentRunConfig()])!;
    }

    private static Session CreateSession(string sessionId)
    {
        var constructor = typeof(Session).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string)],
            modifiers: null)
            ?? throw new MissingMethodException(nameof(Session), ".ctor(string)");

        return (Session)constructor.Invoke([sessionId]);
    }

    private static OutputPlaybackQueuedEvent CreateQueuedEvent(OutputPlaybackRequest request)
    {
        return new OutputPlaybackQueuedEvent
        {
            OutputFlowId = request.OutputFlowId,
            ResponseId = request.ResponseId,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex
        };
    }

    private static OutputPlaybackStartedEvent CreateStartedEvent(OutputPlaybackRequest request)
    {
        return new OutputPlaybackStartedEvent
        {
            OutputFlowId = request.OutputFlowId,
            ResponseId = request.ResponseId,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex
        };
    }

    private static OutputPlaybackCompletedEvent CreateCompletedEvent(OutputPlaybackRequest request)
    {
        return new OutputPlaybackCompletedEvent
        {
            OutputFlowId = request.OutputFlowId,
            ResponseId = request.ResponseId,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            Cursor = new OutputPlaybackCursor
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                PlayedDuration = request.EstimatedDuration ?? TimeSpan.FromSeconds(1),
                PlayedTextLength = request.SourceTextStart + request.SourceTextLength,
                Precision = OutputAlignmentPrecision.LocalOnly
            }
        };
    }

    private static OutputPlaybackProgressEvent CreateProgressEvent(OutputPlaybackRequest request)
    {
        return new OutputPlaybackProgressEvent
        {
            OutputFlowId = request.OutputFlowId,
            ResponseId = request.ResponseId,
            SegmentId = request.SegmentId,
            SegmentIndex = request.SegmentIndex,
            Cursor = new OutputPlaybackCursor
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                PlayedDuration = TimeSpan.FromMilliseconds(250),
                PlayedTextLength = request.SourceTextStart
                    + Math.Max(1, request.SourceTextLength / 2),
                Precision = OutputAlignmentPrecision.LocalOnly
            }
        };
    }

    private static string ExtractText(ChatResponseUpdate update)
    {
        return string.Concat(update.Contents
            .OfType<TextContent>()
            .Select(content => content.Text));
    }

    private static string ExtractText(AgentModelUpdate update)
    {
        return update.ChatUpdate is { } chatUpdate
            ? ExtractText(chatUpdate)
            : string.Empty;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    private static async Task<int> CountArtifactsAsync(InMemoryContentStore contentStore) =>
        (await contentStore.QueryAsync(ContentScope.Create("session-progressive"))).Count;

    private sealed class ScriptedAudioOutputSink(
        Func<OutputPlaybackRequest, IReadOnlyList<OutputPlaybackEvent>> createEvents) : IAudioOutputSink
    {
        private readonly Dictionary<OutputFlowId, Queue<OutputPlaybackEvent>> _events = [];

        public ValueTask<OutputSinkStartResult> StartAsync(
            OutputAudioStream stream,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = CreateRequest(stream);
            var queue = GetQueue(request.OutputFlowId);
            foreach (var playbackEvent in createEvents(request))
            {
                queue.Enqueue(playbackEvent);
            }

            return ValueTask.FromResult(new OutputSinkStartResult
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                Disposition = OutputSinkStartDisposition.Accepted
            });
        }

        public ValueTask WriteAsync(
            OutputAudioChunk chunk,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            OutputAudioStreamCompletion completion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
            OutputFlowId outputFlowId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_events.TryGetValue(outputFlowId, out var queue))
            {
                yield break;
            }

            while (queue.TryDequeue(out var playbackEvent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return playbackEvent;
                await Task.Yield();
            }
        }

        public ValueTask<OutputPlaybackBoundary> InterruptAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new OutputPlaybackBoundary
            {
                OutputFlowId = outputFlowId,
                ResponseId = new ResponseId("response-progressive-1"),
                PlayedTextLength = 0,
                Precision = OutputAlignmentPrecision.LocalOnly
            });
        }

        public ValueTask FlushAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_events.TryGetValue(outputFlowId, out var queue))
            {
                queue.Clear();
            }

            return ValueTask.CompletedTask;
        }

        private Queue<OutputPlaybackEvent> GetQueue(OutputFlowId outputFlowId)
        {
            if (!_events.TryGetValue(outputFlowId, out var queue))
            {
                queue = new Queue<OutputPlaybackEvent>();
                _events.Add(outputFlowId, queue);
            }

            return queue;
        }

        private static OutputPlaybackRequest CreateRequest(OutputAudioStream stream)
        {
            return new OutputPlaybackRequest
            {
                OutputFlowId = stream.OutputFlowId,
                ResponseId = stream.ResponseId,
                SegmentId = stream.SegmentId,
                SegmentIndex = stream.SegmentIndex,
                SourceTextStart = stream.SourceTextStart,
                SourceTextLength = stream.SourceTextLength,
                IsFinalSegment = stream.IsFinalSegment,
                MediaType = stream.MediaType,
                Alignment = stream.Alignment,
                Interruptibility = stream.Interruptibility
            };
        }
    }

    private class FakeTextToSpeechClient : ITextToSpeechClient
    {
        private readonly object _textsGate = new();
        private readonly List<string> _texts = [];

        public IReadOnlyList<string> Texts
        {
            get
            {
                lock (_textsGate)
                {
                    return _texts.ToArray();
                }
            }
        }

        public virtual Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            int count;
            lock (_textsGate)
            {
                _texts.Add(text);
                count = _texts.Count;
            }

            return Task.FromResult(new TextToSpeechResponse([
                new DataContent(new byte[] { 1, 2, 3, (byte)count }, "audio/mpeg")
            ])
            {
                ModelId = options?.ModelId
            });
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new TextToSpeechResponseUpdate((await GetAudioAsync(text, options, cancellationToken)).Contents)
            {
                Kind = TextToSpeechResponseUpdateKind.AudioUpdated
            };
        }

        public virtual object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(TextToSpeechClientMetadata))
            {
                return new TextToSpeechClientMetadata("fake-tts", null, "fake-model");
            }

            if (serviceType == typeof(TextToSpeechCapabilityProfile))
            {
                return new TextToSpeechCapabilityProfile
                {
                    SupportsCompletedTextSynthesis = true
                };
            }

            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class BlockingTextToSpeechClient : FakeTextToSpeechClient
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public override async Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await _release.Task.WaitAsync(cancellationToken);
            return await base.GetAudioAsync(text, options, cancellationToken);
        }
    }

    private sealed class ConcurrentBlockingTextToSpeechClient : FakeTextToSpeechClient
    {
        private readonly TaskCompletionSource _secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public void Release() => _release.TrySetResult();

        public async Task WaitForStartedCountAsync(int count)
        {
            if (Volatile.Read(ref _started) >= count)
            {
                return;
            }

            await _secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }

        public override async Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _started) >= 2)
            {
                _secondStarted.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
            return await base.GetAudioAsync(text, options, cancellationToken);
        }
    }

    private sealed class UnsupportedTextToSpeechClient : ITextToSpeechClient
    {
        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Unsupported client should not be called.");
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(TextToSpeechClientMetadata))
            {
                return new TextToSpeechClientMetadata("unsupported-tts", null, "unsupported-model");
            }

            if (serviceType == typeof(TextToSpeechCapabilityProfile))
            {
                return new TextToSpeechCapabilityProfile
                {
                    SupportsCompletedTextSynthesis = false
                };
            }

            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class StreamingOnlyTextToSpeechClient : ITextToSpeechClient
    {
        public List<string> Texts { get; } = [];

        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Completed text synthesis should not be called.");
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Texts.Add(text);
            await Task.Yield();
            yield return new TextToSpeechResponseUpdate([
                new DataContent(new byte[] { 1, 2 }, "audio/mpeg")
            ])
            {
                Kind = TextToSpeechResponseUpdateKind.AudioUpdated,
                ModelId = options?.ModelId
            };
            yield return new TextToSpeechResponseUpdate([
                new DataContent(new byte[] { 3, 4 }, "audio/mpeg")
            ])
            {
                Kind = TextToSpeechResponseUpdateKind.AudioUpdated,
                ModelId = options?.ModelId
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(TextToSpeechClientMetadata))
            {
                return new TextToSpeechClientMetadata("streaming-tts", null, "streaming-model");
            }

            if (serviceType == typeof(TextToSpeechCapabilityProfile))
            {
                return new TextToSpeechCapabilityProfile
                {
                    SupportsCompletedTextSynthesis = false,
                    SupportsCompletedTextAudioStreaming = true
                };
            }

            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakePushTextToSpeechClient : FakeTextToSpeechClient
    {
        public FakePushTextToSpeechClient()
            : this(new FakePushTextToSpeechStream())
        {
        }

        public FakePushTextToSpeechClient(FakePushTextToSpeechStream stream)
        {
            Stream = stream;
        }

        public FakePushTextToSpeechStream Stream { get; }

        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(TextToSpeechClientMetadata))
            {
                return new TextToSpeechClientMetadata("push-tts", null, "push-model");
            }

            if (serviceType == typeof(TextToSpeechCapabilityProfile))
            {
                return new TextToSpeechCapabilityProfile
                {
                    SupportsCompletedTextSynthesis = true,
                    SupportsPushTextAudioStreaming = true,
                    SupportsCancellationBeforeAudio = true,
                    SupportsCancellationAfterAudio = true
                };
            }

            if (serviceType == typeof(IPushTextToSpeechStreamFactory))
            {
                return new FakePushTextToSpeechStreamFactory(Stream);
            }

            return null;
        }
    }

    private sealed class LyingPushTextToSpeechClient : FakeTextToSpeechClient
    {
        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(TextToSpeechClientMetadata))
            {
                return new TextToSpeechClientMetadata("lying-push-tts", null, "fake-model");
            }

            if (serviceType == typeof(TextToSpeechCapabilityProfile))
            {
                return new TextToSpeechCapabilityProfile
                {
                    SupportsCompletedTextSynthesis = true,
                    SupportsPushTextAudioStreaming = true
                };
            }

            return null;
        }
    }

    private sealed class FactoryOnlyPushTextToSpeechClient : FakeTextToSpeechClient
    {
        public FakePushTextToSpeechStream Stream { get; } = new();

        public override object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(TextToSpeechClientMetadata))
            {
                return new TextToSpeechClientMetadata("factory-only-tts", null, "fake-model");
            }

            if (serviceType == typeof(TextToSpeechCapabilityProfile))
            {
                return new TextToSpeechCapabilityProfile
                {
                    SupportsCompletedTextSynthesis = true,
                    SupportsPushTextAudioStreaming = false
                };
            }

            if (serviceType == typeof(IPushTextToSpeechStreamFactory))
            {
                return new FakePushTextToSpeechStreamFactory(Stream);
            }

            return null;
        }
    }

    private sealed class FakePushTextToSpeechStreamFactory(
        FakePushTextToSpeechStream stream) : IPushTextToSpeechStreamFactory
    {
        public ValueTask<IPushTextToSpeechStream> OpenStreamAsync(
            PushTextToSpeechStreamRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.OpenRequest = request;
            return ValueTask.FromResult<IPushTextToSpeechStream>(stream);
        }
    }

    private sealed class FakePushTextToSpeechStream : IPushTextToSpeechStream
    {
        private readonly Channel<PushTextToSpeechAudioUpdate> _audio = Channel.CreateUnbounded<PushTextToSpeechAudioUpdate>();

        public PushTextToSpeechStreamRequest? OpenRequest { get; set; }

        public List<PushTextToSpeechInput> PushedInputs { get; } = [];

        public bool ThrowOnPushText { get; init; }

        public bool ThrowAfterAudio { get; init; }

        public ValueTask PushTextAsync(
            PushTextToSpeechInput input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PushedInputs.Add(input);
            if (!input.IsFinalInput && ThrowOnPushText)
            {
                throw new InvalidOperationException("push failed before audio");
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask CompleteInputAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _audio.Writer.WriteAsync(
                new PushTextToSpeechAudioUpdate
                {
                    AudioData = new byte[] { 9, 8, 7, 6 },
                    MediaType = "audio/mpeg",
                    ModelId = OpenRequest?.ModelId ?? "push-model"
                },
                cancellationToken);
            _audio.Writer.TryComplete();
        }

        public IAsyncEnumerable<PushTextToSpeechAudioUpdate> ReadAudioAsync(
            CancellationToken cancellationToken = default)
        {
            return ReadAudioCoreAsync(cancellationToken);
        }

        private async IAsyncEnumerable<PushTextToSpeechAudioUpdate> ReadAudioCoreAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var update in _audio.Reader.ReadAllAsync(cancellationToken))
            {
                yield return update;
                if (ThrowAfterAudio)
                {
                    throw new InvalidOperationException("push failed after audio");
                }
            }
        }

        public ValueTask CancelAsync(CancellationToken cancellationToken = default)
        {
            _audio.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _audio.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "unused")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CompletingAudioOutputSink : IAudioOutputSink
    {
        private readonly Dictionary<OutputFlowId, Queue<OutputPlaybackEvent>> _events = [];
        private int _writeCount;

        public int WriteCount => Volatile.Read(ref _writeCount);

        public ValueTask<OutputSinkStartResult> StartAsync(
            OutputAudioStream stream,
            CancellationToken cancellationToken = default)
        {
            var request = new OutputPlaybackRequest
            {
                OutputFlowId = stream.OutputFlowId,
                ResponseId = stream.ResponseId,
                SegmentId = stream.SegmentId,
                SegmentIndex = stream.SegmentIndex,
                SourceTextStart = stream.SourceTextStart,
                SourceTextLength = stream.SourceTextLength,
                IsFinalSegment = stream.IsFinalSegment,
                MediaType = stream.MediaType,
                Alignment = stream.Alignment,
                Interruptibility = stream.Interruptibility
            };
            var queue = GetQueue(request.OutputFlowId);
            queue.Enqueue(new OutputPlaybackQueuedEvent
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex
            });
            queue.Enqueue(new OutputPlaybackStartedEvent
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex
            });
            queue.Enqueue(CreateProgressEvent(request));
            queue.Enqueue(new OutputPlaybackCompletedEvent
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                Cursor = new OutputPlaybackCursor
                {
                    OutputFlowId = request.OutputFlowId,
                    ResponseId = request.ResponseId,
                    SegmentId = request.SegmentId,
                    SegmentIndex = request.SegmentIndex,
                    PlayedDuration = request.EstimatedDuration ?? TimeSpan.FromSeconds(1),
                    PlayedTextLength = request.SourceTextStart + request.SourceTextLength,
                    Precision = OutputAlignmentPrecision.LocalOnly
                }
            });

            return ValueTask.FromResult(new OutputSinkStartResult
            {
                OutputFlowId = request.OutputFlowId,
                ResponseId = request.ResponseId,
                SegmentId = request.SegmentId,
                SegmentIndex = request.SegmentIndex,
                Disposition = OutputSinkStartDisposition.Accepted
            });
        }

        public ValueTask WriteAsync(
            OutputAudioChunk chunk,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writeCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            OutputAudioStreamCompletion completion,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
            OutputFlowId outputFlowId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_events.TryGetValue(outputFlowId, out var queue))
            {
                yield break;
            }

            while (queue.TryDequeue(out var playbackEvent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return playbackEvent;
                await Task.Yield();
            }
        }

        public ValueTask<OutputPlaybackBoundary> InterruptAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new OutputPlaybackBoundary
            {
                OutputFlowId = outputFlowId,
                ResponseId = new ResponseId("response-progressive-1"),
                PlayedTextLength = 0,
                Precision = OutputAlignmentPrecision.LocalOnly
            });
        }

        public ValueTask FlushAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default)
        {
            if (_events.TryGetValue(outputFlowId, out var queue))
            {
                queue.Clear();
            }

            return ValueTask.CompletedTask;
        }

        private Queue<OutputPlaybackEvent> GetQueue(OutputFlowId outputFlowId)
        {
            if (!_events.TryGetValue(outputFlowId, out var queue))
            {
                queue = new Queue<OutputPlaybackEvent>();
                _events.Add(outputFlowId, queue);
            }

            return queue;
        }
    }

    private sealed class RejectingAudioOutputSink : IAudioOutputSink
    {
        private readonly Dictionary<OutputFlowId, Queue<OutputPlaybackEvent>> _events = [];
        private readonly bool _emitFailureEvent;
        private int _startCount;
        private int _writeCount;
        private int _completeCount;

        public RejectingAudioOutputSink(bool emitFailureEvent = true)
        {
            _emitFailureEvent = emitFailureEvent;
        }

        public int StartCount => Volatile.Read(ref _startCount);

        public int WriteCount => Volatile.Read(ref _writeCount);

        public int CompleteCount => Volatile.Read(ref _completeCount);

        public ValueTask<OutputSinkStartResult> StartAsync(
            OutputAudioStream stream,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _startCount);
            var error = new AudioErrorInfo
            {
                Code = "sink_rejected",
                Message = "The test sink rejected the stream.",
                Category = "Playback"
            };
            if (_emitFailureEvent)
            {
                var queue = GetQueue(stream.OutputFlowId);
                queue.Enqueue(new OutputPlaybackFailedEvent
                {
                    OutputFlowId = stream.OutputFlowId,
                    ResponseId = stream.ResponseId,
                    SegmentId = stream.SegmentId,
                    SegmentIndex = stream.SegmentIndex,
                    Error = error
                });
            }

            return ValueTask.FromResult(new OutputSinkStartResult
            {
                OutputFlowId = stream.OutputFlowId,
                ResponseId = stream.ResponseId,
                SegmentId = stream.SegmentId,
                SegmentIndex = stream.SegmentIndex,
                Disposition = OutputSinkStartDisposition.Rejected,
                Error = error
            });
        }

        public ValueTask WriteAsync(
            OutputAudioChunk chunk,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _writeCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask CompleteAsync(
            OutputAudioStreamCompletion completion,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _completeCount);
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<OutputPlaybackEvent> ReadPlaybackEventsAsync(
            OutputFlowId outputFlowId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!_events.TryGetValue(outputFlowId, out var queue))
            {
                yield break;
            }

            while (queue.TryDequeue(out var playbackEvent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return playbackEvent;
                await Task.Yield();
            }
        }

        public ValueTask<OutputPlaybackBoundary> InterruptAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new OutputPlaybackBoundary
            {
                OutputFlowId = outputFlowId,
                ResponseId = new ResponseId("response-progressive-1"),
                PlayedTextLength = 0,
                Precision = OutputAlignmentPrecision.LocalOnly
            });
        }

        public ValueTask FlushAsync(
            OutputFlowId outputFlowId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_events.TryGetValue(outputFlowId, out var queue))
            {
                queue.Clear();
            }

            return ValueTask.CompletedTask;
        }

        private Queue<OutputPlaybackEvent> GetQueue(OutputFlowId outputFlowId)
        {
            if (!_events.TryGetValue(outputFlowId, out var queue))
            {
                queue = new Queue<OutputPlaybackEvent>();
                _events.Add(outputFlowId, queue);
            }

            return queue;
        }
    }
}
