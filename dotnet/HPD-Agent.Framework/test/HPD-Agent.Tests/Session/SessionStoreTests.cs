using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent;

using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for ISessionStore implementations (InMemorySessionStore).
/// Covers V3 Session/Thread CRUD operations and cleanup.
/// </summary>
public class SessionStoreTests : AgentTestBase
{
    //──────────────────────────────────────────────────────────────────
    // INMEMORY SESSION STORE - SESSION CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_SaveAndLoadSession_RoundTrip()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session-1");
        session.AddMetadata("key", "value");

        // Act
        await store.SaveSessionAsync(session);
        var loaded = await store.LoadSessionAsync("test-session-1");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("test-session-1", loaded.Id);
        Assert.Equal("value", loaded.Metadata["key"]);
    }

    [Fact]
    public async Task InMemoryStore_LoadNonExistentSession_ReturnsNull()
    {
        // Arrange
        var store = new InMemorySessionStore();

        // Act
        var result = await store.LoadSessionAsync("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_RemovesSession()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-to-delete");
        session.AddMetadata("key", "value");
        await store.SaveSessionAsync(session);

        // Act
        await store.DeleteSessionAsync("session-to-delete");
        var loaded = await store.LoadSessionAsync("session-to-delete");

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_ListSessionIds_ReturnsAllSessions()
    {
        // Arrange
        var store = new InMemorySessionStore();
        await store.SaveSessionAsync(new HPD.Agent.Session("session-1"));
        await store.SaveSessionAsync(new HPD.Agent.Session("session-2"));
        await store.SaveSessionAsync(new HPD.Agent.Session("session-3"));

        // Act
        var ids = await store.ListSessionIdsAsync();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("session-1", ids);
        Assert.Contains("session-2", ids);
        Assert.Contains("session-3", ids);
    }

    [Fact]
    public async Task InMemoryStore_SaveSession_OverwritesPrevious()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("overwrite-session");
        session.AddMetadata("version", "1");
        await store.SaveSessionAsync(session);

        // Update metadata
        session.AddMetadata("version", "2");
        await store.SaveSessionAsync(session);

        // Act
        var loaded = await store.LoadSessionAsync("overwrite-session");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("2", loaded.Metadata["version"].ToString());
    }

    //──────────────────────────────────────────────────────────────────
    // INMEMORY SESSION STORE - THREAD CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_SaveAndLoadThread_RoundTrip()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("thread-1");
        thread.AddMessage(UserMessage("Hello"));
        thread.AddMessage(AssistantMessage("Hi there!"));

        // Act
        await store.SaveInitialThreadAsync("session-1", thread);
        var loaded = await store.LoadThreadAsync("session-1", "thread-1");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("thread-1", loaded.Id);
        Assert.Equal("session-1", loaded.SessionId);
        Assert.Equal(2, loaded.MessageCount);
    }

    [Fact]
    public async Task InMemoryStore_LoadNonExistentThread_ReturnsNull()
    {
        // Arrange
        var store = new InMemorySessionStore();

        // Act
        var result = await store.LoadThreadAsync("session-1", "non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteThread_RemovesThread()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("thread-to-delete");
        thread.AddMessage(UserMessage("Test"));
        await store.SaveInitialThreadAsync("session-1", thread);

        // Act
        await store.DeleteThreadAsync("session-1", "thread-to-delete");
        var loaded = await store.LoadThreadAsync("session-1", "thread-to-delete");

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_ListThreadIds_ReturnsAllThreads()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        await store.SaveInitialThreadAsync("session-1", session.CreateThread("thread-1"));
        await store.SaveInitialThreadAsync("session-1", session.CreateThread("thread-2"));
        await store.SaveInitialThreadAsync("session-1", session.CreateThread("thread-3"));

        // Act
        var ids = await store.ListThreadIdsAsync("session-1");

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("thread-1", ids);
        Assert.Contains("thread-2", ids);
        Assert.Contains("thread-3", ids);
    }

    [Fact]
    public async Task InMemoryStore_ListThreadIds_EmptyForNonExistentSession()
    {
        // Arrange
        var store = new InMemorySessionStore();

        // Act
        var ids = await store.ListThreadIdsAsync("non-existent-session");

        // Assert
        Assert.Empty(ids);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_AlsoDeletesThreads()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        await store.SaveSessionAsync(session);

        var thread = session.CreateThread("thread-1");
        thread.AddMessage(UserMessage("Hello"));
        await store.SaveInitialThreadAsync("session-1", thread);

        // Act
        await store.DeleteSessionAsync("session-1");

        // Assert
        Assert.Null(await store.LoadSessionAsync("session-1"));
        Assert.Null(await store.LoadThreadAsync("session-1", "thread-1"));
    }

    //──────────────────────────────────────────────────────────────────
    // INMEMORY SESSION STORE - CLEANUP
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_DeleteInactiveSessions_DryRun_DoesNotDelete()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("inactive-session");
        await store.SaveSessionAsync(session);
        await Task.Delay(50);

        // Act
        var count = await store.DeleteInactiveSessionsAsync(
            TimeSpan.FromMilliseconds(10), dryRun: true);

        // Assert
        Assert.Equal(1, count);
        Assert.NotNull(await store.LoadSessionAsync("inactive-session"));
    }

    [Fact]
    public async Task InMemoryStore_DeleteInactiveSessions_ActualDelete_RemovesSessions()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("inactive-session");
        await store.SaveSessionAsync(session);
        await Task.Delay(50);

        // Act
        var count = await store.DeleteInactiveSessionsAsync(
            TimeSpan.FromMilliseconds(10), dryRun: false);

        // Assert
        Assert.Equal(1, count);
        Assert.Null(await store.LoadSessionAsync("inactive-session"));
    }
}
