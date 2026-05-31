// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Realtime;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Events;
using HPD.Events.Struct;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.Tests;

#pragma warning disable MEAI001

public sealed class RealtimeAudioModeTests
{
    [Fact]
    public async Task StartAsync_CreatesAndStopAsync_DisposesRealtimeSession()
    {
        var realtimeClient = new FakeRealtimeClient();
        var agent = await CreateRealtimeAgentAsync(realtimeClient);

        await agent.StartAsync();
        await agent.StopAsync();

        Assert.Equal(1, realtimeClient.CreateSessionCallCount);
        Assert.True(realtimeClient.Session.DisposeCalled);
    }

    [Fact]
    public async Task AudioInputFrame_SendsAppendCommitAndCreateResponseMessages()
    {
        var realtimeClient = new FakeRealtimeClient();
        var agent = await CreateRealtimeAgentAsync(realtimeClient);

        await agent.StartAsync();
        await agent.RunAsync(new AudioInputFrame(
            SessionId: null,
            BranchId: "main",
            Audio: new byte[] { 1, 2, 3 },
            MimeType: "audio/pcm",
            TimestampNs: 0,
            IsFinal: true));

        await WaitUntilAsync(() => realtimeClient.Session.SentMessages.Count >= 3);
        await agent.StopAsync();

        var append = Assert.IsType<InputAudioBufferAppendRealtimeClientMessage>(
            realtimeClient.Session.SentMessages[0]);
        Assert.Equal([1, 2, 3], append.Content.Data.ToArray());
        Assert.Equal("audio/pcm", append.Content.MediaType);
        Assert.IsType<InputAudioBufferCommitRealtimeClientMessage>(
            realtimeClient.Session.SentMessages[1]);
        Assert.IsType<CreateResponseRealtimeClientMessage>(
            realtimeClient.Session.SentMessages[2]);
    }

    [Fact]
    public async Task RealtimeOutputAudioDelta_ProjectsAudioChunkEventAndLocalFrame()
    {
        using var coordinator = new HPD.Events.Core.EventCoordinator();
        using var structEvents = new StructEventHub();
        var projector = new RealtimeEventProjector(coordinator, structEvents, "agent");
        var chunkCompletion = new TaskCompletionSource<AudioChunkEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var frames = structEvents.Route<AudioOutputFrame>().Subscribe();
        using var subscription = coordinator.Subscribe<AudioChunkEvent>(evt =>
        {
            chunkCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        projector.Project(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-1"
        });
        projector.Project(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputAudioDelta)
        {
            ResponseId = "response-1",
            Audio = Convert.ToBase64String([4, 5, 6])
        });

        var chunk = await chunkCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var frame = await ReadFrameAsync(frames);

        Assert.Equal("audio/pcm", chunk.MimeType);
        Assert.Equal([4, 5, 6], Convert.FromBase64String(chunk.Base64Audio));
        Assert.Equal([4, 5, 6], frame.Audio.ToArray());
    }

    [Fact]
    public async Task ResponseCreatedAndDone_ProjectBranchRunLifecycle()
    {
        var realtimeClient = new FakeRealtimeClient();
        var agent = await CreateRealtimeAgentAsync(realtimeClient);
        var startedCompletion = new TaskCompletionSource<BranchRunStartedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completedCompletion = new TaskCompletionSource<BranchRunCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<BranchRunStartedEvent>(evt =>
        {
            startedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<BranchRunCompletedEvent>(evt =>
        {
            completedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync();
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-2"
        });
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "response-2",
            Status = RealtimeResponseStatus.Completed
        });

        var started = await startedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completed = await completedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await agent.StopAsync();

        Assert.Equal("response-2", started.RuntimeRunId);
        Assert.Equal("response-2", completed.RuntimeRunId);
        Assert.False(completed.Cancelled);
    }

    [Fact]
    public async Task OutputAudioTranscription_ProjectsTranscriptionEvents()
    {
        using var coordinator = new HPD.Events.Core.EventCoordinator();
        using var structEvents = new StructEventHub();
        var projector = new RealtimeEventProjector(coordinator, structEvents, "agent");
        var completedCompletion = new TaskCompletionSource<TranscriptionCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var deltas = new List<TranscriptionDeltaEvent>();

        using var deltaSubscription = coordinator.Subscribe<TranscriptionDeltaEvent>(evt =>
        {
            deltas.Add(evt);
            return ValueTask.CompletedTask;
        });
        using var completedSubscription = coordinator.Subscribe<TranscriptionCompletedEvent>(evt =>
        {
            completedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        projector.Project(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-transcript"
        });
        projector.Project(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputAudioTranscriptionDelta)
        {
            ResponseId = "response-transcript",
            Text = "hel"
        });
        projector.Project(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputAudioTranscriptionDelta)
        {
            ResponseId = "response-transcript",
            Text = "lo"
        });
        projector.Project(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputAudioTranscriptionDone)
        {
            ResponseId = "response-transcript"
        });

        var completed = await completedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("hello", completed.FinalText);
        Assert.Equal(3, deltas.Count);
        Assert.False(deltas[0].IsFinal);
        Assert.False(deltas[1].IsFinal);
        Assert.True(deltas[2].IsFinal);
        Assert.Equal("hello", deltas[2].Text);
        Assert.Equal("response-transcript", deltas[2].EventFlowId);
    }

    [Fact]
    public async Task ErrorRealtimeServerMessage_CompletesActiveBranchRunAsFailed()
    {
        var realtimeClient = new FakeRealtimeClient();
        var agent = await CreateRealtimeAgentAsync(realtimeClient);
        var completedCompletion = new TaskCompletionSource<BranchRunCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<BranchRunCompletedEvent>(evt =>
        {
            completedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync();
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-error"
        });
        realtimeClient.Session.Emit(new ErrorRealtimeServerMessage
        {
            Error = new ErrorContent("realtime exploded")
            {
                ErrorCode = "realtime_failed"
            }
        });

        var completed = await completedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await agent.StopAsync();

        Assert.Equal("response-error", completed.RuntimeRunId);
        Assert.False(completed.Cancelled);
        Assert.Equal("realtime_failed", completed.ErrorType);
        Assert.Equal("realtime exploded", completed.ErrorMessage);
    }

    [Fact]
    public async Task ResponseDone_Cancelled_EmitsInterruptionAndCompletesCancelledBranchRun()
    {
        var realtimeClient = new FakeRealtimeClient();
        var agent = await CreateRealtimeAgentAsync(realtimeClient);
        var interruptedCompletion = new TaskCompletionSource<UserInterruptedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completedCompletion = new TaskCompletionSource<BranchRunCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<UserInterruptedEvent>(evt =>
        {
            interruptedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<BranchRunCompletedEvent>(evt =>
        {
            completedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync();
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-cancelled"
        });
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "response-cancelled",
            Status = RealtimeResponseStatus.Cancelled,
            Error = new ErrorContent("barge in")
        });

        var interrupted = await interruptedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completed = await completedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await agent.StopAsync();

        Assert.Equal("response-cancelled", interrupted.EventFlowId);
        Assert.Equal("barge in", interrupted.TranscribedText);
        Assert.Equal("response-cancelled", completed.RuntimeRunId);
        Assert.True(completed.Cancelled);
    }

    [Fact]
    public async Task LongRunningRealtimeTool_DoesNotBlockReceiveLoopErrorProjection()
    {
        var realtimeClient = new FakeRealtimeClient();
        var toolStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toolGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowTool = AIFunctionFactory.Create(
            async (string city) =>
            {
                toolStarted.TrySetResult();
                await toolGate.Task.ConfigureAwait(false);
                return $"weather:{city}";
            },
            name: "SlowWeather");
        var agent = await CreateRealtimeAgentAsync(
            realtimeClient,
            tools: [slowTool]);
        var completedCompletion = new TaskCompletionSource<BranchRunCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<BranchRunCompletedEvent>(evt =>
        {
            completedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync();
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-slow-tool"
        });
        realtimeClient.Session.Emit(new ResponseOutputItemRealtimeServerMessage(
            RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = "response-slow-tool",
            Item = new RealtimeConversationItem(
            [
                new FunctionCallContent(
                    "call-slow-weather",
                    "SlowWeather",
                    new Dictionary<string, object?> { ["city"] = "Accra" })
            ])
        });

        await toolStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        realtimeClient.Session.Emit(new ErrorRealtimeServerMessage
        {
            Error = new ErrorContent("tool still running")
            {
                ErrorCode = "receive_loop_alive"
            }
        });

        var completed = await completedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        toolGate.TrySetResult();
        await agent.StopAsync();

        Assert.Equal("response-slow-tool", completed.RuntimeRunId);
        Assert.Equal("receive_loop_alive", completed.ErrorType);
    }

    [Fact]
    public async Task TextInputDuringRealtimeRuntime_SendsConversationItemAndSkipsChatModel()
    {
        var realtimeClient = new FakeRealtimeClient();
        var chatClient = new CountingChatClient();
        var agent = await CreateRealtimeAgentAsync(realtimeClient, chatClient);

        await agent.StartAsync();
        await agent.RunAsync("hello realtime");

        await WaitUntilAsync(() => realtimeClient.Session.SentMessages.Count >= 2);
        await agent.StopAsync();

        var itemMessage = Assert.IsType<CreateConversationItemRealtimeClientMessage>(
            realtimeClient.Session.SentMessages[0]);
        var text = Assert.IsType<TextContent>(Assert.Single(itemMessage.Item.Contents));
        Assert.Equal("hello realtime", text.Text);
        Assert.IsType<CreateResponseRealtimeClientMessage>(
            realtimeClient.Session.SentMessages[1]);
        Assert.Equal(0, chatClient.StreamingCallCount);
    }

    [Fact]
    public async Task ResponseOutputItemDone_WithFunctionCall_ExecutesToolAndSendsResult()
    {
        var realtimeClient = new FakeRealtimeClient();
        var tool = AIFunctionFactory.Create(
            (string city) => $"weather:{city}",
            name: "GetWeather");
        var agent = await CreateRealtimeAgentAsync(
            realtimeClient,
            tools: [tool]);

        await agent.StartAsync();
        realtimeClient.Session.Emit(new ResponseOutputItemRealtimeServerMessage(
            RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = "response-tools",
            Item = new RealtimeConversationItem(
            [
                new FunctionCallContent(
                    "call-weather",
                    "GetWeather",
                    new Dictionary<string, object?> { ["city"] = "Accra" })
            ])
        });

        await WaitUntilAsync(() => realtimeClient.Session.SentMessages.Count >= 2);
        await agent.StopAsync();

        var resultMessage = Assert.IsType<CreateConversationItemRealtimeClientMessage>(
            realtimeClient.Session.SentMessages[0]);
        var result = Assert.IsType<FunctionResultContent>(
            Assert.Single(resultMessage.Item.Contents));
        Assert.Equal("call-weather", result.CallId);
        Assert.Equal("weather:Accra", result.Result?.ToString());
        Assert.IsType<CreateResponseRealtimeClientMessage>(
            realtimeClient.Session.SentMessages[1]);
    }

    [Fact]
    public async Task OutputWithoutResponseCreated_SynthesizesBranchRunStartBeforeCompletion()
    {
        var realtimeClient = new FakeRealtimeClient();
        var agent = await CreateRealtimeAgentAsync(realtimeClient);
        var startedCompletion = new TaskCompletionSource<BranchRunStartedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completedCompletion = new TaskCompletionSource<BranchRunCompletedEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        agent.Subscribe<BranchRunStartedEvent>(evt =>
        {
            startedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        agent.Subscribe<BranchRunCompletedEvent>(evt =>
        {
            completedCompletion.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });

        await agent.StartAsync();
        await agent.RunAsync("bind session");
        await WaitUntilAsync(() => realtimeClient.Session.SentMessages.Count >= 2);

        realtimeClient.Session.Emit(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputTextDelta)
        {
            ResponseId = "response-synthesized-start",
            Text = "hello"
        });
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "response-synthesized-start",
            Status = RealtimeResponseStatus.Completed
        });

        var started = await startedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completed = await completedCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await agent.StopAsync();

        Assert.Equal("response-synthesized-start", started.RuntimeRunId);
        Assert.Equal("response-synthesized-start", completed.RuntimeRunId);
    }

    [Fact]
    public async Task StartAsync_UsesRealtimeOverrideClientFromAudioRunConfig()
    {
        var defaultRealtimeClient = new FakeRealtimeClient();
        var overrideRealtimeClient = new FakeRealtimeClient();
        var chatClient = new CountingChatClient();
        var agent = await CreateRealtimeAgentAsync(defaultRealtimeClient, chatClient);

        await agent.StartAsync(new AgentRunConfig
        {
            Audio = new AudioRunConfig
            {
                Realtime = new RealtimeAudioConfig
                {
                    OverrideClient = overrideRealtimeClient
                }
            }
        });
        await agent.RunAsync(
            "hello override");

        await WaitUntilAsync(() => overrideRealtimeClient.Session.SentMessages.Count >= 2);
        await agent.StopAsync();

        Assert.Equal(0, defaultRealtimeClient.CreateSessionCallCount);
        Assert.False(defaultRealtimeClient.Session.DisposeCalled);
        Assert.Equal(1, overrideRealtimeClient.CreateSessionCallCount);
        Assert.Empty(defaultRealtimeClient.Session.SentMessages);
        Assert.Equal(2, overrideRealtimeClient.Session.SentMessages.Count);
    }

    [Fact]
    public async Task StartAsync_UsesRealtimeClientProviderFromAudioRunConfig()
    {
        var defaultRealtimeClient = new FakeRealtimeClient();
        var overrideRealtimeClient = new FakeRealtimeClient();
        var registry = new ProviderRegistry();
        var defaultProvider = new FakeRealtimeProvider("default-realtime", defaultRealtimeClient);
        var overrideProvider = new FakeRealtimeProvider("override-realtime", overrideRealtimeClient);
        registry.Register(defaultProvider);
        registry.Register(overrideProvider);
        var config = new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            IOMode = AudioIOMode.AudioToAudioAndText,
            Realtime = new RealtimeAudioConfig()
        };

        var agent = await new AgentBuilder(new AgentConfig
            {
                Name = "RealtimeProviderOverrideAgent",
                AgentId = "realtime-provider-override-agent",
                Clients = new AgentClientConfig
                {
                    Realtime = new ClientProviderConfig
                    {
                        ProviderKey = "default-realtime",
                        ModelName = "default-model"
                    }
                }
            },
            registry)
            .WithChatClient(new CountingChatClient())
            .UseRealtimeAudio(config)
            .BuildAsync();

        await agent.StartAsync(new AgentRunConfig
        {
            Audio = new AudioRunConfig
            {
                Realtime = new RealtimeAudioConfig
                {
                    Client = new ClientProviderConfig
                    {
                        ProviderKey = "override-realtime",
                        ModelName = "override-model"
                    }
                }
            }
        });
        await agent.RunAsync("hello provider override");

        await WaitUntilAsync(() => overrideRealtimeClient.Session.SentMessages.Count >= 2);
        await agent.StopAsync();

        Assert.Equal(1, defaultProvider.CreateRealtimeClientCallCount);
        Assert.Equal(1, overrideProvider.CreateRealtimeClientCallCount);
        Assert.Equal(0, defaultRealtimeClient.CreateSessionCallCount);
        Assert.Equal(1, overrideRealtimeClient.CreateSessionCallCount);
        Assert.Empty(defaultRealtimeClient.Session.SentMessages);
        Assert.Equal("override-model", overrideRealtimeClient.Session.Options?.Model);
    }

    [Fact]
    public async Task ResponseLifecycle_WithAudioFrameScope_PersistsBranchRunEvents()
    {
        var realtimeClient = new FakeRealtimeClient();
        var store = new InMemorySessionStore();
        var contentStore = new InMemoryContentStore();
        var session = new Session("session-realtime");
        var branch = session.CreateBranch("main");
        await store.SaveSessionAsync(session);
        await store.SaveInitialBranchAsync(session.Id, branch);

        var agent = await CreateRealtimeAgentAsync(
            realtimeClient,
            sessionStore: store,
            contentStore: contentStore);

        await agent.StartAsync();
        await agent.RunAsync(new AudioInputFrame(
            SessionId: session.Id,
            BranchId: branch.Id,
            Audio: new byte[] { 9 },
            MimeType: "audio/pcm",
            TimestampNs: 0,
            IsFinal: false));
        await WaitUntilAsync(() => realtimeClient.Session.SentMessages.Count >= 1);

        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-persisted"
        });
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "response-persisted",
            Status = RealtimeResponseStatus.Completed
        });

        await WaitUntilAsync(async () =>
        {
            var doc = await store.LoadBranchDocumentAsync(session.Id, branch.Id);
            return doc?.Events.OfType<BranchRunCompletedEvent>()
                .Any(evt => evt.RuntimeRunId == "response-persisted") == true;
        });
        await agent.StopAsync();

        var document = await store.LoadBranchDocumentAsync(session.Id, branch.Id);
        Assert.Contains(document!.Events.OfType<BranchRunStartedEvent>(),
            evt => evt.RuntimeRunId == "response-persisted" &&
                   evt.SessionId == session.Id &&
                   evt.BranchId == branch.Id);
        Assert.Contains(document.Events.OfType<BranchRunCompletedEvent>(),
            evt => evt.RuntimeRunId == "response-persisted" &&
                   evt.SessionId == session.Id &&
                   evt.BranchId == branch.Id);
    }

    [Fact]
    public async Task ResponseDone_WithRealtimeAudio_UploadsAssembledAudioArtifact()
    {
        var realtimeClient = new FakeRealtimeClient();
        var store = new InMemorySessionStore();
        var contentStore = new InMemoryContentStore();
        var session = new Session("session-realtime-artifact");
        var branch = session.CreateBranch("main");
        await store.SaveSessionAsync(session);
        await store.SaveInitialBranchAsync(session.Id, branch);

        var agent = await CreateRealtimeAgentAsync(
            realtimeClient,
            sessionStore: store,
            contentStore: contentStore);

        await agent.StartAsync();
        await agent.RunAsync(new AudioInputFrame(
            SessionId: session.Id,
            BranchId: branch.Id,
            Audio: new byte[] { 1 },
            MimeType: "audio/pcm",
            TimestampNs: 0,
            IsFinal: false));
        await WaitUntilAsync(() => realtimeClient.Session.SentMessages.Count >= 1);

        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseCreated)
        {
            ResponseId = "response-audio-artifact"
        });
        realtimeClient.Session.Emit(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputAudioDelta)
        {
            ResponseId = "response-audio-artifact",
            Audio = Convert.ToBase64String([2, 3]),
            RawRepresentation = new DataContent(Array.Empty<byte>(), "audio/opus")
        });
        realtimeClient.Session.Emit(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputAudioDelta)
        {
            ResponseId = "response-audio-artifact",
            Audio = Convert.ToBase64String([4])
        });
        realtimeClient.Session.Emit(new OutputTextAudioRealtimeServerMessage(
            RealtimeServerMessageType.OutputAudioDone)
        {
            ResponseId = "response-audio-artifact"
        });
        realtimeClient.Session.Emit(new ResponseCreatedRealtimeServerMessage(
            RealtimeServerMessageType.ResponseDone)
        {
            ResponseId = "response-audio-artifact",
            Status = RealtimeResponseStatus.Completed
        });

        await WaitUntilAsync(async () =>
            (await contentStore.QueryAsync(session.Id)).Count == 1);
        await agent.StopAsync();

        var artifacts = await contentStore.QueryAsync(session.Id);
        var artifact = Assert.Single(artifacts);
        Assert.Equal("/artifacts", artifact.Tags?["folder"]);
        Assert.Equal("realtime", artifact.Tags?["audio-role"]);
        Assert.Equal("response-audio-artifact", artifact.Tags?["response-id"]);
        Assert.Equal("false", artifact.Tags?["interrupted"]);
        Assert.Equal("audio/opus", artifact.ContentType);

        var content = await contentStore.ReadBytesAsync(session.Id, artifact.Id);
        Assert.Equal([2, 3, 4], content);
    }

    private static async Task<Agent> CreateRealtimeAgentAsync(
        FakeRealtimeClient realtimeClient,
        IChatClient? chatClient = null,
        IList<AITool>? tools = null,
        ISessionStore? sessionStore = null,
        IContentStore? contentStore = null)
    {
        var config = new AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            IOMode = AudioIOMode.AudioToAudioAndText,
            Realtime = new RealtimeAudioConfig()
        };

        return await new AgentBuilder(new AgentConfig
            {
                Name = "RealtimeTestAgent",
                AgentId = "realtime-test-agent",
                ServerConfiguredTools = tools,
                SessionStore = sessionStore
            })
            .WithChatClient(chatClient ?? new CountingChatClient())
            .WithContentStore(contentStore ?? new InMemoryContentStore())
            .UseRealtimeAudio(config, realtimeClient)
            .BuildAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    private static async Task<AudioOutputFrame> ReadFrameAsync(
        StructEventSubscription<AudioOutputFrame> frames)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!cts.IsCancellationRequested)
        {
            if (frames.TryRead(out var frame))
                return frame;

            await Task.Delay(10, cts.Token);
        }

        throw new TimeoutException("Timed out waiting for realtime audio frame.");
    }

    private sealed class FakeRealtimeClient : IRealtimeClient
    {
        public FakeRealtimeSession Session { get; } = new();
        public int CreateSessionCallCount { get; private set; }

        public Task<IRealtimeClientSession> CreateSessionAsync(
            RealtimeSessionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CreateSessionCallCount++;
            Session.Options = options;
            return Task.FromResult<IRealtimeClientSession>(Session);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeRealtimeProvider(
        string providerKey,
        IRealtimeClient client) : IRealtimeClientProvider
    {
        public string ProviderKey { get; } = providerKey;
        public string DisplayName => ProviderKey;
        public int CreateRealtimeClientCallCount { get; private set; }

        public IRealtimeClient CreateRealtimeClient(
            ClientProviderConfig config,
            IServiceProvider? services = null)
        {
            CreateRealtimeClientCallCount++;
            return client;
        }

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Realtime] = new()
                {
                    Family = ProviderClientFamily.Realtime
                }
            }
        };

        public ProviderValidationResult ValidateConfiguration(
            ClientProviderConfig config,
            ProviderClientFamily family) => ProviderValidationResult.Success();
    }

    private sealed class FakeRealtimeSession : IRealtimeClientSession
    {
        private readonly Channel<RealtimeServerMessage> _messages = Channel.CreateUnbounded<RealtimeServerMessage>();
        private readonly object _sync = new();

        public RealtimeSessionOptions? Options { get; set; }
        public List<RealtimeClientMessage> SentMessages { get; } = [];
        public bool DisposeCalled { get; private set; }

        public Task SendAsync(
            RealtimeClientMessage message,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                SentMessages.Add(message);
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }
        }

        public void Emit(RealtimeServerMessage message) =>
            _messages.Writer.TryWrite(message);

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            _messages.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("fake");
        public int StreamingCallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCallCount++;
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
