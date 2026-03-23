// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Audio;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

#pragma warning disable MEAI001

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Integration-style tests (33-34): wire a real InMemoryContentStore, upload audio bytes,
/// transform to UriContent(asset://...), and verify the middleware resolves and transcribes them.
/// </summary>
public class AssetUriResolutionTests
{
    // -------------------------------------------------------------------------
    // 33. Full round-trip: upload → UriContent(asset://) → middleware resolves → STT called
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EndToEnd_UploadAudio_ThenTranscribe_ViaAssetUri()
    {
        // Arrange — upload audio bytes to store
        var audioBytes = new byte[] { 0x49, 0x44, 0x33, 0x04 }; // fake MP3
        var contentStore = new InMemoryContentStore();
        const string sessionId = "e2e-session-single";
        var assetId = await contentStore.PutAsync(sessionId, audioBytes, "audio/mpeg");

        var stt = new CapturingSttClient("transcribed audio");
        var middleware = new AudioPipelineMiddleware
        {
            SpeechToTextClient = stt,
            IOMode = AudioIOMode.AudioToText
        };

        // Build a message with UriContent(asset://...) as the middleware will see post-upload
        var uriContent = new UriContent(new Uri($"asset://{assetId}"), "audio/mpeg");
        var session = CreateSessionWithStore(sessionId, contentStore);
        var context = CreateBeforeIterationContext(session, [new ChatMessage(ChatRole.User, [uriContent])]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — STT received the exact bytes that were uploaded
        Assert.Equal(1, stt.CallCount);
        Assert.Equal(audioBytes, stt.LastReceivedBytes);

        // And the message was replaced with a TextContent transcription
        var text = Assert.IsType<TextContent>(Assert.Single(context.Messages[^1].Contents));
        Assert.Equal("transcribed audio", text.Text);
    }

    // -------------------------------------------------------------------------
    // 34. Two audio assets uploaded — both resolved and transcribed separately
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EndToEnd_MultipleAssets_EachResolved()
    {
        // Arrange — upload two distinct audio blobs
        var bytes1 = new byte[] { 0x01, 0x02, 0x03 };
        var bytes2 = new byte[] { 0x04, 0x05, 0x06 };
        var contentStore = new InMemoryContentStore();
        const string sessionId = "e2e-session-multi";
        var assetId1 = await contentStore.PutAsync(sessionId, bytes1, "audio/mpeg");
        var assetId2 = await contentStore.PutAsync(sessionId, bytes2, "audio/mpeg");

        var stt = new SequentialCapturingSttClient(["first", "second"]);
        var middleware = new AudioPipelineMiddleware
        {
            SpeechToTextClient = stt,
            IOMode = AudioIOMode.AudioToText
        };

        var session = CreateSessionWithStore(sessionId, contentStore);
        var message = new ChatMessage(ChatRole.User, [
            new UriContent(new Uri($"asset://{assetId1}"), "audio/mpeg"),
            new UriContent(new Uri($"asset://{assetId2}"), "audio/mpeg")
        ]);
        var context = CreateBeforeIterationContext(session, [message]);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert — both STT calls made, text joined
        Assert.Equal(2, stt.CallCount);
        var text = Assert.IsType<TextContent>(Assert.Single(context.Messages[^1].Contents));
        Assert.Contains("first", text.Text);
        Assert.Contains("second", text.Text);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static SessionModel CreateSessionWithStore(string sessionId, IContentStore contentStore)
    {
        var store = new FixedContentSessionStore(contentStore);
        return new SessionModel(sessionId) { Store = store };
    }

    private static BeforeIterationContext CreateBeforeIterationContext(
        SessionModel? session,
        List<ChatMessage> messages)
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
            CancellationToken.None);

        return agentContext.AsBeforeIteration(
            iteration: 0,
            messages: messages,
            options: new ChatOptions(),
            runConfig: new AgentRunConfig());
    }

    // =========================================================================
    // STT fakes
    // =========================================================================

    private class CapturingSttClient : ISpeechToTextClient
    {
        private readonly string _result;
        public int CallCount { get; private set; }
        public byte[]? LastReceivedBytes { get; private set; }

        public CapturingSttClient(string result) => _result = result;

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            using var ms = new MemoryStream();
            audioSpeechStream.CopyTo(ms);
            LastReceivedBytes = ms.ToArray();
            return Task.FromResult(new SpeechToTextResponse(_result));
        }

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }

    private class SequentialCapturingSttClient : ISpeechToTextClient
    {
        private readonly string[] _results;
        private int _index;
        public int CallCount { get; private set; }

        public SequentialCapturingSttClient(string[] results) => _results = results;

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var result = _index < _results.Length ? _results[_index++] : "";
            return Task.FromResult(new SpeechToTextResponse(result));
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
}
