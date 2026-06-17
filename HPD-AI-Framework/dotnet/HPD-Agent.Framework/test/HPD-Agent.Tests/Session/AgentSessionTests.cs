using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for V3 Session and Thread types.
/// Covers construction, message operations, metadata, display name, execution state, and store property.
/// </summary>
public class AgentSessionTests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // SESSION - CONSTRUCTION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Session_DefaultConstructor_GeneratesId()
    {
        // Arrange & Act
        var session = new HPD.Agent.Session();

        // Assert
        Assert.NotNull(session.Id);
        Assert.NotEmpty(session.Id);
        Assert.True(Guid.TryParse(session.Id, out _));
    }

    [Fact]
    public void Session_WithId_UsesProvidedId()
    {
        // Arrange & Act
        var session = new HPD.Agent.Session("custom-session-id");

        // Assert
        Assert.Equal("custom-session-id", session.Id);
    }

    [Fact]
    public void Session_WithId_ThrowsOnNullOrWhitespace()
    {
        Assert.Throws<ArgumentNullException>(() => new HPD.Agent.Session(null!));
        Assert.Throws<ArgumentException>(() => new HPD.Agent.Session(""));
        Assert.Throws<ArgumentException>(() => new HPD.Agent.Session("   "));
    }

    [Fact]
    public void Session_CreatedAt_SetToNow()
    {
        // Arrange
        var before = DateTime.UtcNow;

        // Act
        var session = new HPD.Agent.Session();

        // Assert
        var after = DateTime.UtcNow;
        Assert.InRange(session.CreatedAt, before, after);
    }

    //──────────────────────────────────────────────────────────────────
    // THREAD - CONSTRUCTION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Thread_Constructor_GeneratesId()
    {
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();
        Assert.NotNull(thread.Id);
        Assert.NotEmpty(thread.Id);
        Assert.Equal("session-1", thread.SessionId);
    }

    [Fact]
    public void Thread_WithId_UsesProvidedId()
    {
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("thread-1");
        Assert.Equal("thread-1", thread.Id);
        Assert.Equal("session-1", thread.SessionId);
    }

    //──────────────────────────────────────────────────────────────────
    // THREAD - MESSAGE OPERATIONS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Thread_AddMessage_AddsToCollection()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();

        // Act
        thread.AddMessage(UserMessage("Hello"));
        thread.AddMessage(AssistantMessage("Hi!"));

        // Assert
        Assert.Equal(2, thread.MessageCount);
        Assert.Equal("Hello", thread.Messages[0].Text);
        Assert.Equal("Hi!", thread.Messages[1].Text);
    }

    [Fact]
    public void Thread_AddMessages_AddsMultiple()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();
        var messages = new List<ChatMessage>
        {
            UserMessage("One"),
            AssistantMessage("Two"),
            UserMessage("Three")
        };

        // Act
        thread.AddMessages(messages);

        // Assert
        Assert.Equal(3, thread.MessageCount);
    }

    [Fact]
    public void Thread_Clear_RemovesAllMessages()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();
        thread.AddMessage(UserMessage("Hello"));

        // Act
        thread.Clear();

        // Assert
        Assert.Empty(thread.Messages);
    }

    [Fact]
    public void Thread_AddMessage_UpdatesLastActivity()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();
        var initialActivity = thread.LastActivity;
        System.Threading.Thread.Sleep(10);

        // Act
        thread.AddMessage(UserMessage("Hello"));

        // Assert
        Assert.True(thread.LastActivity > initialActivity);
    }

    //──────────────────────────────────────────────────────────────────
    // SESSION - METADATA
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Session_AddMetadata_StoresValue()
    {
        // Arrange
        var session = new HPD.Agent.Session();

        // Act
        session.AddMetadata("customKey", "customValue");

        // Assert
        Assert.Equal("customValue", session.Metadata["customKey"]);
    }

    //──────────────────────────────────────────────────────────────────
    // THREAD - DISPLAY NAME
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Thread_GetDisplayName_FromFirstUserMessage()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();
        thread.AddMessage(UserMessage("Hello, how are you today?"));

        // Act
        var displayName = thread.GetDisplayName(maxLength: 15);

        // Assert
        Assert.Equal("Hello, how a...", displayName);
    }

    [Fact]
    public void Thread_GetDisplayName_FallsBackToId()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();

        // Act
        var displayName = thread.GetDisplayName();

        // Assert — falls back to thread ID when no messages
        Assert.Equal(thread.Id, displayName);
    }

    [Fact]
    public void Thread_GetDisplayName_PreferDescription()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();
        thread.AddMessage(UserMessage("Some user message"));
        thread.Description = "Custom Name";

        // Act
        var displayName = thread.GetDisplayName();

        // Assert
        Assert.Equal("Custom Name", displayName);
    }

    //──────────────────────────────────────────────────────────────────
    // THREAD - EXECUTION STATE
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Thread_ExecutionState_GetSet()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread();
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(), "run-123", "conv-456", "TestAgent");

        // Act
        thread.ExecutionState = state;

        // Assert
        Assert.NotNull(thread.ExecutionState);
        Assert.Equal("run-123", thread.ExecutionState.RunId);
    }

    //──────────────────────────────────────────────────────────────────
    // SESSION - STORE PROPERTY
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Session_Store_DefaultsToNull()
    {
        // Arrange & Act
        var session = new HPD.Agent.Session();

        // Assert
        Assert.Null(session.Store);
    }

    [Fact]
    public void Session_Store_CanBeSet()
    {
        // Arrange
        var session = new HPD.Agent.Session();
        var store = new InMemorySessionStore();

        // Act
        session.Store = store;

        // Assert
        Assert.Same(store, session.Store);
    }

    [Fact]
    public void Session_Store_NotSerializedToJson()
    {
        // Arrange
        var session = new HPD.Agent.Session("test-session");
        session.AddMetadata("key", "value");

        // Set a store reference
        var store = new InMemorySessionStore();
        session.Store = store;

        // Act — Serialize the session
        var json = System.Text.Json.JsonSerializer.Serialize(session);

        // Assert — JSON should NOT contain "Store" property
        Assert.DoesNotContain("\"Store\"", json);
        Assert.DoesNotContain("\"store\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
