using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.AgentIntegration.Output;
using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Audio.Runtime.Scenarios;
using HPD.Agent.Providers.Audio.Meai;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

#pragma warning disable EXTEXP0001
#pragma warning disable MEAI001

public sealed class AudioRuntimeConsumerSetupTests
{
    [Fact]
    public async Task AgentBuilder_WithFakeTextToSpeechClient_BuildsAndSynthesizesAssistantOutput()
    {
        var chatClient = new CapturingChatClient("hello from assistant");
        var textToSpeechClient = new FakeTextToSpeechClient([1, 2, 3, 4]);
        var contentStore = new InMemoryContentStore();
        var artifacts = new List<AssistantAudioOutputArtifactCapturedEvent>();

        var agent = await AgentBuilder.Create()
            .WithChatClient(chatClient)
            .WithContentStore(contentStore)
            .WithAudioRuntimeAttachment(options =>
            {
                options.AssistantOutputSynthesisMode = AssistantOutputSynthesisMode.FinalText;
                options.AssistantOutputTextToSpeechClient = textToSpeechClient;
                options.AssistantOutputProviderKey = "fake-tts";
                options.AssistantOutputModelId = "fake-model";
                options.AssistantOutputVoiceId = "fake-voice";
                options.AssistantOutputFormat = "mp3";
            })
            .BuildAsync();

        using var subscription = agent.Subscribe<AssistantAudioOutputArtifactCapturedEvent>(artifacts.Add);

        await agent.CreateSessionAsync("consumer-tts-session");
        var result = await agent.RunAsync("Say hello.", sessionId: "consumer-tts-session");

        Assert.Equal("hello from assistant", result.Text);
        Assert.Equal("hello from assistant", textToSpeechClient.LastText);

        await WaitUntilAsync(() => artifacts.Count == 1);
        var artifact = Assert.Single(artifacts);
        Assert.Equal("consumer-tts-session", artifact.SessionId);
        Assert.Equal("audio/mpeg", artifact.MediaType);
        Assert.Equal(4, artifact.SizeBytes);

        var storedBytes = await contentStore.ReadBytesAsync(new ContentAddress(
            ContentScope.Create("consumer-tts-session"), artifact.Artifact.ArtifactId));
        Assert.Equal([1, 2, 3, 4], storedBytes);
    }

    [Fact]
    public async Task AgentBuilder_WithFakeSpeechToTextBridge_BuildsAndProjectsTranscriptIntoModelInput()
    {
        var chatClient = new CapturingChatClient("assistant saw transcript");
        var speechToTextClient = new FakeSpeechToTextClient("transcribed audio");
        var resolver = new AnyInputContentResolver([9, 8, 7, 6]);

        var agent = await AgentBuilder.Create()
            .WithChatClient(chatClient)
            .WithAudioRuntimeAttachment(options =>
            {
                options.InteractionSessionFactory =
                    new MeaiBatchSpeechToTextInteractionSessionFactory(speechToTextClient, resolver);
            })
            .BuildAsync();

        await agent.CreateSessionAsync("consumer-stt-session");
        var audio = AudioContent.Wav(new byte[] { 9, 8, 7, 6 });
        audio.Name = "question.wav";

        var result = await agent.RunAsync(new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, [audio])],
            SessionId = "consumer-stt-session",
            ThreadId = "main"
        });

        Assert.Equal("assistant saw transcript", result.TurnResult.Text);
        Assert.Equal([9, 8, 7, 6], speechToTextClient.LastAudioBytes);
        Assert.Contains(chatClient.LastMessages, message =>
            message.Role == ChatRole.User &&
            message.Text?.Contains("transcribed audio", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(condition());
    }

    private sealed class CapturingChatClient(string responseText) : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)])
            {
                ResponseId = "consumer-response"
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToArray();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, responseText)
            {
                ResponseId = "consumer-response"
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeTextToSpeechClient(byte[] audio) : ITextToSpeechClient
    {
        public string? LastText { get; private set; }

        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastText = text;
            return Task.FromResult(new TextToSpeechResponse([
                new DataContent(audio, "audio/mpeg")
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

    private sealed class FakeSpeechToTextClient(string transcript) : ISpeechToTextClient
    {
        public byte[]? LastAudioBytes { get; private set; }

        public async Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await using var copy = new MemoryStream();
            await audioSpeechStream.CopyToAsync(copy, cancellationToken);
            LastAudioBytes = copy.ToArray();
            return new SpeechToTextResponse(transcript);
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
        }
    }

    private sealed class AnyInputContentResolver(byte[] bytes) : IInputContentSourceResolver
    {
        public ValueTask<InputContentSourceOpenResult> OpenAsync(
            InputContentRef inputContent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(InputContentSourceOpenResult.Opened(new InputContentSource
            {
                InputContentId = inputContent.Id,
                MediaType = inputContent.MediaType ?? "audio/wav",
                Name = inputContent.Name,
                SizeBytes = bytes.LongLength,
                Sha256 = inputContent.Sha256,
                OpenStreamAsync = ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
                }
            }));
        }
    }
}

#pragma warning restore MEAI001
#pragma warning restore EXTEXP0001
