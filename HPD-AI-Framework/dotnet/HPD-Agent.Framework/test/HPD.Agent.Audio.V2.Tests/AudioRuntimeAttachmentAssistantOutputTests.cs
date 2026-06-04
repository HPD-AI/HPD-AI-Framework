using System.Reflection;
using HPD.Agent;
using HPD.Agent.Audio.AgentIntegration;
using HPD.Agent.Audio.AgentIntegration.Middleware;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Trace;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable MEAI001

public sealed class AudioRuntimeAttachmentAssistantOutputTests
{
    [Fact]
    public async Task AfterMessageTurn_DefaultOptions_DoNotSynthesize()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(new byte[] { 1, 2, 3 })
        });
        var readyEvents = new List<AssistantAudioOutputArtifactCapturedEvent>();
        var failedEvents = new List<AssistantAudioOutputFailedEvent>();
        var subscriptions = new List<IDisposable>();
        var context = CreateAfterMessageTurnContext(
            "hello from assistant",
            configureCoordinator: coordinator =>
            {
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(evt =>
                {
                    readyEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputFailedEvent>(evt =>
                {
                    failedEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
            });

        await attachment.AfterMessageTurnAsync(context, CancellationToken.None);

        Assert.Empty(attachment.LastOutputResults);
        Assert.Empty(readyEvents);
        Assert.Empty(failedEvents);
    }

    [Fact]
    public async Task AfterMessageTurn_EnabledTts_SynthesizesArtifactWithoutPlaybackClaim()
    {
        var workspaceStore = new InMemoryWorkspaceStore();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText,
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(new byte[] { 1, 2, 3, 4 }),
            AssistantOutputProviderKey = "fake-tts",
            AssistantOutputModelId = "voice-model",
            AssistantOutputVoiceId = "voice-1",
            AssistantOutputFormat = "mp3"
        });
        var readyEvents = new List<AssistantAudioOutputArtifactCapturedEvent>();
        var failedEvents = new List<AssistantAudioOutputFailedEvent>();
        var subscriptions = new List<IDisposable>();
        var context = CreateAfterMessageTurnContext(
            "hello from assistant",
            workspaceStore,
            coordinator =>
            {
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(evt =>
                {
                    readyEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputFailedEvent>(evt =>
                {
                    failedEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
            });

        await attachment.AfterMessageTurnAsync(context, CancellationToken.None);

        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status);
        Assert.Equal("hello from assistant", result.Text);
        Assert.Equal(OutputCommitDisposition.SynthesizedNotPlayed, result.Commit?.Disposition);
        var artifactRecord = Assert.Single(result.Ledger.OfType<OutputArtifactLedgerRecord>());
        var artifactRef = artifactRecord.Artifact;
        Assert.Equal("hpd-content", artifactRef.Store);
        Assert.Equal("audio/mpeg", artifactRef.MediaType);
        Assert.Equal(4, artifactRef.SizeBytes);
        Assert.False(string.IsNullOrWhiteSpace(artifactRef.Sha256));

        var storedInfo = await workspaceStore.StatAsync("session-output", artifactRef.ArtifactId);
        Assert.NotNull(storedInfo);
        Assert.Equal("audio/mpeg", storedInfo.ContentType);
        Assert.Equal(4, storedInfo.SizeBytes);
        Assert.Equal(ContentSource.Agent, storedInfo.Origin);
        Assert.Equal(WorkspaceContentRoles.Artifact, storedInfo.Role);
        Assert.Equal(WorkspaceContentPaths.BranchArtifacts("session-output", "main"), storedInfo.PathHint);
        Assert.Equal("assistant-audio", storedInfo.Tags?["kind"]);
        Assert.Equal("fake-tts", storedInfo.Tags?["provider"]);
        Assert.Equal("voice-model", storedInfo.Tags?["model"]);
        Assert.Equal("voice-1", storedInfo.Tags?["voice"]);

        var storedBytes = await workspaceStore.ReadBytesAsync("session-output", artifactRef.ArtifactId);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, storedBytes);

        Assert.Contains(result.Ledger.OfType<AssistantOutputLedgerRecord>(), r =>
            r.Disposition == OutputDisposition.SynthesizedNotPlayed);
        Assert.Contains(result.Ledger.OfType<TtsSynthesisRequestedLedgerRecord>(), r =>
            r.ProviderKey == "fake-tts" &&
            r.ModelId == "voice-model" &&
            r.VoiceId == "voice-1");
        Assert.Contains(result.Ledger.OfType<TtsSynthesisResultLedgerRecord>(), r =>
            r.Disposition == TtsSynthesisDisposition.Synthesized &&
            r.SizeBytes == 4);
        Assert.Equal(OutputArtifactKind.SynthesizedAudio, artifactRecord.Kind);

        var artifactTrace = Assert.Single(result.Trace.OfType<AudioOutputArtifactTraceRecord>());
        Assert.NotNull(artifactTrace.Artifact);

        await WaitUntilAsync(() => readyEvents.Count == 1);
        var ready = Assert.Single(readyEvents);
        Assert.Empty(failedEvents);
        Assert.Equal(result.OutputFlowId.Value, ready.OutputFlowId);
        Assert.Equal(result.ResponseId.Value, ready.ResponseId);
        Assert.Equal("audio/mpeg", ready.MediaType);
        Assert.Equal(artifactRef, ready.Artifact);
        Assert.Equal(4, ready.SizeBytes);
    }

    [Fact]
    public async Task AfterMessageTurn_TtsFailure_DegradesToTextOnlyAndDoesNotThrow()
    {
        var workspaceStore = new InMemoryWorkspaceStore();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText,
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(new InvalidOperationException("tts down")),
            AssistantOutputProviderKey = "fake-tts"
        });
        var readyEvents = new List<AssistantAudioOutputArtifactCapturedEvent>();
        var failedEvents = new List<AssistantAudioOutputFailedEvent>();
        var subscriptions = new List<IDisposable>();
        var context = CreateAfterMessageTurnContext(
            "still keep text",
            workspaceStore,
            coordinator =>
            {
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(evt =>
                {
                    readyEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputFailedEvent>(evt =>
                {
                    failedEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
            });

        await attachment.AfterMessageTurnAsync(context, CancellationToken.None);

        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status);
        Assert.Equal("still keep text", result.Text);
        Assert.Equal(OutputCommitDisposition.SynthesisFailedTextOnly, result.Commit?.Disposition);
        Assert.NotNull(result.Error);
        Assert.Empty(await workspaceStore.QueryAsync("session-output"));
        Assert.Contains(result.Ledger.OfType<TtsSynthesisResultLedgerRecord>(), r =>
            r.Disposition == TtsSynthesisDisposition.Failed &&
            r.Error is not null);
        Assert.Contains(result.Ledger.OfType<AssistantOutputLedgerRecord>(), r =>
            r.Disposition == OutputDisposition.SynthesisFailedTextOnly);

        Assert.Empty(readyEvents);
        await WaitUntilAsync(() => failedEvents.Count == 1);
        var failed = Assert.Single(failedEvents);
        Assert.Equal(result.OutputFlowId.Value, failed.OutputFlowId);
        Assert.Equal(result.ResponseId.Value, failed.ResponseId);
        Assert.Equal("fake-tts", failed.ProviderKey);
        Assert.Equal(nameof(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly), failed.Disposition);
        Assert.Equal("InvalidOperationException", failed.Error?.Code);
    }

    [Fact]
    public async Task AfterMessageTurn_EnabledTtsWithoutWorkspaceStore_ReturnsClearTextOnlyFailure()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText,
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(new byte[] { 1, 2, 3 }),
            AssistantOutputProviderKey = "fake-tts"
        });
        var readyEvents = new List<AssistantAudioOutputArtifactCapturedEvent>();
        var failedEvents = new List<AssistantAudioOutputFailedEvent>();
        var subscriptions = new List<IDisposable>();
        var context = CreateAfterMessageTurnContext(
            "still keep text",
            workspaceStore: null,
            coordinator =>
            {
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(evt =>
                {
                    readyEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputFailedEvent>(evt =>
                {
                    failedEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
            });

        await attachment.AfterMessageTurnAsync(context, CancellationToken.None);

        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesisFailedTextOnly, result.Status);
        Assert.Equal("still keep text", result.Text);
        Assert.Equal("MissingWorkspaceStore", result.Error?.Code);
        Assert.Empty(result.Ledger.OfType<OutputArtifactLedgerRecord>());

        Assert.Empty(readyEvents);
    }

    [Fact]
    public async Task AfterMessageTurn_DisabledArtifactCaptureWithoutWorkspaceStore_SynthesizesWithoutArtifact()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText,
            AssistantOutputTextToSpeechClient = new FakeTextToSpeechClient(new byte[] { 1, 2, 3 }),
            AssistantOutputProviderKey = "fake-tts",
            AssistantOutputArtifactCapturePolicy = AssistantAudioArtifactCapturePolicy.Disabled
        });
        var readyEvents = new List<AssistantAudioOutputArtifactCapturedEvent>();
        var failedEvents = new List<AssistantAudioOutputFailedEvent>();
        var subscriptions = new List<IDisposable>();
        var context = CreateAfterMessageTurnContext(
            "stream only",
            workspaceStore: null,
            coordinator =>
            {
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(evt =>
                {
                    readyEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
                subscriptions.Add(coordinator.Subscribe<AssistantAudioOutputFailedEvent>(evt =>
                {
                    failedEvents.Add(evt);
                    return ValueTask.CompletedTask;
                }));
            });

        await attachment.AfterMessageTurnAsync(context, CancellationToken.None);

        var result = Assert.Single(attachment.LastOutputResults);
        Assert.Equal(AssistantTextToSpeechOutputStatus.SynthesizedNotPlayed, result.Status);
        Assert.Equal("stream only", result.Text);
        Assert.Null(result.Error);
        Assert.Empty(result.Ledger.OfType<OutputArtifactLedgerRecord>());
        Assert.Empty(readyEvents);
        Assert.Empty(failedEvents);
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(
        string assistantText,
        IWorkspaceStore? workspaceStore = null,
        Action<EventCoordinator>? configureCoordinator = null)
    {
        var state = AgentLoopState.InitialSafe([], "run-audio-output", "conversation-output", "audio-test-agent");
        var coordinator = new EventCoordinator();
        configureCoordinator?.Invoke(coordinator);
        var agentContext = new AgentContext(
            "audio-test-agent",
            "conversation-output",
            state,
            coordinator,
            session: CreateSession("session-output"),
            branch: null,
            CancellationToken.None,
            traceId: "00000000000000000000000000000002",
            workspaceStore: workspaceStore);
        var assistant = new ChatMessage(ChatRole.Assistant, assistantText);
        var finalResponse = new ChatResponse([assistant])
        {
            ResponseId = "assistant-response-1"
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

    private sealed class FakeTextToSpeechClient : ITextToSpeechClient
    {
        private readonly byte[]? _audio;
        private readonly Exception? _exception;

        public FakeTextToSpeechClient(byte[] audio)
        {
            _audio = audio;
        }

        public FakeTextToSpeechClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(new TextToSpeechResponse([
                new DataContent(_audio!, "audio/mpeg")
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

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return serviceType == typeof(TextToSpeechClientMetadata)
                ? new TextToSpeechClientMetadata("fake-tts", null, "fake-model")
                : null;
        }

        public void Dispose()
        {
        }
    }

}
