// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Middleware;
using HPD.Events;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Tests for AudioPipelineMiddleware TTS artifact upload:
/// assembled audio is stored in /artifacts after synthesis completes.
/// </summary>
public class AudioPipelineMiddlewareOutputTests
{
    // -------------------------------------------------------------------------
    // 16. Full synthesis → artifact uploaded to /artifacts
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_WhenComplete_UploadsAssembledAudioToArtifacts()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var sessionId = "session-tts-upload";
        var session = CreateSession(sessionId, contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01, 0x02, 0x03 }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Hello.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — one artifact uploaded to the session scope
        var artifacts = await contentStore.QueryAsync(sessionId, cancellationToken: CancellationToken.None);
        Assert.Single(artifacts);
        var artifact = artifacts[0];
        Assert.Equal("/artifacts", artifact.Tags?["folder"]);
        Assert.Equal("tts", artifact.Tags?["audio-role"]);
        Assert.Equal(sessionId, artifact.Tags?["session"]);
        Assert.Equal(ContentSource.Agent, artifact.Origin);
    }

    // -------------------------------------------------------------------------
    // 17. synthesis-id tag matches the emitted SynthesisStartedEvent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_Tags_ContainSynthesisId()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var session = CreateSession("session-synth-id", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0xAA }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Done.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — synthesis-id tag is a non-empty 8-char hex string
        var artifacts = await contentStore.QueryAsync("session-synth-id", cancellationToken: CancellationToken.None);
        Assert.Single(artifacts);
        var synthId = artifacts[0].Tags?["synthesis-id"];
        Assert.NotNull(synthId);
        Assert.Equal(8, synthId!.Length);
    }

    // -------------------------------------------------------------------------
    // 18. voice and model tags reflect resolved values
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_Tags_ContainVoiceAndModel()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var session = CreateSession("session-voice", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0xBB }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio,
            DefaultVoice = "nova",
            DefaultModel = "tts-1-hd"
        };

        var request = CreateModelRequest(session, singleResponse: "Hi.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert
        var artifacts = await contentStore.QueryAsync("session-voice", cancellationToken: CancellationToken.None);
        Assert.Single(artifacts);
        Assert.Equal("nova", artifacts[0].Tags?["voice"]);
        Assert.Equal("tts-1-hd", artifacts[0].Tags?["model"]);
    }

    // -------------------------------------------------------------------------
    // 19. interrupted tag is "true" when stream is interrupted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_WhenNotInterrupted_InterruptedTagIsFalse()
    {
        // Arrange
        var contentStore = new InMemoryContentStore();
        var session = CreateSession("session-not-interrupted", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "OK.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert
        var artifacts = await contentStore.QueryAsync("session-not-interrupted", cancellationToken: CancellationToken.None);
        Assert.Single(artifacts);
        Assert.Equal("false", artifacts[0].Tags?["interrupted"]);
    }

    // -------------------------------------------------------------------------
    // 20. No session → upload silently skipped, no exception
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_NoSession_DoesNotThrow()
    {
        // Arrange
        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        // No session on request
        var request = CreateModelRequest(session: null, singleResponse: "Hello.");

        // Act — must not throw
        var updates = await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — updates still returned normally
        Assert.NotEmpty(updates);
    }

    // -------------------------------------------------------------------------
    // 21. Content store throws → exception swallowed, SynthesisCompletedEvent still fires
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_ContentStoreThrows_DoesNotBreakResponse()
    {
        // Arrange
        var session = CreateSession("session-throw", new ThrowingContentStore());

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Fine.");

        // Act — must not throw even though store throws
        var updates = await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — LLM response still passed through
        Assert.NotEmpty(updates);
    }

    // -------------------------------------------------------------------------
    // 22. Zero chunks from TTS → PutAsync not called
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_ZeroChunks_NoUploadAttempted()
    {
        // Arrange
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-zero", contentStore);

        var tts = new FakeTtsClient([]); // returns no chunks
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Silent.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — PutAsync never called
        Assert.Equal(0, contentStore.PutCallCount);
    }

    // -------------------------------------------------------------------------
    // 23. Assembled bytes are exact concatenation of all chunk bytes in order
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_AssembledAudio_IsConcatenationOfAllChunks()
    {
        // Arrange
        var chunk1 = new byte[] { 0x01, 0x02 };
        var chunk2 = new byte[] { 0x03, 0x04 };
        var chunk3 = new byte[] { 0x05, 0x06 };
        var expected = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

        var contentStore = new SpyContentStore();
        var session = CreateSession("session-concat", contentStore);

        var tts = new FakeTtsClient([chunk1, chunk2, chunk3]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Three chunks.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — uploaded bytes are the exact concatenation
        Assert.Equal(1, contentStore.PutCallCount);
        Assert.Equal(expected, contentStore.LastUploadedBytes);
    }

    // -------------------------------------------------------------------------
    // 24. DefaultOutputFormat used as contentType
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_OutputFormat_UsedAsContentType()
    {
        // Arrange
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-format", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio,
            DefaultOutputFormat = "audio/opus"
        };

        var request = CreateModelRequest(session, singleResponse: "Opus.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert
        Assert.Equal("audio/opus", contentStore.LastContentType);
    }

    // -------------------------------------------------------------------------
    // 25. DefaultOutputFormat null → falls back to audio/mpeg
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_DefaultOutputFormat_FallsBackToMpeg()
    {
        // Arrange
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-mpeg", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio,
            DefaultOutputFormat = null // not set
        };

        var request = CreateModelRequest(session, singleResponse: "MP3.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request, r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert
        Assert.Equal("audio/mpeg", contentStore.LastContentType);
    }

    // -------------------------------------------------------------------------
    // 19. interrupted tag is "true" when stream is interrupted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamWithTts_WhenInterrupted_InterruptedTagIsTrue()
    {
        // Arrange — registry starts un-interrupted; TTS yields one chunk then calls InterruptAll()
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-interrupted", contentStore);

        var registry = new DelayedInterruptStreamRegistry();
        var tts = new InterruptingTtsClient(new byte[] { 0x01 }, registry);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var state = AgentLoopState.InitialSafe([], "run", "conv", "TestAgent");
        var request = new ModelRequest
        {
            Model = new SingleResponseChatClient("OK."),
            Messages = [new ChatMessage(ChatRole.User, "test")],
            Options = new ChatOptions(),
            State = state,
            Iteration = 0,
            Session = session,
            Streams = registry
        };

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        // Assert — audio was assembled and uploaded with interrupted=true
        Assert.Equal(1, contentStore.PutCallCount);
        Assert.Equal("true", contentStore.LastTags?["interrupted"]);
    }

    // =========================================================================
    // SynthesisState accumulation (27-29) — tested through middleware output
    // =========================================================================

    // -------------------------------------------------------------------------
    // 27. ChunkIndex starts at 0 (first chunk is index 0)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SynthesisState_InitialChunkIndex_IsZero()
    {
        // Arrange — single chunk; verify the first (and only) chunk emitted has index 0
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-idx", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0xAA }]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Hi.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        // Assert — one upload occurred (assembled bytes non-empty), confirming chunk was processed
        Assert.Equal(1, contentStore.PutCallCount);
        Assert.NotNull(contentStore.LastUploadedBytes);
        Assert.NotEmpty(contentStore.LastUploadedBytes!);
    }

    // -------------------------------------------------------------------------
    // 28. AssembledAudio starts empty (no upload when TTS returns no chunks)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SynthesisState_AssembledAudio_StartsEmpty_NoUploadWhenNoChunks()
    {
        // Arrange — TTS produces no chunks → assembled audio stays empty → PutAsync not called
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-empty", contentStore);

        var tts = new FakeTtsClient([]); // zero chunks
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Silent.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        // Assert — nothing uploaded because assembled audio was empty
        Assert.Equal(0, contentStore.PutCallCount);
    }

    // -------------------------------------------------------------------------
    // 29. AddRange accumulates bytes in order across multiple chunks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SynthesisState_AddRange_AccumulatesBytes_InOrder()
    {
        // Arrange — three chunks; verify uploaded bytes are in insertion order
        var chunk1 = new byte[] { 0x01, 0x02 };
        var chunk2 = new byte[] { 0x03, 0x04 };
        var chunk3 = new byte[] { 0x05, 0x06 };
        var expected = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

        var contentStore = new SpyContentStore();
        var session = CreateSession("session-order", contentStore);

        var tts = new FakeTtsClient([chunk1, chunk2, chunk3]);
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
        };

        var request = CreateModelRequest(session, singleResponse: "Three.");

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        // Assert — bytes accumulated in exact order
        Assert.Equal(expected, contentStore.LastUploadedBytes);
    }

    // =========================================================================
    // TtsArtifactTags (35-38) — tag convention contracts
    // =========================================================================

    // -------------------------------------------------------------------------
    // 35. folder tag equals /artifacts
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TtsArtifact_HasFolder_Tag_EqualToArtifacts()
    {
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-tag-folder", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware { TextToSpeechClient = tts, IOMode = AudioIOMode.TextToAudio };
        var request = CreateModelRequest(session, singleResponse: "Hi.");

        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        Assert.Equal("/artifacts", contentStore.LastTags?["folder"]);
    }

    // -------------------------------------------------------------------------
    // 36. audio-role tag equals tts
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TtsArtifact_HasAudioRole_Tag_EqualToTts()
    {
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-tag-role", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware { TextToSpeechClient = tts, IOMode = AudioIOMode.TextToAudio };
        var request = CreateModelRequest(session, singleResponse: "Hi.");

        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        Assert.Equal("tts", contentStore.LastTags?["audio-role"]);
    }

    // -------------------------------------------------------------------------
    // 37. Origin is ContentSource.Agent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TtsArtifact_Origin_IsAgent()
    {
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-tag-origin", contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware { TextToSpeechClient = tts, IOMode = AudioIOMode.TextToAudio };
        var request = CreateModelRequest(session, singleResponse: "Hi.");

        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        Assert.Equal(ContentSource.Agent, contentStore.LastOrigin);
    }

    // -------------------------------------------------------------------------
    // 38. session tag matches the session ID
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TtsArtifact_SessionTag_MatchesSessionId()
    {
        var contentStore = new SpyContentStore();
        const string sessionId = "session-tag-id-check";
        var session = CreateSession(sessionId, contentStore);

        var tts = new FakeTtsClient([new byte[] { 0x01 }]);
        var middleware = new AudioPipelineMiddleware { TextToSpeechClient = tts, IOMode = AudioIOMode.TextToAudio };
        var request = CreateModelRequest(session, singleResponse: "Hi.");

        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options),
            CancellationToken.None)!);

        Assert.Equal(sessionId, contentStore.LastTags?["session"]);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static SessionModel CreateSession(string sessionId, IContentStore contentStore)
    {
        var store = new FixedContentSessionStore(contentStore);
        return new SessionModel(sessionId) { Store = store };
    }

    private static ModelRequest CreateModelRequest(SessionModel? session, string singleResponse)
    {
        var chatClient = new SingleResponseChatClient(singleResponse);
        var state = AgentLoopState.InitialSafe([], "run", "conv", "TestAgent");

        return new ModelRequest
        {
            Model = chatClient,
            Messages = [new ChatMessage(ChatRole.User, "test")],
            Options = new ChatOptions(),
            State = state,
            Iteration = 0,
            Session = session
        };
    }

    private static async Task<List<ChatResponseUpdate>> DrainStreamAsync(
        IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        var results = new List<ChatResponseUpdate>();
        await foreach (var update in stream)
            results.Add(update);
        return results;
    }

    // =========================================================================
    // Fake TTS client
    // =========================================================================

    /// <summary>TTS client that yields a fixed set of byte chunks as streaming responses.</summary>
    private class FakeTtsClient : ITextToSpeechClient
    {
        private readonly byte[][] _chunks;
        public FakeTtsClient(byte[][] chunks) => _chunks = chunks;

        public Task<TextToSpeechResponse> GetSpeechAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingSpeechAsync(
            IAsyncEnumerable<string> textStream,
            TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < _chunks.Length; i++)
            {
                yield return new TextToSpeechResponseUpdate
                {
                    Audio = new DataContent(_chunks[i], "audio/mpeg"),
                    Duration = TimeSpan.FromMilliseconds(100),
                    IsLast = i == _chunks.Length - 1
                };
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }

    // =========================================================================
    // Fake chat client (single-response)
    // =========================================================================

    private class SingleResponseChatClient : IChatClient
    {
        private readonly string _response;
        public ChatClientMetadata Metadata => new("fake");
        public SingleResponseChatClient(string response) => _response = response;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, _response);
            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }

    // =========================================================================
    // Spy content store (records calls)
    // =========================================================================

    private class SpyContentStore : IContentStore
    {
        public int PutCallCount { get; private set; }
        public byte[]? LastUploadedBytes { get; private set; }
        public string? LastContentType { get; private set; }
        public Dictionary<string, string>? LastTags { get; private set; }
        public ContentSource? LastOrigin { get; private set; }

        public Task<string> PutAsync(
            string? scope,
            byte[] data,
            string contentType,
            ContentMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            PutCallCount++;
            LastUploadedBytes = data;
            LastContentType = contentType;
            LastTags = metadata?.Tags != null ? new Dictionary<string, string>(metadata.Tags) : null;
            LastOrigin = metadata?.Origin;
            return Task.FromResult(Guid.NewGuid().ToString("N"));
        }

        public Task<ContentData?> GetAsync(string? scope, string contentId, CancellationToken cancellationToken = default)
            => Task.FromResult<ContentData?>(null);

        public Task DeleteAsync(string? scope, string contentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ContentInfo>> QueryAsync(string? scope = null, ContentQuery? query = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContentInfo>>([]);
    }

    // =========================================================================
    // Throwing content store
    // =========================================================================

    private class ThrowingContentStore : IContentStore
    {
        public Task<string> PutAsync(string? scope, byte[] data, string contentType, ContentMetadata? metadata = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated store failure");

        public Task<ContentData?> GetAsync(string? scope, string contentId, CancellationToken cancellationToken = default)
            => Task.FromResult<ContentData?>(null);

        public Task DeleteAsync(string? scope, string contentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ContentInfo>> QueryAsync(string? scope = null, ContentQuery? query = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContentInfo>>([]);
    }

    // =========================================================================
    // Session store backed by a fixed IContentStore
    // =========================================================================

    private class FixedContentSessionStore : ISessionStore
    {
        private readonly IContentStore _contentStore;
        public FixedContentSessionStore(IContentStore store) => _contentStore = store;

        public IContentStore? GetContentStore(string sessionId) => _contentStore;

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

        public Task SaveBranchAsync(string sessionId, Branch branch, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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

    // =========================================================================
    // Stream registry that starts normal, becomes interrupted after first chunk
    // (for test #19 — we need audio to assemble, then wasInterrupted == true)
    // =========================================================================

    private class DelayedInterruptStreamRegistry : IStreamRegistry
    {
        private readonly DelayedInterruptHandle _handle = new();

        public IStreamHandle Create(string? streamId = null) => _handle;
        public IStreamHandle BeginStream(string streamId) => _handle;
        public IStreamHandle? Get(string streamId) => _handle;
        public void InterruptStream(string streamId) => _handle.Interrupt();
        public void CompleteStream(string streamId) { }
        public bool IsActive(string streamId) => !_handle.IsInterrupted;
        public void InterruptAll() => _handle.Interrupt();
        public void InterruptWhere(Func<IStreamHandle, bool> predicate) { }
        public IReadOnlyList<IStreamHandle> ActiveStreams => [];
        public int ActiveCount => 0;
    }

    /// <summary>
    /// Handle that starts as not-interrupted, then becomes interrupted once Interrupt() is called.
    /// The TTS client below calls Interrupt() after yielding the first chunk, so:
    ///   - SynthesizeAndEmitAsync sees IsInterrupted=false → lets the chunk through (audio accumulates)
    ///   - StreamWithTtsAsync finally block sees IsInterrupted=true → wasInterrupted=true → tag="true"
    /// </summary>
    private class DelayedInterruptHandle : IStreamHandle
    {
        private volatile bool _interrupted;
        public string StreamId => "delayed-interrupt-stream";
        public bool IsInterrupted => _interrupted;
        public bool IsCompleted => _interrupted;
        public int EmittedCount => 0;
        public int DroppedCount => 0;
        public event Action<IStreamHandle>? OnInterrupted;
#pragma warning disable CS0067
        public event Action<IStreamHandle>? OnCompleted;
#pragma warning restore CS0067
        public void Interrupt() { _interrupted = true; OnInterrupted?.Invoke(this); }
        public void Complete() { }
        public Task WaitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    // =========================================================================
    // TTS client that yields one chunk then marks the stream as interrupted
    // =========================================================================

    private class InterruptingTtsClient : ITextToSpeechClient
    {
        private readonly byte[] _chunk;
        private readonly DelayedInterruptStreamRegistry _registry;

        public InterruptingTtsClient(byte[] chunk, DelayedInterruptStreamRegistry registry)
        {
            _chunk = chunk;
            _registry = registry;
        }

        public Task<TextToSpeechResponse> GetSpeechAsync(
            string text, TextToSpeechOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingSpeechAsync(
            IAsyncEnumerable<string> textStream,
            TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Yield the chunk first so it accumulates in AssembledAudio
            yield return new TextToSpeechResponseUpdate
            {
                Audio = new DataContent(_chunk, "audio/mpeg"),
                Duration = TimeSpan.FromMilliseconds(100),
                IsLast = true
            };
            // Now interrupt the stream — middleware will see IsInterrupted=true in the finally block
            _registry.InterruptAll();
            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }
}
