// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;
using SessionModel = global::HPD.Agent.Session;

namespace HPD.Agent.Tests.Middleware.V2;

/// <summary>
/// Tests for ModelRequest.Session — the property added to give middleware
/// access to session storage during model calls (e.g. TTS artifact upload).
/// </summary>
public class ModelRequestSessionTests
{
    // -------------------------------------------------------------------------
    // 30. Session defaults to null when not provided
    // -------------------------------------------------------------------------

    [Fact]
    public void ModelRequest_Session_DefaultsToNull()
    {
        // Arrange & Act
        var request = new ModelRequest
        {
            Model = new TestChatClient(),
            Messages = [],
            Options = new ChatOptions(),
            State = CreateTestState(),
            Iteration = 0
        };

        // Assert
        Assert.Null(request.Session);
    }

    // -------------------------------------------------------------------------
    // 31. Session can be set and retrieved
    // -------------------------------------------------------------------------

    [Fact]
    public void ModelRequest_Session_CanBeSet()
    {
        // Arrange
        var session = new SessionModel("test-session-id");

        // Act
        var request = new ModelRequest
        {
            Model = new TestChatClient(),
            Messages = [],
            Options = new ChatOptions(),
            State = CreateTestState(),
            Iteration = 0,
            Session = session
        };

        // Assert
        Assert.Same(session, request.Session);
        Assert.Equal("test-session-id", request.Session.Id);
    }

    // -------------------------------------------------------------------------
    // 32. Override() preserves Session unchanged
    // -------------------------------------------------------------------------

    [Fact]
    public void ModelRequest_Override_PreservesSession()
    {
        // Arrange
        var session = new SessionModel("preserve-me");
        var original = new ModelRequest
        {
            Model = new TestChatClient(),
            Messages = [new ChatMessage(ChatRole.User, "original")],
            Options = new ChatOptions { Temperature = 0.5f },
            State = CreateTestState(),
            Iteration = 0,
            Session = session
        };

        // Act — override messages only
        var modified = original.Override(
            messages: [new ChatMessage(ChatRole.User, "modified")]);

        // Assert — Session is still the same instance on the modified copy
        Assert.Same(session, modified.Session);
        Assert.Equal("preserve-me", modified.Session!.Id);

        // And the original is untouched
        Assert.Same(session, original.Session);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static AgentLoopState CreateTestState()
        => AgentLoopState.InitialSafe([], "run123", "conv123", "TestAgent");

    private class TestChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("test");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public TService? GetService<TService>(object? serviceKey = null) where TService : class => null;
        public void Dispose() => GC.SuppressFinalize(this);
    }
}
