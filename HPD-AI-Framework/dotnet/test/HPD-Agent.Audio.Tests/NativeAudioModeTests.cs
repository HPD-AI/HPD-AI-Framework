// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Middleware;
using HPD.Events;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

#pragma warning disable MEAI001

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Tests for AudioPipelineMiddleware in Native processing mode (tests 46–59).
/// Native mode: model outputs audio directly as DataContent(audio/*) in the response stream.
/// Middleware extracts those chunks as AudioChunkEvents and uploads assembled bytes to /artifacts.
/// TTS is never involved.
/// </summary>
public class NativeAudioModeTests
{
    // =========================================================================
    // WrapModelCallStreamingAsync — Native audio extraction (46–56)
    // =========================================================================

    // -------------------------------------------------------------------------
    // 46. Model response with DataContent(audio/pcm) → AudioChunkEvent emitted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_EmitsAudioChunkEvent_WhenModelResponseContainsAudioDataContent()
    {
        // Arrange — model returns one update containing DataContent(audio/pcm)
        var audioBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-native-chunk", contentStore);

        var model = new AudioDataChatClient([new DataContent(audioBytes, "audio/pcm")]);
        var emittedChunks = new List<AudioChunkEvent>();
        var coordinator = new CapturingEventCoordinator(e =>
        {
            if (e is AudioChunkEvent c) emittedChunks.Add(c);
        });

        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session, model, coordinator);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — one AudioChunkEvent with the correct bytes and MIME
        Assert.Single(emittedChunks);
        Assert.Equal("audio/pcm", emittedChunks[0].MimeType);
        Assert.Equal(audioBytes, Convert.FromBase64String(emittedChunks[0].Base64Audio));
    }

    // -------------------------------------------------------------------------
    // 47. All ChatResponseUpdates are still yielded to the consumer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_PassesThroughAllUpdates()
    {
        // Arrange — two updates: one with text, one with audio
        var model = new MultiUpdateChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("thinking...")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new DataContent(new byte[] { 0xAA }, "audio/pcm")])
        ]);

        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session: null, model, coordinator: null);

        // Act
        var updates = await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — both updates yielded
        Assert.Equal(2, updates.Count);
    }

    // -------------------------------------------------------------------------
    // 48. TTS client is never called in Native mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_DoesNotCallTts()
    {
        // Arrange — TTS client injected but must remain silent
        var tts = new CallCountingTtsClient();
        var model = new AudioDataChatClient([new DataContent(new byte[] { 0x01 }, "audio/pcm")]);

        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session: null, model, coordinator: null);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — TTS never touched
        Assert.Equal(0, tts.CallCount);
    }

    // -------------------------------------------------------------------------
    // 49. Assembled native audio bytes are uploaded to content store
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_UploadsAssembledAudioToContentStore()
    {
        // Arrange
        var audioBytes = new byte[] { 0x10, 0x20, 0x30 };
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-native-upload", contentStore);

        var model = new AudioDataChatClient([new DataContent(audioBytes, "audio/pcm")]);
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session, model, coordinator: null);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — uploaded once with exact bytes
        Assert.Equal(1, contentStore.PutCallCount);
        Assert.Equal(audioBytes, contentStore.LastUploadedBytes);
    }

    // -------------------------------------------------------------------------
    // 50. audio-role tag is "native" (not "tts")
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_AudioRoleTag_IsNative()
    {
        // Arrange
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-native-tag-role", contentStore);

        var model = new AudioDataChatClient([new DataContent(new byte[] { 0x01 }, "audio/pcm")]);
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session, model, coordinator: null);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert
        Assert.Equal("native", contentStore.LastTags?["audio-role"]);
    }

    // -------------------------------------------------------------------------
    // 51. Multiple audio chunks across updates — all bytes accumulated in order
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_MultipleAudioChunks_AllAccumulatedInOrder()
    {
        // Arrange — three updates, each with one audio chunk
        var chunk1 = new byte[] { 0x01, 0x02 };
        var chunk2 = new byte[] { 0x03, 0x04 };
        var chunk3 = new byte[] { 0x05, 0x06 };
        var expected = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };

        var contentStore = new SpyContentStore();
        var session = CreateSession("session-native-multi", contentStore);

        var model = new AudioDataChatClient([
            new DataContent(chunk1, "audio/pcm"),
            new DataContent(chunk2, "audio/pcm"),
            new DataContent(chunk3, "audio/pcm")
        ]);
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session, model, coordinator: null);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — bytes accumulated in exact insertion order
        Assert.Equal(expected, contentStore.LastUploadedBytes);
    }

    // -------------------------------------------------------------------------
    // 52. No upload when the model response contains no audio DataContent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_NoUploadWhenNoAudioInResponse()
    {
        // Arrange — model returns only text, no audio
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-native-no-audio", contentStore);

        var model = new MultiUpdateChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("text only")])
        ]);
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session, model, coordinator: null);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — nothing uploaded
        Assert.Equal(0, contentStore.PutCallCount);
    }

    // -------------------------------------------------------------------------
    // 53. ChunkIndex increments for each audio DataContent item
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_ChunkIndexIncrements_PerAudioItem()
    {
        // Arrange — three audio chunks; capture the emitted events
        var emittedChunks = new List<AudioChunkEvent>();
        var coordinator = new CapturingEventCoordinator(e =>
        {
            if (e is AudioChunkEvent c) emittedChunks.Add(c);
        });

        var model = new AudioDataChatClient([
            new DataContent(new byte[] { 0x01 }, "audio/pcm"),
            new DataContent(new byte[] { 0x02 }, "audio/pcm"),
            new DataContent(new byte[] { 0x03 }, "audio/pcm")
        ]);
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session: null, model, coordinator);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — indices 0, 1, 2
        Assert.Equal(3, emittedChunks.Count);
        Assert.Equal(0, emittedChunks[0].ChunkIndex);
        Assert.Equal(1, emittedChunks[1].ChunkIndex);
        Assert.Equal(2, emittedChunks[2].ChunkIndex);
    }

    // -------------------------------------------------------------------------
    // 54. interrupted tag is "true" when stream is interrupted (same as Pipeline)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_InterruptedTagIsTrue_WhenStreamInterrupted()
    {
        // Arrange — registry that becomes interrupted after first audio chunk
        var contentStore = new SpyContentStore();
        var session = CreateSession("session-native-interrupted", contentStore);

        var registry = new DelayedInterruptStreamRegistry();
        var model = new InterruptingNativeAudioChatClient(new byte[] { 0x01 }, registry);

        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var state = AgentLoopState.InitialSafe([], "run", "conv", "TestAgent");
        var request = new ModelRequest
        {
            Model = model,
            Messages = [new ChatMessage(ChatRole.User, "test")],
            Options = new ChatOptions(),
            State = state,
            Iteration = 0,
            Session = session,
            Streams = registry
        };

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — uploaded with interrupted=true
        Assert.Equal(1, contentStore.PutCallCount);
        Assert.Equal("true", contentStore.LastTags?["interrupted"]);
    }

    // -------------------------------------------------------------------------
    // 55. TextContent in the response stream does NOT produce AudioChunkEvent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_TextContentInResponse_ProducesNoAudioChunkEvent()
    {
        // Arrange — model returns only TextContent
        var emittedChunks = new List<AudioChunkEvent>();
        var coordinator = new CapturingEventCoordinator(e =>
        {
            if (e is AudioChunkEvent c) emittedChunks.Add(c);
        });

        var model = new MultiUpdateChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hello")])
        ]);
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session: null, model, coordinator);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — no audio chunk events
        Assert.Empty(emittedChunks);
    }

    // -------------------------------------------------------------------------
    // 56. DataContent with non-audio MIME is ignored (no AudioChunkEvent)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NativeMode_Stream_NonAudioDataContent_ProducesNoAudioChunkEvent()
    {
        // Arrange — DataContent with image MIME
        var emittedChunks = new List<AudioChunkEvent>();
        var coordinator = new CapturingEventCoordinator(e =>
        {
            if (e is AudioChunkEvent c) emittedChunks.Add(c);
        });

        var model = new MultiUpdateChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new DataContent(new byte[] { 0xFF }, "image/png")])
        ]);
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var request = CreateModelRequest(session: null, model, coordinator);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — no audio chunk events for image content
        Assert.Empty(emittedChunks);
    }

    // =========================================================================
    // Mode routing / separation (57–59)
    // =========================================================================

    // -------------------------------------------------------------------------
    // 57. Pipeline mode (default) still routes through TTS — no regression
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PipelineMode_IsDefault_WrapModelCall_UsesTts()
    {
        // Arrange
        var tts = new CallCountingTtsClient();
        var middleware = new AudioPipelineMiddleware
        {
            TextToSpeechClient = tts,
            IOMode = AudioIOMode.TextToAudio
            // ProcessingMode defaults to Pipeline
        };

        var model = new MultiUpdateChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello.")])
        ]);
        var request = CreateModelRequest(session: null, model, coordinator: null);

        // Act
        await DrainStreamAsync(middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None)!);

        // Assert — TTS was called (Pipeline path active)
        Assert.True(tts.CallCount > 0);
    }

    // -------------------------------------------------------------------------
    // 58. Native mode returns non-null (intercepts stream) when HasAudioOutput
    // -------------------------------------------------------------------------

    [Fact]
    public void NativeMode_WrapModelCall_ReturnsNonNull_WhenHasAudioOutput()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToAudio,
            ProcessingMode = AudioProcessingMode.Native
        };

        var model = new MultiUpdateChatClient([]);
        var request = CreateModelRequest(session: null, model, coordinator: null);

        // Act
        var result = middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None);

        // Assert — not null: middleware intercepts the stream
        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // 59. Native mode with AudioToText IOMode returns null (no audio output to handle)
    // -------------------------------------------------------------------------

    [Fact]
    public void NativeMode_WrapModelCall_ReturnsNull_WhenIOModeIsAudioToText()
    {
        // Arrange — AudioToText has no audio output path
        var middleware = new AudioPipelineMiddleware
        {
            IOMode = AudioIOMode.AudioToText,
            ProcessingMode = AudioProcessingMode.Native
        };

        var model = new MultiUpdateChatClient([]);
        var request = CreateModelRequest(session: null, model, coordinator: null);

        // Act
        var result = middleware.WrapModelCallStreamingAsync(request,
            r => r.Model.GetStreamingResponseAsync(r.Messages, r.Options), CancellationToken.None);

        // Assert — null: no audio output, no interception
        Assert.Null(result);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static SessionModel CreateSession(string sessionId, IContentStore contentStore)
    {
        var store = new FixedContentSessionStore(contentStore);
        return new SessionModel(sessionId) { Store = store };
    }

    private static ModelRequest CreateModelRequest(
        SessionModel? session,
        IChatClient model,
        IEventCoordinator? coordinator)
    {
        var state = AgentLoopState.InitialSafe([], "run", "conv", "TestAgent");
        return new ModelRequest
        {
            Model = model,
            Messages = [new ChatMessage(ChatRole.User, "test")],
            Options = new ChatOptions(),
            State = state,
            Iteration = 0,
            Session = session,
            EventCoordinator = coordinator
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
    // Fake chat clients
    // =========================================================================

    /// <summary>
    /// Chat client that returns one update per DataContent item provided.
    /// Each update contains exactly one DataContent (simulates native audio model output).
    /// </summary>
    private class AudioDataChatClient : IChatClient
    {
        private readonly DataContent[] _chunks;
        public ChatClientMetadata Metadata => new("native-fake");
        public AudioDataChatClient(DataContent[] chunks) => _chunks = chunks;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var chunk in _chunks)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [chunk]);
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
        public void Dispose() { }
    }

    /// <summary>
    /// Chat client that yields a pre-built sequence of ChatResponseUpdates.
    /// </summary>
    private class MultiUpdateChatClient : IChatClient
    {
        private readonly ChatResponseUpdate[] _updates;
        public ChatClientMetadata Metadata => new("multi-fake");
        public MultiUpdateChatClient(ChatResponseUpdate[] updates) => _updates = updates;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in _updates)
            {
                yield return update;
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
        public void Dispose() { }
    }

    /// <summary>
    /// Chat client that yields one audio DataContent chunk then marks the stream interrupted.
    /// Used for test #54.
    /// </summary>
    private class InterruptingNativeAudioChatClient : IChatClient
    {
        private readonly byte[] _chunk;
        private readonly DelayedInterruptStreamRegistry _registry;
        public ChatClientMetadata Metadata => new("interrupting-native-fake");

        public InterruptingNativeAudioChatClient(byte[] chunk, DelayedInterruptStreamRegistry registry)
        {
            _chunk = chunk;
            _registry = registry;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new DataContent(_chunk, "audio/pcm")]);
            _registry.InterruptAll();
            await Task.Yield();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
        public void Dispose() { }
    }

    /// <summary>
    /// TTS client that counts calls without doing any synthesis.
    /// </summary>
    private class CallCountingTtsClient : ITextToSpeechClient
    {
        public int CallCount { get; private set; }

        public Task<TextToSpeechResponse> GetSpeechAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new TextToSpeechResponse
            {
                Audio = new DataContent(new byte[] { 0x00 }, "audio/mpeg")
            });
        }

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingSpeechAsync(
            IAsyncEnumerable<string> textStream,
            TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            // consume stream
            await foreach (var _ in textStream.WithCancellation(cancellationToken)) { }
            yield return new TextToSpeechResponseUpdate
            {
                Audio = new DataContent(new byte[] { 0x00 }, "audio/mpeg"),
                IsLast = true
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    // =========================================================================
    // Event coordinator that captures emitted events
    // =========================================================================

    private class CapturingEventCoordinator : IEventCoordinator
    {
        private readonly Action<AgentEvent> _onEmit;
        public CapturingEventCoordinator(Action<AgentEvent> onEmit) => _onEmit = onEmit;
        public void Emit(Event evt) => _onEmit((AgentEvent)evt);
        public void EmitUpstream(Event evt) => _onEmit((AgentEvent)evt);
        public bool TryRead([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Event? evt) { evt = null; return false; }
        public async IAsyncEnumerable<Event> ReadAllAsync(CancellationToken ct = default) 
        {
            await Task.CompletedTask;
            yield break;
        }
        public void SetParent(IEventCoordinator parent) { }
        public Task<TResponse> WaitForResponseAsync<TResponse>(
            string requestId,
            TimeSpan timeout,
            CancellationToken ct = default) where TResponse : Event
            => throw new NotImplementedException("WaitForResponseAsync not supported in test mock");
        public void SendResponse(string requestId, Event response) { }
        public IStreamRegistry Streams { get; } = new NoOpStreamRegistry();
        public IDisposable Subscribe(Action<AgentEvent> handler) => NoOpDisposable.Instance;
        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();
            public void Dispose() { }
        }
        private sealed class NoOpStreamRegistry : IStreamRegistry
        {
            public IStreamHandle Create(string? streamId = null) => new NoOpStreamHandle();
            public IStreamHandle BeginStream(string streamId) => new NoOpStreamHandle();
            public IStreamHandle? Get(string streamId) => new NoOpStreamHandle();
            public void InterruptStream(string streamId) { }
            public void CompleteStream(string streamId) { }
            public bool IsActive(string streamId) => false;
            public void InterruptAll() { }
            public void InterruptWhere(Func<IStreamHandle, bool> predicate) { }
            public IReadOnlyList<IStreamHandle> ActiveStreams { get; } = Array.Empty<IStreamHandle>();
            public int ActiveCount => 0;
        }
        private sealed class NoOpStreamHandle : IStreamHandle
        {
            public string StreamId => "noop-stream";
            public bool IsInterrupted => false;
            public bool IsCompleted => false;
            public int EmittedCount => 0;
            public int DroppedCount => 0;
            public event Action<IStreamHandle>? OnInterrupted;
            public event Action<IStreamHandle>? OnCompleted;
            public void Interrupt() { }
            public void Complete() { }
            public Task WaitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void Dispose() { }
        }
    }

    // =========================================================================
    // Spy content store (records PutAsync calls)
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
    // Session store backed by a fixed IContentStore
    // =========================================================================

    private class FixedContentSessionStore : ISessionStore
    {
        private readonly IContentStore _contentStore;
        public FixedContentSessionStore(IContentStore contentStore) => _contentStore = contentStore;

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
    // Delayed-interrupt infrastructure (re-used from OutputTests for test #54)
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

    private class DelayedInterruptHandle : IStreamHandle
    {
        private volatile bool _interrupted;
        public string StreamId => "native-interrupt-stream";
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
}
