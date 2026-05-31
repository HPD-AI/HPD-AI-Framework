// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Realtime;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

#pragma warning disable MEAI001

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Tests for AudioAttachmentTranscriptionMiddleware attachment audio detection.
/// Covers the three audio content shapes: AudioContent, DataContent(audio/*), and
/// UriContent(hpd-content://) post-upload.
/// </summary>
public class AudioAttachmentTranscriptionMiddlewareTests
{
    // -------------------------------------------------------------------------
    // 1. AudioContent typed instance is detected and transcribed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_AudioContent_IsDetectedAndTranscribed()
    {
        // Arrange
        var audioBytes = new byte[] { 0x49, 0x44, 0x33 }; // MP3 ID3 header
        var stt = new FakeSpeechToTextClient("hello world");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(audioBytes, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — audio replaced with transcription text
        var newMessage = context.Messages[^1];
        Assert.Single(newMessage.Contents);
        var text = Assert.IsType<TextContent>(newMessage.Contents[0]);
        Assert.Equal("hello world", text.Text);
        Assert.Equal(1, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 2. Raw DataContent with audio MIME still works (regression guard)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_DataContent_WithAudioMime_IsDetectedAndTranscribed()
    {
        // Arrange
        var audioBytes = new byte[] { 0x49, 0x44, 0x33 };
        var stt = new FakeSpeechToTextClient("hey there");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new DataContent(audioBytes, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        var newMessage = context.Messages[^1];
        var text = Assert.IsType<TextContent>(Assert.Single(newMessage.Contents));
        Assert.Equal("hey there", text.Text);
        Assert.Equal(1, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 3. UriContent(hpd-content://) resolves bytes from content store then transcribes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_UriContent_WithContentReferenceAndAudioMime_ResolvesFromStore()
    {
        // Arrange
        var audioBytes = new byte[] { 0x49, 0x44, 0x33 };
        var contentStore = new InMemoryContentStore();
        var sessionId = "test-session-uri";
        var contentInfo = await contentStore.WriteBytesAsync(sessionId, audioBytes, "audio/mpeg");

        var stt = new FakeSpeechToTextClient("transcribed from store");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var session = CreateSessionWithStore(sessionId, contentStore);
        var uriContent = new UriContent(ContentReferenceResolverMiddleware.CreateContentUri(contentInfo.Id), "audio/mpeg");
        var message = new ChatMessage(ChatRole.User, [uriContent]);
        var context = CreateBeforeIterationContext(session, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — STT received the resolved bytes
        var newMessage = context.Messages[^1];
        var text = Assert.IsType<TextContent>(Assert.Single(newMessage.Contents));
        Assert.Equal("transcribed from store", text.Text);
        Assert.Equal(1, stt.CallCount);
        Assert.Equal(audioBytes, stt.LastReceivedBytes);
    }

    // -------------------------------------------------------------------------
    // 4. Content not found in store - item silently skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_UriContent_AssetNotFoundInStore_IsSkipped()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var stt = new FakeSpeechToTextClient("never called");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var session = CreateSessionWithStore("session-missing", contentStore);
        // Note: nothing put in the store, so the reference does not exist.
        var uriContent = new UriContent(new Uri("hpd-content://nonexistent-id"), "audio/mpeg");
        var message = new ChatMessage(ChatRole.User, [uriContent]);
        var context = CreateBeforeIterationContext(session, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — message unchanged, STT not called
        Assert.Equal(0, stt.CallCount);
        Assert.IsType<UriContent>(context.Messages[^1].Contents[0]); // still there
    }

    // -------------------------------------------------------------------------
    // 5. UriContent with https:// scheme is not treated as audio input
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_UriContent_WithHttpScheme_IsIgnored()
    {
        // Arrange
        var stt = new FakeSpeechToTextClient("should not be called");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var uriContent = new UriContent(new Uri("https://example.com/audio.mp3"), "audio/mpeg");
        var message = new ChatMessage(ChatRole.User, [uriContent]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — no transcription, message unchanged
        Assert.Equal(0, stt.CallCount);
        Assert.IsType<UriContent>(context.Messages[^1].Contents[0]);
    }

    // -------------------------------------------------------------------------
    // 6. No content store: hpd-content:// UriContent skipped gracefully
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_NoContentStore_UriContentIsSkipped()
    {
        // Arrange — session with a store that returns null for GetContentStore
        var stt = new FakeSpeechToTextClient("should not be called");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var store = new NullContentSessionStore();
        var session = new SessionModel("no-store-session") { Store = store };
        var uriContent = new UriContent(new Uri("hpd-content://some-id"), "audio/mpeg");
        var message = new ChatMessage(ChatRole.User, [uriContent]);
        var context = CreateBeforeIterationContext(session, messages: [message]);

        // Act — must not throw
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(0, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 7. Mixed content: only audio extracted; text and image preserved
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_MixedContent_OnlyAudioExtracted()
    {
        // Arrange
        var audioBytes = new byte[] { 0x49, 0x44, 0x33 };
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG
        var stt = new FakeSpeechToTextClient("spoken words");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new TextContent("hello"),
            new AudioContent(audioBytes, "audio/mpeg"),
            new DataContent(imageBytes, "image/png")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — transcription prepended, audio removed, text and image preserved
        var contents = context.Messages[^1].Contents;
        Assert.Equal(3, contents.Count);
        Assert.IsType<TextContent>(contents[0]);
        Assert.Equal("spoken words", ((TextContent)contents[0]).Text);
        Assert.IsType<TextContent>(contents[1]);
        Assert.Equal("hello", ((TextContent)contents[1]).Text);
        Assert.IsType<DataContent>(contents[2]);
        Assert.Equal("image/png", ((DataContent)contents[2]).MediaType);
    }

    // -------------------------------------------------------------------------
    // 8. Multiple audio items are all transcribed and joined with space
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_MultipleAudioItems_AllTranscribed_JoinedBySpace()
    {
        // Arrange
        var audioBytes1 = new byte[] { 0x49, 0x44, 0x33 };
        var audioBytes2 = new byte[] { 0x49, 0x44, 0x34 };
        var stt = new SequentialSpeechToTextClient(["first part", "second part"]);
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(audioBytes1, "audio/mpeg"),
            new AudioContent(audioBytes2, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — both transcriptions joined, two audio items removed
        var contents = context.Messages[^1].Contents;
        var text = Assert.IsType<TextContent>(Assert.Single(contents));
        Assert.Equal("first part second part", text.Text);
        Assert.Equal(2, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 9. AudioContent with empty bytes — STT not called
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_AudioContent_WithEmptyData_IsSkipped()
    {
        // Arrange
        var stt = new FakeSpeechToTextClient("should not be called");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(Array.Empty<byte>(), "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — empty content skipped, no STT call
        Assert.Equal(0, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 10. Non-user last message — no processing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_NonUserMessage_IsIgnored()
    {
        // Arrange
        var audioBytes = new byte[] { 0x49, 0x44, 0x33 };
        var stt = new FakeSpeechToTextClient("should not be called");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        // Last message is from assistant, not user
        var message = new ChatMessage(ChatRole.Assistant, [
            new AudioContent(audioBytes, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — no processing
        Assert.Equal(0, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 11. No messages — returns without error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_NoMessages_Returns()
    {
        // Arrange
        var stt = new FakeSpeechToTextClient("should not be called");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var context = CreateBeforeIterationContext(session: null, messages: []);

        // Act — must not throw
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(0, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 12. No speech recognizer — audio present but skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_SttClientNull_AudioSkipped()
    {
        // Arrange — no STT configured
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = null,
            IOMode = AudioIOMode.AudioToText
        };

        var audioBytes = new byte[] { 0x49, 0x44, 0x33 };
        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(audioBytes, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act — must not throw
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — message unchanged (audio still in there)
        Assert.IsType<AudioContent>(context.Messages[^1].Contents[0]);
    }

    [Fact]
    public async Task BeforeIteration_UsesAgentClientSpeechToTextConfig_WhenAudioConfigDoesNotSetStt()
    {
        var stt = new FakeSpeechToTextClient("from client family config");
        var provider = new FakeSpeechToTextProvider(stt);
        var registry = new ProviderRegistry();
        registry.Register(provider);

        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            ProviderRegistry = registry,
            IOMode = AudioIOMode.AudioToText
        };

        var agentConfig = new AgentConfig
        {
            Clients = new AgentClientConfig
            {
                SpeechToText = new ClientProviderConfig
                {
                    ProviderKey = provider.ProviderKey,
                    ModelName = "stt-model"
                }
            }
        };

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(new byte[] { 0x49, 0x44, 0x33 }, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message], agentConfig: agentConfig);

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        var text = Assert.IsType<TextContent>(Assert.Single(context.Messages[^1].Contents));
        Assert.Equal("from client family config", text.Text);
        Assert.Equal(1, stt.CallCount);
        Assert.Equal("stt-model", provider.CreatedConfigs.Single().ModelName);
    }

    // -------------------------------------------------------------------------
    // 13. STT error on first item — metrics emitted, second item still processed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_SttError_EmitsMetricsEvent_ContinuesOtherItems()
    {
        // Arrange
        var audioBytes1 = new byte[] { 0x49, 0x44, 0x33 };
        var audioBytes2 = new byte[] { 0x49, 0x44, 0x34 };
        var stt = new ThrowingThenSucceedingSttClient("recovered");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(audioBytes1, "audio/mpeg"), // first call throws
            new AudioContent(audioBytes2, "audio/mpeg")  // second call succeeds
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act — must not throw
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — second item transcription is present
        var contents = context.Messages[^1].Contents;
        var text = Assert.IsType<TextContent>(Assert.Single(contents));
        Assert.Equal("recovered", text.Text);
        Assert.Equal(2, stt.CallCount);
    }

    // -------------------------------------------------------------------------
    // 14. After transcription, audio content is not in the new message
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_AudioContent_OriginalRemovedFromMessage()
    {
        // Arrange
        var audioBytes = new byte[] { 0x49, 0x44, 0x33 };
        var stt = new FakeSpeechToTextClient("spoken");
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(audioBytes, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — no DataContent with audio MIME remains
        var newContents = context.Messages[^1].Contents;
        Assert.DoesNotContain(newContents, c =>
            c is DataContent dc && dc.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true);
    }

    // -------------------------------------------------------------------------
    // 15. Two audio items → two TranscriptionDeltaEvents (verified via message replacement)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BeforeIteration_TwoAudioItems_BothTranscriptionsIncludedInFinalText()
    {
        // Arrange
        var stt = new SequentialSpeechToTextClient(["alpha", "beta"]);
        var middleware = new AudioAttachmentTranscriptionMiddleware
        {
            SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt),
            IOMode = AudioIOMode.AudioToText
        };

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(new byte[] { 0x01 }, "audio/mpeg"),
            new AudioContent(new byte[] { 0x02 }, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — both transcriptions joined in the final TextContent
        var text = Assert.IsType<TextContent>(Assert.Single(context.Messages[^1].Contents));
        Assert.Contains("alpha", text.Text);
        Assert.Contains("beta", text.Text);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static BeforeIterationContext CreateBeforeIterationContext(
        SessionModel? session,
        List<ChatMessage> messages,
        AgentConfig? agentConfig = null,
        AgentRunConfig? runConfig = null)
    {
        var state = AgentLoopState.InitialSafe([], "run", "conv", "TestAgent");
        var eventCoordinator = new HPD.Events.Core.EventCoordinator();
        var agentContext = new AgentContext(
            "TestAgent",
            "conv",
            state,
            eventCoordinator,
            session,
            branch: null,
            CancellationToken.None,
            config: agentConfig,
            contentStore: (session?.Store as FixedContentSessionStore)?.ContentStore);

        return agentContext.AsBeforeIteration(
            iteration: 0,
            messages: messages,
            options: new ChatOptions(),
            runConfig: runConfig ?? new AgentRunConfig());
    }

    private static SessionModel CreateSessionWithStore(string sessionId, IContentStore contentStore)
    {
        var store = new FixedContentSessionStore(contentStore);
        var session = new SessionModel(sessionId) { Store = store };
        return session;
    }

    private static AudioAttachmentTranscriptionMiddleware CreateRealtimeConfiguredAttachmentMiddleware() =>
        new(new global::HPD.Agent.Audio.AudioConfig
        {
            ProcessingMode = AudioProcessingMode.Realtime,
            Realtime = new RealtimeAudioConfig()
        });

    // =========================================================================
    // Fake STT clients
    // =========================================================================

    /// <summary>STT client that always returns the same transcription.</summary>
    private class FakeSpeechToTextClient : ISpeechToTextClient
    {
        private readonly string _result;
        public int CallCount { get; private set; }
        public byte[]? LastReceivedBytes { get; private set; }

        public FakeSpeechToTextClient(string result) => _result = result;

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            using var ms = new MemoryStream();
            audioSpeechStream.CopyTo(ms);
            LastReceivedBytes = ms.ToArray();
            return Task.FromResult(MakeSttResponse(_result));
        }

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }

    private sealed class FakeSpeechToTextProvider(ISpeechToTextClient client) : ISpeechToTextClientProvider
    {
        public string ProviderKey => "fake-stt";
        public string DisplayName => "Fake STT";
        public List<ClientProviderConfig> CreatedConfigs { get; } = [];

        public ISpeechToTextClient CreateSpeechToTextClient(ClientProviderConfig config, IServiceProvider? services = null)
        {
            CreatedConfigs.Add(config);
            return client;
        }

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata() => new()
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

        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    /// <summary>STT client that returns different responses for each call in sequence.</summary>
    private class SequentialSpeechToTextClient : ISpeechToTextClient
    {
        private readonly string[] _results;
        private int _index;
        public int CallCount { get; private set; }

        public SequentialSpeechToTextClient(string[] results) => _results = results;

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var result = _index < _results.Length ? _results[_index++] : "";
            return Task.FromResult(MakeSttResponse(result));
        }

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }

    /// <summary>STT client that throws on the first call, succeeds on subsequent calls.</summary>
    private class ThrowingThenSucceedingSttClient : ISpeechToTextClient
    {
        private readonly string _successResult;
        private bool _hasThrown;
        public int CallCount { get; private set; }

        public ThrowingThenSucceedingSttClient(string successResult) => _successResult = successResult;

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw new InvalidOperationException("Simulated STT error");
            }
            return Task.FromResult(MakeSttResponse(_successResult));
        }

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }

    // =========================================================================
    // Fake session stores
    // =========================================================================

    /// <summary>Session store with no content ownership.</summary>
    private class NullContentSessionStore : ISessionStore
    {
        public Task<SessionModel?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<SessionModel?>(new SessionModel(sessionId));

        public Task SaveSessionAsync(SessionModel session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Branch?> LoadBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default)
            => Task.FromResult<Branch?>(null);
        public Task<List<string>> ListBranchIdsAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task DeleteBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<UncommittedTurn?> LoadUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<UncommittedTurn?>(null);

        public Task SaveUncommittedTurnAsync(UncommittedTurn turn, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteInactiveSessionsAsync(TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    /// <summary>Session store with no content ownership.</summary>
    private class FixedContentSessionStore : ISessionStore
    {
        public FixedContentSessionStore(IContentStore contentStore) => ContentStore = contentStore;

        public IContentStore ContentStore { get; }

        public Task<SessionModel?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<SessionModel?>(new SessionModel(sessionId));

        public Task SaveSessionAsync(SessionModel session, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Branch?> LoadBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default)
            => Task.FromResult<Branch?>(null);
        public Task<List<string>> ListBranchIdsAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task DeleteBranchAsync(string sessionId, string branchId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<UncommittedTurn?> LoadUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<UncommittedTurn?>(null);

        public Task SaveUncommittedTurnAsync(UncommittedTurn turn, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteUncommittedTurnAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> DeleteInactiveSessionsAsync(TimeSpan inactivityThreshold, bool dryRun = false, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private static SpeechToTextResponse MakeSttResponse(string text)
        => new(text);

    // =========================================================================
    // 40–45. Realtime config - attachment transcription remains semantic
    // =========================================================================

    // -------------------------------------------------------------------------
    // 40. Attachment transcription is independent of live processing mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RealtimeMode_BeforeIteration_StillTranscribesAudioAttachment()
    {
        // Arrange
        var stt = new FakeSpeechToTextClient("transcribed attachment");
        var middleware = CreateRealtimeConfiguredAttachmentMiddleware();
        middleware.SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt);
        middleware.IOMode = AudioIOMode.AudioToText;

        var message = new ChatMessage(ChatRole.User, [
            new DataContent(new byte[] { 0x01 }, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — realtime is a live transport mode; attachment STT still runs explicitly here.
        Assert.Equal(1, stt.CallCount);
        var text = Assert.IsType<TextContent>(Assert.Single(context.Messages[^1].Contents));
        Assert.Equal("transcribed attachment", text.Text);
    }

    // -------------------------------------------------------------------------
    // 41. DataContent(audio/*) is replaced in realtime mode when attachment transcription is configured
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RealtimeMode_BeforeIteration_ReplacesDataContentAudioInMessages()
    {
        // Arrange
        var audioBytes = new byte[] { 0x01, 0x02, 0x03 };
        var stt = new FakeSpeechToTextClient("data transcript");
        var middleware = CreateRealtimeConfiguredAttachmentMiddleware();
        middleware.SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt);
        middleware.IOMode = AudioIOMode.AudioToText;

        var message = new ChatMessage(ChatRole.User, [
            new DataContent(audioBytes, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — original DataContent replaced with transcription text
        var contents = context.Messages[^1].Contents;
        Assert.Single(contents);
        var text = Assert.IsType<TextContent>(contents[0]);
        Assert.Equal("data transcript", text.Text);
    }

    // -------------------------------------------------------------------------
    // 42. Typed AudioContent is also transcribed in Realtime mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RealtimeMode_BeforeIteration_TranscribesTypedAudioContent()
    {
        // Arrange
        var audioBytes = new byte[] { 0x04, 0x05 };
        var middleware = CreateRealtimeConfiguredAttachmentMiddleware();
        middleware.SpeechRecognizer = new MeaiBatchSpeechRecognizer(new FakeSpeechToTextClient("typed transcript"));
        middleware.IOMode = AudioIOMode.AudioToText;

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(audioBytes, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — AudioContent is substituted by the attachment transcript
        var contents = context.Messages[^1].Contents;
        Assert.Single(contents);
        var text = Assert.IsType<TextContent>(contents[0]);
        Assert.Equal("typed transcript", text.Text);
    }

    // -------------------------------------------------------------------------
    // 43. TextContent is injected in Realtime mode for explicit attachment STT
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RealtimeMode_BeforeIteration_InjectsTextContent()
    {
        // Arrange
        var middleware = CreateRealtimeConfiguredAttachmentMiddleware();
        middleware.SpeechRecognizer = new MeaiBatchSpeechRecognizer(new FakeSpeechToTextClient("injected"));
        middleware.IOMode = AudioIOMode.AudioToText;

        var message = new ChatMessage(ChatRole.User, [
            new DataContent(new byte[] { 0xFF }, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — TextContent injected
        var contents = context.Messages[^1].Contents;
        var text = Assert.IsType<TextContent>(Assert.Single(contents));
        Assert.Equal("injected", text.Text);
    }

    // -------------------------------------------------------------------------
    // 44. Existing TextContent items are preserved beside attachment transcript
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RealtimeMode_BeforeIteration_PassesThroughExistingTextContent()
    {
        // Arrange
        var middleware = CreateRealtimeConfiguredAttachmentMiddleware();
        middleware.SpeechRecognizer = new MeaiBatchSpeechRecognizer(new FakeSpeechToTextClient("audio transcript"));
        middleware.IOMode = AudioIOMode.AudioToAudioAndText;

        var message = new ChatMessage(ChatRole.User, [
            new TextContent("hello"),
            new DataContent(new byte[] { 0x01 }, "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — transcript is inserted before existing text
        var contents = context.Messages[^1].Contents;
        Assert.Equal(2, contents.Count);
        Assert.Equal("audio transcript", Assert.IsType<TextContent>(contents[0]).Text);
        Assert.Equal("hello", Assert.IsType<TextContent>(contents[1]).Text);
    }

    // -------------------------------------------------------------------------
    // 45. Realtime mode does not bypass explicit attachment transcription
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RealtimeMode_BeforeIteration_TranscribesWhenSttClientInjected()
    {
        // Arrange — STT client is present and attachment middleware is explicit.
        var stt = new FakeSpeechToTextClient("should see this");
        var middleware = CreateRealtimeConfiguredAttachmentMiddleware();
        middleware.SpeechRecognizer = new MeaiBatchSpeechRecognizer(stt);
        middleware.IOMode = AudioIOMode.AudioToAudioAndText;

        var message = new ChatMessage(ChatRole.User, [
            new AudioContent(new byte[] { 0xAA }, "audio/mpeg"),
            new TextContent("also here")
        ]);
        var context = CreateBeforeIterationContext(session: null, messages: [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(1, stt.CallCount);
        Assert.Equal(2, context.Messages[^1].Contents.Count);
        Assert.Equal("should see this", Assert.IsType<TextContent>(context.Messages[^1].Contents[0]).Text);
        Assert.Equal("also here", Assert.IsType<TextContent>(context.Messages[^1].Contents[1]).Text);
    }
}
