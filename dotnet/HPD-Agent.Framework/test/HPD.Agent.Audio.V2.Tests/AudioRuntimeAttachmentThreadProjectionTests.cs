using System.Reflection;
using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.AgentIntegration.Thread;
using HPD.Agent.Audio.AgentIntegration.Middleware;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Audio.Runtime.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.Meai;
using HPD.Agent.Middleware;
using HPD.Events.Struct;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable EXTEXP0001

public sealed class AudioRuntimeAttachmentThreadProjectionTests
{
    [Fact]
    public async Task AgentBuilder_WithAudioConfig_AutoAttachesAudioRuntimeBeforeContentUpload()
    {
        var agent = await new AgentBuilder(new AgentConfig
        {
            Audio = new AudioConfig
            {
                InputMode = AudioInputMode.ReferenceOnly,
                OutputMode = AudioOutputMode.TextOnly
            }
        })
            .WithDeferredProvider()
            .BuildAsync();

        var audioIndex = IndexOfMiddleware<AudioRuntimeAttachment>(agent.Middlewares);
        var uploadIndex = IndexOfMiddleware<ContentUploadMiddleware>(agent.Middlewares);

        Assert.True(audioIndex >= 0);
        Assert.True(uploadIndex >= 0);
        Assert.True(audioIndex < uploadIndex);
    }

    [Fact]
    public async Task AgentBuilder_WithExplicitAudioRuntimeAttachment_DoesNotAutoAttachDuplicate()
    {
        var agent = await new AgentBuilder(new AgentConfig
        {
            Audio = new AudioConfig
            {
                OutputMode = AudioOutputMode.TextToSpeech
            }
        })
            .WithDeferredProvider()
            .WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
            {
                Enabled = false
            })
            .BuildAsync();

        Assert.Single(agent.Middlewares.OfType<AudioRuntimeAttachment>());
    }

    [Fact]
    public async Task AgentBuilderAudioRuntimeAttachment_RunsBeforeContentUpload_AndPreservesInputMediaIdentity()
    {
        var contentStore = new InMemoryContentStore();
        var agent = await AgentBuilder.Create()
            .WithDeferredProvider()
            .WithContentStore(contentStore)
            .WithAudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
            {
                RunAudioInteractionRuntime = false
            })
            .BuildAsync();

        var audioIndex = IndexOfMiddleware<AudioRuntimeAttachment>(agent.Middlewares);
        var uploadIndex = IndexOfMiddleware<ContentUploadMiddleware>(agent.Middlewares);

        Assert.True(audioIndex >= 0);
        Assert.True(uploadIndex >= 0);
        Assert.True(audioIndex < uploadIndex);

        var attachment = Assert.IsType<AudioRuntimeAttachment>(agent.Middlewares[audioIndex]);
        var upload = Assert.IsType<ContentUploadMiddleware>(agent.Middlewares[uploadIndex]);
        var audio = AudioContent.Wav(new byte[] { 9, 8, 7, 6 });
        audio.Name = "builder-order.wav";
        var context = CreateBeforeMessageTurnContext(
            "session-builder-order",
            new ChatMessage(ChatRole.User, [audio]),
            CreateSession("session-builder-order"));

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Same(audio, context.UserMessage?.Contents.Single());
        var inputContentMetadata = Assert.IsType<AudioInteractionInputMetadata[]>(
            context.UserMessage?.AdditionalProperties?[AudioRuntimeAttachment.AudioInteractionInputsMetadataKey]);
        var inputContent = Assert.Single(inputContentMetadata);
        Assert.Equal(0, inputContent.ContentIndex);
        Assert.Equal("TypedContent", inputContent.SourceKind);
        Assert.Equal("audio/wav", inputContent.MediaType);
        Assert.Equal("builder-order.wav", inputContent.Name);
        Assert.Equal(4, inputContent.SizeBytes);
        Assert.False(string.IsNullOrWhiteSpace(inputContent.Sha256));

        await upload.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.IsType<UriContent>(context.UserMessage?.Contents.Single());
        Assert.IsNotType<TextContent>(context.UserMessage?.Contents.Single());
        Assert.Equal(
            inputContentMetadata,
            context.UserMessage?.AdditionalProperties?[AudioRuntimeAttachment.AudioInteractionInputsMetadataKey]);
    }

    [Fact]
    public async Task BeforeMessageTurn_ProjectsInputMediaTranscript_ToSessionThreadWithoutRawAudio()
    {
        var store = new InMemorySessionStore();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            ThreadProjectionSink = new SessionThreadProjectionSink(store),
            InteractionSessionFactory = new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"middleware transcript:{inputContent.Name ?? inputContent.Id.Value}"
                })
        });

        var audio = AudioContent.Wav(new byte[] { 1, 2, 3, 4 });
        audio.Name = "middleware.wav";
        var message = new ChatMessage(ChatRole.User, [audio]);
        var context = CreateBeforeMessageTurnContext("session-middleware", message);

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Contains(audio, context.UserMessage!.Contents);
        Assert.Contains(context.UserMessage.Contents.OfType<TextContent>(), text =>
            text.Text == "middleware transcript:middleware.wav");

        var loaded = await store.ProjectThreadAsync("session-middleware", "main", ThreadProjectionPurpose.ThreadHistory);
        var events = await store.CollectThreadEventsAsync("session-middleware", "main");

        Assert.NotNull(loaded);
        var projectedMessage = Assert.Single(loaded.Messages);
        Assert.Equal(ChatRole.User, projectedMessage.Role);
        Assert.Equal("middleware transcript:middleware.wav", projectedMessage.Text);

        Assert.NotNull(events);
        Assert.DoesNotContain(events.OfType<ContentAddedEvent>(), e => e.Content is AudioContent or DataContent);

        var textDelta = Assert.Single(events.OfType<TextDeltaEvent>());
        Assert.Equal("middleware transcript:middleware.wav", textDelta.Text);
    }

    [Fact]
    public async Task BeforeMessageTurn_CreatesInteractionFactoryFromInputMediaResolver()
    {
        var store = new InMemorySessionStore();
        var client = new FakeSpeechToTextClient("meai middleware transcript");
        var factoryCreateCount = 0;
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            ThreadProjectionSink = new SessionThreadProjectionSink(store),
            InteractionSessionFactoryResolver = sourceResolver =>
            {
                factoryCreateCount++;
                return new MeaiBatchSpeechToTextInteractionSessionFactory(
                    client,
                    sourceResolver);
            }
        });

        var bytes = new byte[] { 7, 6, 5, 4 };
        var audio = AudioContent.Wav(bytes);
        audio.Name = "meai-middleware.wav";
        var message = new ChatMessage(ChatRole.User, [audio]);
        var context = CreateBeforeMessageTurnContext("session-meai-middleware", message);

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Equal(1, factoryCreateCount);
        Assert.Equal(bytes, client.LastAudioBytes);

        var loaded = await store.ProjectThreadAsync("session-meai-middleware", "main", ThreadProjectionPurpose.ThreadHistory);
        Assert.NotNull(loaded);
        var projectedMessage = Assert.Single(loaded.Messages);
        Assert.Equal(ChatRole.User, projectedMessage.Role);
        Assert.Equal("meai middleware transcript", projectedMessage.Text);

        Assert.Contains(audio, context.UserMessage!.Contents);
        Assert.Contains(context.UserMessage.Contents.OfType<TextContent>(), text =>
            text.Text == "meai middleware transcript");

        var runtimeMetadata = Assert.IsType<AudioInteractionRuntimeMetadata[]>(
            context.UserMessage?.AdditionalProperties?[AudioRuntimeAttachment.AudioInteractionRuntimeResultsKey]);
        Assert.Equal("meai middleware transcript", Assert.Single(runtimeMetadata).Transcript);
    }

    [Fact]
    public async Task BeforeMessageTurn_PassesProviderCandidatesIntoRoute()
    {
        var candidate = new ProviderCapabilityProfile
        {
            ProviderKey = "candidate-stt",
            Declared = new ProviderDeclaredCapabilities
            {
                Flags = ProviderCapabilityFlag.SpeechToText
            }
        };
        var route = new RecordingProviderRoute();
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            ProviderRoute = route,
            ProviderCandidates = [candidate],
            InteractionSessionFactory = new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"candidate transcript:{inputContent.Name}"
                })
        });
        var audio = AudioContent.Wav(new byte[] { 1, 2, 3 });
        audio.Name = "candidate-flow.wav";
        var context = CreateBeforeMessageTurnContext(
            "session-candidate-flow",
            new ChatMessage(ChatRole.User, [audio]));

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.NotNull(route.LastRequest);
        var passedCandidate = Assert.Single(route.LastRequest!.Candidates);
        Assert.Equal("candidate-stt", passedCandidate.ProviderKey);
        Assert.Contains(context.UserMessage!.Contents.OfType<TextContent>(), text =>
            text.Text == "candidate transcript:candidate-flow.wav");
    }

    [Fact]
    public async Task BeforeMessageTurn_RouteByProviderCapability_InjectsCommittedTranscriptAndKeepsAgentModelTurn()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            ProviderCandidates =
            [
                new ProviderCapabilityProfile
                {
                    ProviderKey = "stt-runtime",
                    Declared = new ProviderDeclaredCapabilities
                    {
                        Flags = ProviderCapabilityFlag.SpeechToText
                    }
                }
            ],
            ProviderRoute = new FakeProviderRoute(providerKey: "stt-runtime"),
            InteractionSessionFactory = new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"stt transcript:{inputContent.Name}"
                })
        });
        var audio = AudioContent.Wav(new byte[] { 4, 5, 6 });
        audio.Name = "inputContent-audio.wav";
        var context = CreateBeforeMessageTurnContext(
            "session-inputContent-audio",
            new ChatMessage(ChatRole.User, [audio]));

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Contains(context.UserMessage!.Contents.OfType<TextContent>(), text =>
            text.Text == "stt transcript:inputContent-audio.wav");

        var metadata = Assert.Single(Assert.IsType<AudioInteractionRuntimeMetadata[]>(
            context.UserMessage.AdditionalProperties[AudioRuntimeAttachment.AudioInteractionRuntimeResultsKey]));
        Assert.Equal("stt-runtime", metadata.ProviderKey);
        Assert.Equal(nameof(ProviderRouteDecisionKind.OpenCandidate), metadata.RouteDecisionKind);
        Assert.Equal(nameof(AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech), metadata.Topology);
        Assert.Equal(nameof(ProviderResponseOwnership.HpdChatOwnsResponse), metadata.ResponseOwnership);
        Assert.Empty(metadata.AssistantOutputTexts);
    }

    [Fact]
    public async Task BeforeMessageTurn_TextOnlyDefault_DoesNotRunAudioInteractionRuntime()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText
        });
        var message = new ChatMessage(ChatRole.User, [new TextContent("tell me a joke")]);
        var context = CreateBeforeMessageTurnContext("session-text-only-default", message);

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Empty(attachment.LastResults);
        Assert.False(context.UserMessage!.AdditionalProperties?.ContainsKey(
            AudioRuntimeAttachment.AudioInteractionRuntimeResultsKey) ?? false);
    }

    [Fact]
    public async Task BeforeMessageTurn_RealtimeTransport_DoesNotRunSplitAudioInteractionRuntime()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            InteractionSessionFactory = new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"should not run:{inputContent.Name}"
                })
        });
        var audio = AudioContent.Wav(CreatePcm16Wav(
            sampleRate: 16000,
            channelCount: 1,
            samples: [0, 1200, -1200, 0]));
        audio.Name = "native-realtime.wav";
        var message = new ChatMessage(ChatRole.User, [audio]);
        var context = CreateBeforeMessageTurnContext(
            "session-native-realtime",
            message,
            runConfig: new AgentRunConfig
            {
                ModelTransport = AgentModelTransportMode.Realtime
            });

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Empty(attachment.LastResults);
        var preparedAudio = Assert.IsType<AudioContent>(context.UserMessage!.Contents.Single());
        Assert.Equal("audio/pcm;rate=24000", preparedAudio.MediaType);
        Assert.Equal("native-realtime.pcm", preparedAudio.Name);
        Assert.NotEmpty(preparedAudio.Data.ToArray());
        Assert.DoesNotContain(context.UserMessage.Contents.OfType<TextContent>(), text =>
            text.Text.StartsWith("should not run:", StringComparison.Ordinal));

        var inputContentMetadata = Assert.Single(Assert.IsType<AudioInteractionInputMetadata[]>(
            context.UserMessage.AdditionalProperties![AudioRuntimeAttachment.AudioInteractionInputsMetadataKey]));
        Assert.Equal("native-realtime.wav", inputContentMetadata.Name);
        Assert.False(context.UserMessage.AdditionalProperties.ContainsKey(
            AudioRuntimeAttachment.AudioInteractionRuntimeResultsKey));
    }

    [Fact]
    public async Task BeforeMessageTurn_RealtimeTransport_PreparesInputMp3AsPcm()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions());
        var path = FindRepoFile(
            "test",
            "HPD-Agent.AudioCli",
            "freesound_community-how-are-you-doing-today-103598.mp3");
        var audio = await AudioContent.FromFileAsync(path);
        var context = CreateBeforeMessageTurnContext(
            "session-native-realtime-mp3",
            new ChatMessage(ChatRole.User, [audio]),
            runConfig: new AgentRunConfig
            {
                ModelTransport = AgentModelTransportMode.Realtime
            });

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        var preparedAudio = Assert.IsType<AudioContent>(context.UserMessage!.Contents.Single());
        Assert.Equal("audio/pcm;rate=24000", preparedAudio.MediaType);
        Assert.Equal("freesound_community-how-are-you-doing-today-103598.pcm", preparedAudio.Name);
        Assert.True(preparedAudio.Data.Length > audio.Data.Length);
        Assert.Empty(attachment.LastResults);
    }

    [Fact]
    public async Task BeforeIteration_InputMediaRuntimeMetadata_DoesNotSkipAgentModelTurn()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            ProviderCandidates =
            [
                new ProviderCapabilityProfile
                {
                    ProviderKey = "stt-runtime",
                    Declared = new ProviderDeclaredCapabilities
                    {
                        Flags = ProviderCapabilityFlag.SpeechToText
                    }
                }
            ],
            ProviderRoute = new FakeProviderRoute(providerKey: "stt-runtime"),
            InteractionSessionFactory = new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"stt transcript:{inputContent.Name}"
                })
        });
        var audio = AudioContent.Wav(new byte[] { 4, 5, 6 });
        audio.Name = "inputContent-audio.wav";
        var beforeMessageContext = CreateBeforeMessageTurnContext(
            "session-inputContent-audio-skip",
            new ChatMessage(ChatRole.User, [audio]));

        await attachment.BeforeMessageTurnAsync(beforeMessageContext, CancellationToken.None);
        var messages = new List<ChatMessage> { beforeMessageContext.UserMessage! };
        var beforeIterationContext = CreateBeforeIterationContext(
            "session-inputContent-audio-skip",
            messages);

        await ((IAgentMiddleware)attachment).BeforeIterationAsync(beforeIterationContext, CancellationToken.None);

        Assert.False(beforeIterationContext.SkipLLMCall);
        Assert.Null(beforeIterationContext.OverrideResponse);
    }

    [Fact]
    public async Task BeforeMessageTurn_TranscribeOnly_StillInjectsCommittedTranscript()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            PolicySet = new()
            {
                InputMedia = new InputMediaPolicy
                {
                    HandlingMode = InputMediaHandlingMode.TranscribeOnly
                }
            },
            ProviderCandidates =
            [
                new ProviderCapabilityProfile
                {
                    ProviderKey = "transcribe-only",
                    Declared = new ProviderDeclaredCapabilities
                    {
                        Flags = ProviderCapabilityFlag.SpeechToText
                    }
                }
            ],
            ProviderRoute = new FakeProviderRoute(providerKey: "transcribe-only"),
            InteractionSessionFactory = new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"transcribe-only transcript:{inputContent.Name}"
                })
        });
        var audio = AudioContent.Wav(new byte[] { 7, 8, 9 });
        audio.Name = "transcribe-only.wav";
        var context = CreateBeforeMessageTurnContext(
            "session-transcribe-only",
            new ChatMessage(ChatRole.User, [audio]));

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Contains(context.UserMessage!.Contents.OfType<TextContent>(), text =>
            text.Text == "transcribe-only transcript:transcribe-only.wav");
        var metadata = Assert.Single(Assert.IsType<AudioInteractionRuntimeMetadata[]>(
            context.UserMessage.AdditionalProperties![AudioRuntimeAttachment.AudioInteractionRuntimeResultsKey]));
        Assert.Equal(nameof(ProviderResponseOwnership.HpdChatOwnsResponse), metadata.ResponseOwnership);
    }

    [Fact]
    public async Task BeforeIteration_TranscribeOnly_DoesNotSkipNormalChat()
    {
        var attachment = new AudioRuntimeAttachment(new AudioRuntimeAttachmentOptions
        {
            PolicySet = new()
            {
                InputMedia = new InputMediaPolicy
                {
                    HandlingMode = InputMediaHandlingMode.TranscribeOnly
                }
            },
            ProviderCandidates =
            [
                new ProviderCapabilityProfile
                {
                    ProviderKey = "transcribe-only",
                    Declared = new ProviderDeclaredCapabilities
                    {
                        Flags = ProviderCapabilityFlag.SpeechToText
                    }
                }
            ],
            ProviderRoute = new FakeProviderRoute(providerKey: "transcribe-only"),
            InteractionSessionFactory = new FakeAudioInteractionSessionFactory(
                options: new FakeAudioInteractionSessionOptions
                {
                    TranscriptFactory = inputContent => $"transcribe-only transcript:{inputContent.Name}"
                })
        });
        var audio = AudioContent.Wav(new byte[] { 7, 8, 9 });
        audio.Name = "transcribe-only.wav";
        var beforeMessageContext = CreateBeforeMessageTurnContext(
            "session-transcribe-only-no-skip",
            new ChatMessage(ChatRole.User, [audio]));

        await attachment.BeforeMessageTurnAsync(beforeMessageContext, CancellationToken.None);
        var beforeIterationContext = CreateBeforeIterationContext(
            "session-transcribe-only-no-skip",
            [beforeMessageContext.UserMessage!]);

        await ((IAgentMiddleware)attachment).BeforeIterationAsync(beforeIterationContext, CancellationToken.None);

        Assert.False(beforeIterationContext.SkipLLMCall);
        Assert.Null(beforeIterationContext.OverrideResponse);
    }

    [Fact]
    public void UseSpeechToTextProvider_ConfiguresInteractionSessionFactoryResolver()
    {
        var registry = new ProviderRegistry();
        registry.Register(new FakeSpeechToTextClientProvider(
            "fake-stt",
            new FakeSpeechToTextClient("configured")));
        var options = new AudioRuntimeAttachmentOptions();

        Assert.Null(options.InteractionSessionFactoryResolver);

        options.UseSpeechToTextProvider(
            registry,
            new ProviderClientConfig
            {
                ProviderKey = "fake-stt",
                ModelName = "config-model"
            });

        Assert.NotNull(options.InteractionSessionFactoryResolver);
        var factory = options.InteractionSessionFactoryResolver!(
            new EmptyInputContentSourceResolver());
        Assert.NotNull(factory);
    }

    [Fact]
    public async Task BeforeMessageTurn_SpeechToTextProviderBridge_ProjectsCommittedTranscriptWithoutRawAudio()
    {
        var store = new InMemorySessionStore();
        var client = new FakeSpeechToTextClient("provider registry transcript");
        var provider = new FakeSpeechToTextClientProvider("fake-stt", client);
        var registry = new ProviderRegistry();
        registry.Register(provider);

        var options = new AudioRuntimeAttachmentOptions
        {
            ThreadProjectionSink = new SessionThreadProjectionSink(store)
        };
        options.UseSpeechToTextProvider(
            registry,
            new InputMediaSpeechToTextProviderOptions
            {
                ProviderKey = "fake-stt",
                ModelId = "test-model",
                SpeechLanguage = "en-US",
                SpeechSampleRate = 16_000
            });
        var attachment = new AudioRuntimeAttachment(options);

        var bytes = new byte[] { 5, 4, 3, 2 };
        var audio = AudioContent.Wav(bytes);
        audio.Name = "provider-bridge.wav";
        var message = new ChatMessage(ChatRole.User, [audio]);
        var context = CreateBeforeMessageTurnContext("session-provider-bridge", message);

        await attachment.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Equal(1, provider.CreateCount);
        Assert.NotNull(provider.LastConfig);
        Assert.Equal("fake-stt", provider.LastConfig.ProviderKey);
        Assert.Equal("test-model", provider.LastConfig.ModelName);
        Assert.Equal(bytes, client.LastAudioBytes);
        Assert.NotNull(client.LastOptions);
        Assert.Equal("test-model", client.LastOptions.ModelId);
        Assert.Equal("en-US", client.LastOptions.SpeechLanguage);
        Assert.Equal(16_000, client.LastOptions.SpeechSampleRate);
        Assert.True(client.IsDisposed);

        var loaded = await store.ProjectThreadAsync("session-provider-bridge", "main", ThreadProjectionPurpose.ThreadHistory);
        var events = await store.CollectThreadEventsAsync("session-provider-bridge", "main");

        Assert.NotNull(loaded);
        var projectedMessage = Assert.Single(loaded.Messages);
        Assert.Equal(ChatRole.User, projectedMessage.Role);
        Assert.Equal("provider registry transcript", projectedMessage.Text);

        Assert.NotNull(events);
        Assert.DoesNotContain(events.OfType<ContentAddedEvent>(), e => e.Content is AudioContent or DataContent);
        var textDelta = Assert.Single(events.OfType<TextDeltaEvent>());
        Assert.Equal("provider registry transcript", textDelta.Text);
    }

    [Fact]
    public async Task BeforeMessageTurn_SpeechToTextProviderBridge_MissingProviderThrowsUsefulException()
    {
        var options = new AudioRuntimeAttachmentOptions();
        options.UseSpeechToTextProvider(
            new ProviderRegistry(),
            new InputMediaSpeechToTextProviderOptions
            {
                ProviderKey = "missing-stt"
            });
        var attachment = new AudioRuntimeAttachment(options);
        var audio = AudioContent.Wav(new byte[] { 1, 2, 3 });
        var context = CreateBeforeMessageTurnContext(
            "session-missing-provider",
            new ChatMessage(ChatRole.User, [audio]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attachment.BeforeMessageTurnAsync(context, CancellationToken.None));

        Assert.Contains("Provider 'missing-stt' is not registered", exception.Message);
    }

    [Fact]
    public async Task BeforeMessageTurn_SpeechToTextProviderBridge_WrongProviderFamilyThrowsUsefulException()
    {
        var registry = new ProviderRegistry();
        registry.Register(new WrongFamilyProvider("fake-chat"));
        var options = new AudioRuntimeAttachmentOptions();
        options.UseSpeechToTextProvider(
            registry,
            new InputMediaSpeechToTextProviderOptions
            {
                ProviderKey = "fake-chat"
            });
        var attachment = new AudioRuntimeAttachment(options);
        var audio = AudioContent.Wav(new byte[] { 1, 2, 3 });
        var context = CreateBeforeMessageTurnContext(
            "session-wrong-family",
            new ChatMessage(ChatRole.User, [audio]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attachment.BeforeMessageTurnAsync(context, CancellationToken.None));

        Assert.Contains("does not support client family 'SpeechToText'", exception.Message);
    }

    [Fact]
    public void AudioRuntimeAttachmentOptions_DoesNotExposeTranscriptKnobs()
    {
        var properties = typeof(AudioRuntimeAttachmentOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain("ScriptedTranscript", properties);
        Assert.DoesNotContain("TranscriptFactory", properties);
        Assert.Contains("InteractionSessionFactory", properties);
        Assert.Contains("InteractionSessionFactoryResolver", properties);
    }

    private static BeforeMessageTurnContext CreateBeforeMessageTurnContext(
        string conversationId,
        ChatMessage userMessage,
        Session? session = null,
        AgentRunConfig? runConfig = null)
    {
        var state = AgentLoopState.InitialSafe([], "run-audio-middleware", conversationId, "audio-test-agent");
        var thread = session is null ? null : CreateThread(session, "main");
        var agentContext = new AgentContext(
            "audio-test-agent",
            conversationId,
            state,
            new EventCoordinator(),
            session: session,
            thread: thread,
            CancellationToken.None,
            traceId: "00000000000000000000000000000001",
            structEvents: new HPD.Events.Struct.StructEventHub());

        var factory = typeof(AgentContext).GetMethod(
            "AsBeforeMessageTurn",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(AgentContext), "AsBeforeMessageTurn");

        return (BeforeMessageTurnContext)factory.Invoke(
            agentContext,
            [userMessage, new List<ChatMessage>(), runConfig ?? new AgentRunConfig()])!;
    }

    private static BeforeIterationContext CreateBeforeIterationContext(
        string conversationId,
        List<ChatMessage> messages,
        int iteration = 0)
    {
        var state = AgentLoopState.InitialSafe([], "run-audio-middleware", conversationId, "audio-test-agent");
        var agentContext = new AgentContext(
            "audio-test-agent",
            conversationId,
            state,
            new EventCoordinator(),
            session: null,
            thread: null,
            CancellationToken.None,
            traceId: "00000000000000000000000000000001",
            structEvents: new HPD.Events.Struct.StructEventHub());

        var factory = typeof(AgentContext).GetMethod(
            "AsBeforeIteration",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(AgentContext), "AsBeforeIteration");

        return (BeforeIterationContext)factory.Invoke(
            agentContext,
            [iteration, messages, new ChatOptions(), new AgentRunConfig()])!;
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

    private static Thread CreateThread(Session session, string threadId)
    {
        var factory = typeof(Session).GetMethod(
            "CreateThread",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(Session), "CreateThread");

        return (Thread)factory.Invoke(session, ["audio-test-agent", threadId])!;
    }

    private static int IndexOfMiddleware<TMiddleware>(IReadOnlyList<IAgentMiddleware> middlewares)
        where TMiddleware : IAgentMiddleware
    {
        for (var i = 0; i < middlewares.Count; i++)
        {
            if (middlewares[i] is TMiddleware)
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class FakeSpeechToTextClient : ISpeechToTextClient
    {
        private readonly string _transcript;

        public FakeSpeechToTextClient(string transcript)
        {
            _transcript = transcript;
        }

        public byte[]? LastAudioBytes { get; private set; }

        public SpeechToTextOptions? LastOptions { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            audioSpeechStream.CopyTo(copy);
            LastOptions = options;
            LastAudioBytes = copy.ToArray();
            return Task.FromResult(new SpeechToTextResponse(_transcript));
        }

        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return (await GetTextAsync(audioSpeechStream, options, cancellationToken))
                .ToSpeechToTextResponseUpdates()
                .Single();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this)
                ? this
                : null;

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class FakeSpeechToTextClientProvider(
        string providerKey,
        FakeSpeechToTextClient client) : ISpeechToTextClientProvider
    {
        public string ProviderKey => providerKey;

        public string DisplayName => "Fake STT";

        public int CreateCount { get; private set; }

        public ProviderClientConfig? LastConfig { get; private set; }

        public ISpeechToTextClient CreateSpeechToTextClient(
            ProviderClientConfig config,
            IServiceProvider? services = null)
        {
            CreateCount++;
            LastConfig = config;
            return client;
        }

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata()
            => new()
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
                {
                    [ProviderClientFamily.SpeechToText] = new()
                    {
                        Family = ProviderClientFamily.SpeechToText
                    }
                }
            };

        public ProviderValidationResult ValidateConfiguration(
            ProviderClientConfig config,
            ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class WrongFamilyProvider(string providerKey) : IProvider
    {
        public string ProviderKey => providerKey;

        public string DisplayName => "Wrong Family";

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata()
            => new()
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
                {
                    [ProviderClientFamily.Chat] = new()
                    {
                        Family = ProviderClientFamily.Chat
                    }
                }
            };

        public ProviderValidationResult ValidateConfiguration(
            ProviderClientConfig config,
            ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class EmptyInputContentSourceResolver : IInputContentSourceResolver
    {
        public ValueTask<InputContentSourceOpenResult> OpenAsync(
            InputContentRef inputContent,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InputContentSourceOpenResult.NotResolved(
                inputContent.Id,
                InputContentSourceOpenStatus.NotFound,
                "not configured"));
    }

    private sealed class RecordingProviderRoute : IProviderRoute
    {
        public ProviderRouteId Id { get; } = new("recording-route");

        public ProviderRouteState State { get; private set; } = ProviderRouteState.Ready;

        public ProviderRouteEpoch CurrentEpoch { get; private set; } = new()
        {
            Id = new ProviderRouteEpochId("recording-route-epoch-0000"),
            ProviderKey = "recording-route",
            StartedAt = DateTimeOffset.UnixEpoch
        };

        public ProviderRouteRequest? LastRequest { get; private set; }

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
            LastRequest = request;
            State = ProviderRouteState.Active;
            var candidate = request.Candidates.Single();
            CurrentEpoch = new ProviderRouteEpoch
            {
                Id = new ProviderRouteEpochId("recording-route-epoch-0001"),
                ProviderKey = candidate.ProviderKey,
                StartedAt = DateTimeOffset.UtcNow
            };

            return ValueTask.FromResult(new ProviderRouteDecision
            {
                RouteId = Id,
                Kind = ProviderRouteDecisionKind.OpenCandidate,
                Epoch = CurrentEpoch,
                Plan = new InteractionExecutionPlan
                {
                    Topology = AudioInteractionTopology.SplitSpeechToTextChatTextToSpeech,
                    RouteEpoch = CurrentEpoch,
                    Capabilities = candidate
                },
                Reason = "recorded-candidate"
            });
        }

        public ValueTask DisposeAsync()
        {
            State = ProviderRouteState.Stopped;
            return ValueTask.CompletedTask;
        }
    }

    private static byte[] CreatePcm16Wav(
        int sampleRate,
        short channelCount,
        IReadOnlyList<short> samples)
    {
        var dataSize = samples.Count * sizeof(short);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channelCount);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channelCount * sizeof(short));
        writer.Write((short)(channelCount * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        return stream.ToArray();
    }

    private static string FindRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. pathParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find repo file '{Path.Combine(pathParts)}' from '{AppContext.BaseDirectory}'.");
    }
}

#pragma warning restore EXTEXP0001
