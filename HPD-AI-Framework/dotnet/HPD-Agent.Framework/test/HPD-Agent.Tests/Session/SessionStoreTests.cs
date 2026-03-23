using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent;

using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for ISessionStore implementations (InMemorySessionStore).
/// Covers V3 Session/Branch CRUD operations and cleanup.
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
    // INMEMORY SESSION STORE - BRANCH CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_SaveAndLoadBranch_RoundTrip()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("branch-1");
        branch.AddMessage(UserMessage("Hello"));
        branch.AddMessage(AssistantMessage("Hi there!"));

        // Act
        await store.SaveBranchAsync("session-1", branch);
        var loaded = await store.LoadBranchAsync("session-1", "branch-1");

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("branch-1", loaded.Id);
        Assert.Equal("session-1", loaded.SessionId);
        Assert.Equal(2, loaded.MessageCount);
    }

    [Fact]
    public async Task InMemoryStore_LoadNonExistentBranch_ReturnsNull()
    {
        // Arrange
        var store = new InMemorySessionStore();

        // Act
        var result = await store.LoadBranchAsync("session-1", "non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteBranch_RemovesBranch()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("branch-to-delete");
        branch.AddMessage(UserMessage("Test"));
        await store.SaveBranchAsync("session-1", branch);

        // Act
        await store.DeleteBranchAsync("session-1", "branch-to-delete");
        var loaded = await store.LoadBranchAsync("session-1", "branch-to-delete");

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_ListBranchIds_ReturnsAllBranches()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        await store.SaveBranchAsync("session-1", session.CreateBranch("branch-1"));
        await store.SaveBranchAsync("session-1", session.CreateBranch("branch-2"));
        await store.SaveBranchAsync("session-1", session.CreateBranch("branch-3"));

        // Act
        var ids = await store.ListBranchIdsAsync("session-1");

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("branch-1", ids);
        Assert.Contains("branch-2", ids);
        Assert.Contains("branch-3", ids);
    }

    [Fact]
    public async Task InMemoryStore_ListBranchIds_EmptyForNonExistentSession()
    {
        // Arrange
        var store = new InMemorySessionStore();

        // Act
        var ids = await store.ListBranchIdsAsync("non-existent-session");

        // Assert
        Assert.Empty(ids);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_AlsoDeletesBranches()
    {
        // Arrange
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("session-1");
        await store.SaveSessionAsync(session);

        var branch = session.CreateBranch("branch-1");
        branch.AddMessage(UserMessage("Hello"));
        await store.SaveBranchAsync("session-1", branch);

        // Act
        await store.DeleteSessionAsync("session-1");

        // Assert
        Assert.Null(await store.LoadSessionAsync("session-1"));
        Assert.Null(await store.LoadBranchAsync("session-1", "branch-1"));
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
