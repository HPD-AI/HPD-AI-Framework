using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for V3 Session and Branch types.
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
    // BRANCH - CONSTRUCTION
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Branch_Constructor_GeneratesId()
    {
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();
        Assert.NotNull(branch.Id);
        Assert.NotEmpty(branch.Id);
        Assert.Equal("session-1", branch.SessionId);
    }

    [Fact]
    public void Branch_WithId_UsesProvidedId()
    {
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("branch-1");
        Assert.Equal("branch-1", branch.Id);
        Assert.Equal("session-1", branch.SessionId);
    }

    //──────────────────────────────────────────────────────────────────
    // BRANCH - MESSAGE OPERATIONS
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Branch_AddMessage_AddsToCollection()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();

        // Act
        branch.AddMessage(UserMessage("Hello"));
        branch.AddMessage(AssistantMessage("Hi!"));

        // Assert
        Assert.Equal(2, branch.MessageCount);
        Assert.Equal("Hello", branch.Messages[0].Text);
        Assert.Equal("Hi!", branch.Messages[1].Text);
    }

    [Fact]
    public void Branch_AddMessages_AddsMultiple()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();
        var messages = new List<ChatMessage>
        {
            UserMessage("One"),
            AssistantMessage("Two"),
            UserMessage("Three")
        };

        // Act
        branch.AddMessages(messages);

        // Assert
        Assert.Equal(3, branch.MessageCount);
    }

    [Fact]
    public void Branch_Clear_RemovesAllMessages()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();
        branch.AddMessage(UserMessage("Hello"));

        // Act
        branch.Clear();

        // Assert
        Assert.Empty(branch.Messages);
    }

    [Fact]
    public void Branch_AddMessage_UpdatesLastActivity()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();
        var initialActivity = branch.LastActivity;
        System.Threading.Thread.Sleep(10);

        // Act
        branch.AddMessage(UserMessage("Hello"));

        // Assert
        Assert.True(branch.LastActivity > initialActivity);
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
    // BRANCH - DISPLAY NAME
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Branch_GetDisplayName_FromFirstUserMessage()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();
        branch.AddMessage(UserMessage("Hello, how are you today?"));

        // Act
        var displayName = branch.GetDisplayName(maxLength: 15);

        // Assert
        Assert.Equal("Hello, how a...", displayName);
    }

    [Fact]
    public void Branch_GetDisplayName_FallsBackToId()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();

        // Act
        var displayName = branch.GetDisplayName();

        // Assert — falls back to branch ID when no messages
        Assert.Equal(branch.Id, displayName);
    }

    [Fact]
    public void Branch_GetDisplayName_PreferDescription()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();
        branch.AddMessage(UserMessage("Some user message"));
        branch.Description = "Custom Name";

        // Act
        var displayName = branch.GetDisplayName();

        // Assert
        Assert.Equal("Custom Name", displayName);
    }

    //──────────────────────────────────────────────────────────────────
    // BRANCH - EXECUTION STATE
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public void Branch_ExecutionState_GetSet()
    {
        // Arrange
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch();
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(), "run-123", "conv-456", "TestAgent");

        // Act
        branch.ExecutionState = state;

        // Assert
        Assert.NotNull(branch.ExecutionState);
        Assert.Equal("run-123", branch.ExecutionState.RunId);
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
