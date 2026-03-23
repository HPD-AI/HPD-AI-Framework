using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;
using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Session;

/// <summary>
/// Tests for UncommittedTurn CRUD operations on ISessionStore implementations
/// and JSON serialization round-trips.
/// </summary>
public class UncommittedTurnTests : AgentTestBase
{
    private static UncommittedTurn CreateTestTurn(string sessionId = "test-session")
    {
        return new UncommittedTurn
        {
            SessionId = sessionId,
            BranchId = UncommittedTurn.DefaultBranch,
            TurnMessages = new List<ChatMessage>
            {
                new(ChatRole.User, "Help me refactor"),
                new(ChatRole.Assistant, "I'll read the file first."),
            },
            Iteration = 2,
            CompletedFunctions = ImmutableHashSet.Create("read_file", "write_file"),
            MiddlewareState = new MiddlewareState(),
            IsTerminated = false,
            TerminationReason = null,
            CreatedAt = new DateTime(2026, 2, 5, 10, 0, 0, DateTimeKind.Utc),
            LastUpdatedAt = new DateTime(2026, 2, 5, 10, 1, 0, DateTimeKind.Utc),
        };
    }

    // ──────────────────────────────────────────────────────────────────
    // INMEMORY STORE
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_SaveAndLoadUncommittedTurn_RoundTrip()
    {
        var store = new InMemorySessionStore();
        var turn = CreateTestTurn();

        await store.SaveUncommittedTurnAsync(turn);
        var loaded = await store.LoadUncommittedTurnAsync("test-session");

        Assert.NotNull(loaded);
        Assert.Equal("test-session", loaded.SessionId);
        Assert.Equal(UncommittedTurn.DefaultBranch, loaded.BranchId);
        Assert.Equal(2, loaded.TurnMessages.Count);
        Assert.Equal(2, loaded.Iteration);
        Assert.Contains("read_file", loaded.CompletedFunctions);
        Assert.Contains("write_file", loaded.CompletedFunctions);
        Assert.False(loaded.IsTerminated);
        Assert.Null(loaded.TerminationReason);
        Assert.Equal(1, loaded.Version);
    }

    [Fact]
    public async Task InMemoryStore_LoadNonExistent_ReturnsNull()
    {
        var store = new InMemorySessionStore();

        var result = await store.LoadUncommittedTurnAsync("no-such-session");

        Assert.Null(result);
    }

    [Fact]
    public async Task InMemoryStore_DeleteUncommittedTurn_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        var turn = CreateTestTurn();

        await store.SaveUncommittedTurnAsync(turn);
        await store.DeleteUncommittedTurnAsync("test-session");
        var loaded = await store.LoadUncommittedTurnAsync("test-session");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_SaveOverwritesPrevious()
    {
        var store = new InMemorySessionStore();
        var turn1 = CreateTestTurn() with { Iteration = 1 };
        var turn2 = CreateTestTurn() with { Iteration = 5 };

        await store.SaveUncommittedTurnAsync(turn1);
        await store.SaveUncommittedTurnAsync(turn2);
        var loaded = await store.LoadUncommittedTurnAsync("test-session");

        Assert.NotNull(loaded);
        Assert.Equal(5, loaded.Iteration);
    }

    [Fact]
    public async Task InMemoryStore_DeleteSession_AlsoDeletesUncommittedTurn()
    {
        var store = new InMemorySessionStore();
        var session = new HPD.Agent.Session("test-session");
        await store.SaveSessionAsync(session);

        var turn = CreateTestTurn();
        await store.SaveUncommittedTurnAsync(turn);

        await store.DeleteSessionAsync("test-session");

        var loaded = await store.LoadUncommittedTurnAsync("test-session");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_DeleteNonExistentUncommittedTurn_NoOp()
    {
        var store = new InMemorySessionStore();

        // Should not throw
        await store.DeleteUncommittedTurnAsync("no-such-session");
    }

    // ──────────────────────────────────────────────────────────────────
    // JSON STORE
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task JsonStore_SaveAndLoadUncommittedTurn_RoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);
            var turn = CreateTestTurn();

            await store.SaveUncommittedTurnAsync(turn);
            var loaded = await store.LoadUncommittedTurnAsync("test-session");

            Assert.NotNull(loaded);
            Assert.Equal("test-session", loaded.SessionId);
            Assert.Equal(UncommittedTurn.DefaultBranch, loaded.BranchId);
            Assert.Equal(2, loaded.TurnMessages.Count);
            Assert.Equal(2, loaded.Iteration);
            Assert.False(loaded.IsTerminated);
            Assert.Equal(1, loaded.Version);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStore_LoadNonExistent_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);

            var result = await store.LoadUncommittedTurnAsync("no-such-session");

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStore_DeleteUncommittedTurn_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);
            var turn = CreateTestTurn();

            await store.SaveUncommittedTurnAsync(turn);
            await store.DeleteUncommittedTurnAsync("test-session");
            var loaded = await store.LoadUncommittedTurnAsync("test-session");

            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStore_DeleteSession_AlsoDeletesUncommittedTurn()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);
            var session = new HPD.Agent.Session("test-session");
            await store.SaveSessionAsync(session);

            var turn = CreateTestTurn();
            await store.SaveUncommittedTurnAsync(turn);

            await store.DeleteSessionAsync("test-session");

            var loaded = await store.LoadUncommittedTurnAsync("test-session");
            Assert.Null(loaded);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonStore_SaveOverwritesPrevious()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonSessionStore(tempDir);
            var turn1 = CreateTestTurn() with { Iteration = 1 };
            var turn2 = CreateTestTurn() with { Iteration = 5 };

            await store.SaveUncommittedTurnAsync(turn1);
            await store.SaveUncommittedTurnAsync(turn2);
            var loaded = await store.LoadUncommittedTurnAsync("test-session");

            Assert.NotNull(loaded);
            Assert.Equal(5, loaded.Iteration);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // JSON SERIALIZATION
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void UncommittedTurn_JsonSerialization_RoundTrip()
    {
        var turn = CreateTestTurn();

        var json = JsonSerializer.Serialize(turn, SessionJsonContext.CombinedOptions);
        var deserialized = JsonSerializer.Deserialize<UncommittedTurn>(json, SessionJsonContext.CombinedOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(turn.SessionId, deserialized.SessionId);
        Assert.Equal(turn.BranchId, deserialized.BranchId);
        Assert.Equal(turn.TurnMessages.Count, deserialized.TurnMessages.Count);
        Assert.Equal(turn.Iteration, deserialized.Iteration);
        Assert.Equal(turn.IsTerminated, deserialized.IsTerminated);
        Assert.Equal(turn.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(turn.LastUpdatedAt, deserialized.LastUpdatedAt);
        Assert.Equal(turn.Version, deserialized.Version);
    }

    [Fact]
    public void UncommittedTurn_TerminatedState_SerializesCorrectly()
    {
        var turn = CreateTestTurn() with
        {
            IsTerminated = true,
            TerminationReason = "Max iterations reached"
        };

        var json = JsonSerializer.Serialize(turn, SessionJsonContext.CombinedOptions);
        var deserialized = JsonSerializer.Deserialize<UncommittedTurn>(json, SessionJsonContext.CombinedOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsTerminated);
        Assert.Equal("Max iterations reached", deserialized.TerminationReason);
    }
}
