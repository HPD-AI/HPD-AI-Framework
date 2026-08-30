using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for session metadata, thread persistence, and cleanup across built-in stores.
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);

        // Act
        var result = await store.LoadSessionAsync("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_RemovesSession()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
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

    [Fact]
    public async Task FileStore_SessionMetadataCrudAndThreadCascade_RoundTripAcrossReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-session-store-{Guid.NewGuid():N}");
        try
        {
            var session = new HPD.Agent.Session("session-1");
            session.AddMetadata("title", "original");
            session.MiddlewareState["permission"] = "allow";
            var first = new FileSessionStore(directory, HPD.Agent.Tests.TestEventApplication.Codec);
            await first.SaveSessionAsync(session);
            await first.SaveInitialThreadAsync(session.Id, session.CreateThread("test-agent", "main"));

            session.AddMetadata("title", "updated");
            await first.SaveSessionAsync(session);

            var reopened = new FileSessionStore(directory, HPD.Agent.Tests.TestEventApplication.Codec);
            var loaded = await reopened.LoadSessionAsync(session.Id);
            var ids = await reopened.ListSessionIdsAsync();

            Assert.NotNull(loaded);
            Assert.Equal("updated", loaded.Metadata["title"].ToString());
            Assert.Equal("allow", loaded.MiddlewareState["permission"]);
            Assert.Contains(session.Id, ids);
            Assert.NotNull(await reopened.GetThreadAsync(new ThreadKey(session.Id, "main")));

            await reopened.DeleteSessionAsync(session.Id);

            Assert.Null(await reopened.LoadSessionAsync(session.Id));
            Assert.Null(await reopened.GetThreadAsync(new ThreadKey(session.Id, "main")));
            Assert.DoesNotContain(session.Id, await reopened.ListSessionIdsAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileStore_UnknownDurableEvent_ReportsSafeTypedCoordinates()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-session-store-{Guid.NewGuid():N}");
        try
        {
            var codec = HPD.Agent.Tests.TestEventApplication.Codec;
            var store = new FileSessionStore(directory, codec);
            await store.AppendThreadEventAsync(
                "session-safe",
                "main",
                new TextDeltaEvent("secret-payload", "message-1"));
            var segment = Directory.EnumerateFiles(directory, "segment-*.events", SearchOption.AllDirectories).Single();
            var journal = await File.ReadAllTextAsync(segment);
            await File.WriteAllTextAsync(segment, journal.Replace(
                EventTypes.Content.TEXT_DELTA,
                "UNKNOWN_DURABLE_FIXTURE",
                StringComparison.Ordinal));

            var reopened = new FileSessionStore(directory, codec);
            Func<Task> action = async () =>
                await reopened.CollectThreadEventsAsync("session-safe", "main");

            var exception = await Assert.ThrowsAsync<UnknownDurableAgentEventException>(action);
            Assert.Equal("UNKNOWN_DURABLE_FIXTURE", exception.Discriminator);
            Assert.Equal("session-safe", exception.SessionId);
            Assert.Equal("main", exception.ThreadId);
            Assert.Equal(1, exception.JournalGeneration);
            Assert.DoesNotContain("secret-payload", exception.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FileStore_DeleteInactiveSessions_HonorsDryRunAndDeletesTheSessionTree()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hpd-session-store-{Guid.NewGuid():N}");
        try
        {
            var store = new FileSessionStore(directory, HPD.Agent.Tests.TestEventApplication.Codec);
            var session = new HPD.Agent.Session("inactive-session");
            await store.SaveSessionAsync(session);
            await store.SaveInitialThreadAsync(session.Id, session.CreateThread("test-agent", "main"));
            var sessionDirectory = Path.Combine(directory, "sessions", session.Id);
            Directory.SetLastWriteTimeUtc(sessionDirectory, DateTime.UtcNow.Subtract(TimeSpan.FromDays(2)));

            Assert.Equal(1, await store.DeleteInactiveSessionsAsync(TimeSpan.FromDays(1), dryRun: true));
            Assert.True(Directory.Exists(sessionDirectory));

            Assert.Equal(1, await store.DeleteInactiveSessionsAsync(TimeSpan.FromDays(1)));
            Assert.False(Directory.Exists(sessionDirectory));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    //──────────────────────────────────────────────────────────────────
    // INMEMORY SESSION STORE - THREAD CRUD
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_SaveAndLoadThread_RoundTrip()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("test-agent", "thread-1");
        thread.AddMessage(UserMessage("Hello"));
        thread.AddMessage(AssistantMessage("Hi there!"));

        // Act
        await store.SaveInitialThreadAsync("session-1", thread);
        var loaded = await store.ProjectThreadAsync("session-1", "thread-1", ThreadProjectionPurpose.ThreadHistory);

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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);

        // Act
        var result = await store.ProjectThreadAsync("session-1", "non-existent", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteThread_RemovesThread()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var session = new HPD.Agent.Session("session-1");
        var thread = session.CreateThread("test-agent", "thread-to-delete");
        thread.AddMessage(UserMessage("Test"));
        await store.SaveInitialThreadAsync("session-1", thread);

        // Act
        await store.DeleteThreadAsync("session-1", "thread-to-delete");
        var loaded = await store.ProjectThreadAsync("session-1", "thread-to-delete", ThreadProjectionPurpose.ThreadHistory);

        // Assert
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_ListThreads_ReturnsAllDescriptors()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var session = new HPD.Agent.Session("session-1");
        await store.SaveInitialThreadAsync("session-1", session.CreateThread("test-agent", "thread-1"));
        await store.SaveInitialThreadAsync("session-1", session.CreateThread("test-agent", "thread-2"));
        await store.SaveInitialThreadAsync("session-1", session.CreateThread("test-agent", "thread-3"));

        // Act
        var descriptors = await store.CollectThreadDescriptorsAsync("session-1");
        var ids = descriptors.Select(item => item.Key.ThreadId).ToList();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("thread-1", ids);
        Assert.Contains("thread-2", ids);
        Assert.Contains("thread-3", ids);
    }

    [Fact]
    public async Task InMemoryStore_ListThreads_EmptyForNonExistentSession()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);

        // Act
        var ids = await store.CollectThreadDescriptorsAsync("non-existent-session");

        // Assert
        Assert.Empty(ids);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_AlsoDeletesThreads()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
        var session = new HPD.Agent.Session("session-1");
        await store.SaveSessionAsync(session);

        var thread = session.CreateThread("test-agent", "thread-1");
        thread.AddMessage(UserMessage("Hello"));
        await store.SaveInitialThreadAsync("session-1", thread);

        // Act
        await store.DeleteSessionAsync("session-1");

        // Assert
        Assert.Null(await store.LoadSessionAsync("session-1"));
        Assert.Null(await store.ProjectThreadAsync("session-1", "thread-1", ThreadProjectionPurpose.ThreadHistory));
    }

    //──────────────────────────────────────────────────────────────────
    // INMEMORY SESSION STORE - CLEANUP
    //──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_DeleteInactiveSessions_DryRun_DoesNotDelete()
    {
        // Arrange
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
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
        var store = new InMemorySessionStore(HPD.Agent.Tests.TestEventApplication.Codec);
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
