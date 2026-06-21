using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

/// <summary>
/// Tests for the SessionManager abstract base class.
/// Covers session lifecycle, thread operation locks, session locks, and RemoveSession behaviour.
/// </summary>
public class SessionManagerTests : IDisposable
{
    private readonly InMemorySessionStore _store;
    private readonly TestSessionManagerImpl _manager;

    public SessionManagerTests()
    {
        _store = new InMemorySessionStore();
        _manager = new TestSessionManagerImpl(_store);
    }

    public void Dispose() => _manager.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // Session lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_CreatesSession_WithGeneratedId()
    {
        var (sessionId, threadId) = await _manager.CreateSessionAsync();

        sessionId.Should().NotBeNullOrWhiteSpace();
        threadId.Should().Be("main");
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesSession_WithExplicitId()
    {
        var (sessionId, _) = await _manager.CreateSessionAsync("my-explicit-id");

        sessionId.Should().Be("my-explicit-id");
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesMainThread()
    {
        var (sessionId, _) = await _manager.CreateSessionAsync();

        var thread = await _store.LoadThreadAsync(sessionId, "main");
        thread.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateSessionAsync_PersistsMetadata()
    {
        var meta = new Dictionary<string, object> { ["source"] = "test" };
        var (sessionId, _) = await _manager.CreateSessionAsync(metadata: meta);

        var session = await _store.LoadSessionAsync(sessionId);
        session.Should().NotBeNull();
        session!.Metadata.Should().ContainKey("source");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RemoveSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveSession_CleansThreadOperationLocks_ForSession()
    {
        var (sid, _) = await _manager.CreateSessionAsync();

        _manager.TryAcquireThreadOperationLock(sid, "thread-a");
        _manager.TryAcquireThreadOperationLock(sid, "thread-b");
        _manager.ReleaseThreadOperationLock(sid, "thread-a");
        _manager.ReleaseThreadOperationLock(sid, "thread-b");

        _manager.RemoveSession(sid);

        // After removal fresh semaphores should be created — acquisition succeeds
        _manager.TryAcquireThreadOperationLock(sid, "thread-a").Should().BeTrue();
        _manager.TryAcquireThreadOperationLock(sid, "thread-b").Should().BeTrue();
    }

    [Fact]
    public async Task RemoveSession_DoesNotCleanLocks_ForOtherSessions()
    {
        var (sidA, _) = await _manager.CreateSessionAsync();
        var (sidB, _) = await _manager.CreateSessionAsync();

        // Hold a lock on session B
        _manager.TryAcquireThreadOperationLock(sidB, "thread-z");

        // Remove session A
        _manager.RemoveSession(sidA);

        // Session B lock should still be held
        _manager.TryAcquireThreadOperationLock(sidB, "thread-z").Should().BeFalse();
    }

    [Fact]
    public async Task RemoveSession_DoesNotDeleteStoreData()
    {
        var (sessionId, _) = await _manager.CreateSessionAsync();

        _manager.RemoveSession(sessionId);

        var session = await _store.LoadSessionAsync(sessionId);
        session.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveSession_ClearsActiveThreadRuns_ForSession()
    {
        var (sidA, _) = await _manager.CreateSessionAsync();
        var (sidB, _) = await _manager.CreateSessionAsync();

        _manager.TryStartThreadRun("agent", sidA, "main", out _).Should().BeTrue();
        _manager.TryStartThreadRun("agent", sidB, "main", out _).Should().BeTrue();

        _manager.RemoveSession(sidA);

        _manager.GetActiveThreadRun(sidA, "main").Should().BeNull();
        _manager.GetActiveThreadRun(sidB, "main").Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Thread operation locks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryAcquireThreadOperationLock_ReturnsTrue_FirstAcquisition()
    {
        _manager.TryAcquireThreadOperationLock("session-1", "thread-1").Should().BeTrue();
    }

    [Fact]
    public void TryAcquireThreadOperationLock_ReturnsFalse_WhenAlreadyHeld()
    {
        _manager.TryAcquireThreadOperationLock("session-1", "thread-1");
        _manager.TryAcquireThreadOperationLock("session-1", "thread-1").Should().BeFalse();
    }

    [Fact]
    public void TryAcquireThreadOperationLock_AllowsConcurrentLocks_DifferentThreads()
    {
        var l1 = _manager.TryAcquireThreadOperationLock("session-1", "thread-1");
        var l2 = _manager.TryAcquireThreadOperationLock("session-1", "thread-2");

        l1.Should().BeTrue();
        l2.Should().BeTrue();
    }

    [Fact]
    public void TryAcquireThreadOperationLock_AllowsConcurrentLocks_DifferentSessions()
    {
        var l1 = _manager.TryAcquireThreadOperationLock("session-1", "thread-1");
        var l2 = _manager.TryAcquireThreadOperationLock("session-2", "thread-1");

        l1.Should().BeTrue();
        l2.Should().BeTrue();
    }

    [Fact]
    public void ReleaseThreadOperationLock_AllowsReacquisition()
    {
        _manager.TryAcquireThreadOperationLock("session-1", "thread-1");
        _manager.ReleaseThreadOperationLock("session-1", "thread-1");

        _manager.TryAcquireThreadOperationLock("session-1", "thread-1").Should().BeTrue();
    }

    [Fact]
    public void ReleaseThreadOperationLock_IsIdempotent_WhenNotHeld()
    {
        var act = () => _manager.ReleaseThreadOperationLock("session-1", "thread-1");
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveThreadOperationLock_AllowsReacquisition_AfterRelease()
    {
        _manager.TryAcquireThreadOperationLock("session-1", "thread-a");
        _manager.ReleaseThreadOperationLock("session-1", "thread-a");
        _manager.RemoveThreadOperationLock("session-1", "thread-a");

        _manager.TryAcquireThreadOperationLock("session-1", "thread-a").Should().BeTrue();
    }

    [Fact]
    public void RemoveThreadOperationLock_IsIdempotent_WhenKeyNotPresent()
    {
        var act = () => _manager.RemoveThreadOperationLock("session-x", "thread-x");
        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Active thread runs
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryStartThreadRun_ReturnsFalse_WhenThreadAlreadyHasActiveRun()
    {
        _manager.TryStartThreadRun("agent", "session-1", "thread-1", out var first).Should().BeTrue();
        _manager.TryStartThreadRun("agent", "session-1", "thread-1", out var second).Should().BeFalse();

        second.Should().Be(first);
    }

    [Fact]
    public void TryStartThreadRun_AllowsDifferentThreadsAndSessions()
    {
        _manager.TryStartThreadRun("agent", "session-1", "thread-1", out _).Should().BeTrue();

        _manager.TryStartThreadRun("agent", "session-1", "thread-2", out _).Should().BeTrue();
        _manager.TryStartThreadRun("agent", "session-2", "thread-1", out _).Should().BeTrue();
    }

    [Fact]
    public void CompleteThreadRun_OnlyCompletesMatchingRuntimeRun()
    {
        _manager.TryStartThreadRun("agent", "session-1", "thread-1", out var run).Should().BeTrue();

        _manager.CompleteThreadRun("session-1", "thread-1", "other-run").Should().BeFalse();
        _manager.GetActiveThreadRun("session-1", "thread-1").Should().Be(run);

        _manager.CompleteThreadRun("session-1", "thread-1", run.RuntimeRunId).Should().BeTrue();
        _manager.GetActiveThreadRun("session-1", "thread-1").Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Session locks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithSessionLockAsync_Generic_ExecutesAction_AndReturnsValue()
    {
        var result = await _manager.WithSessionLockAsync("session-1", () => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task WithSessionLockAsync_Void_ExecutesAction()
    {
        var executed = false;
        await _manager.WithSessionLockAsync("session-1", async () =>
        {
            executed = true;
            await Task.CompletedTask;
        });
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task WithSessionLockAsync_PropagatesException()
    {
        Func<Task> act = () => _manager.WithSessionLockAsync("session-1", async () =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("test-error");
        });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("test-error");
    }

    [Fact]
    public async Task WithSessionLockAsync_ReleasesLock_AfterException()
    {
        try
        {
            await _manager.WithSessionLockAsync("session-lock-release", async () =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("boom");
            });
        }
        catch { /* expected */ }

        // Lock must have been released — next call must not deadlock
        var result = await _manager.WithSessionLockAsync("session-lock-release", () => Task.FromResult(99));
        result.Should().Be(99);
    }

    [Fact]
    public async Task WithSessionLockAsync_IsExclusive_BothOverloads()
    {
        var order = new List<string>();
        var barrier = new SemaphoreSlim(0, 1);

        var voidTask = Task.Run(async () =>
        {
            await _manager.WithSessionLockAsync("session-exclusive", async () =>
            {
                order.Add("void-start");
                await barrier.WaitAsync();
                order.Add("void-end");
            });
        });

        await Task.Delay(50);

        var genericTask = Task.Run(async () =>
        {
            await _manager.WithSessionLockAsync<int>("session-exclusive", async () =>
            {
                order.Add("generic");
                return await Task.FromResult(1);
            });
        });

        await Task.Delay(50);
        order.Should().NotContain("generic", "generic task should be blocked while void task holds lock");

        barrier.Release();
        await Task.WhenAll(voidTask, genericTask);

        order.Should().ContainInOrder("void-start", "void-end", "generic");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AllowRecursiveThreadDelete
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllowRecursiveThreadDelete_DefaultsToFalse()
    {
        _manager.AllowRecursiveThreadDelete.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test double
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TestSessionManagerImpl : SessionManager
    {
        public TestSessionManagerImpl(ISessionStore store) : base(store) { }
    }
}
