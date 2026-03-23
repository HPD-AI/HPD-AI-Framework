using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Unit tests for SessionManager stream-lock and session-lock behaviour.
/// Covers RemoveBranchStreamLock, RemoveSession cleanup, and WithSessionLockAsync overloads.
/// </summary>
public class SessionManagerStreamLockTests : IDisposable
{
    private readonly InMemorySessionStore _store;
    private readonly TestSessionManagerImpl _manager;

    public SessionManagerStreamLockTests()
    {
        _store = new InMemorySessionStore();
        _manager = new TestSessionManagerImpl(_store);
    }

    public void Dispose() => _manager.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // RemoveBranchStreamLock
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryAcquireStreamLock_ReturnsFalse_WhenAlreadyAcquired()
    {
        _manager.TryAcquireStreamLock("session-1", "branch-a").Should().BeTrue();
        _manager.TryAcquireStreamLock("session-1", "branch-a").Should().BeFalse();
    }

    [Fact]
    public void RemoveBranchStreamLock_AllowsReacquisition_AfterRelease()
    {
        _manager.TryAcquireStreamLock("session-1", "branch-a");
        _manager.ReleaseStreamLock("session-1", "branch-a");
        _manager.RemoveBranchStreamLock("session-1", "branch-a");

        _manager.TryAcquireStreamLock("session-1", "branch-a").Should().BeTrue();
    }

    [Fact]
    public void RemoveBranchStreamLock_IsIdempotent_WhenKeyNotPresent()
    {
        var act = () => _manager.RemoveBranchStreamLock("session-x", "branch-x");
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveSession_CleansUpAllStreamLocks_ForSession()
    {
        _manager.TryAcquireStreamLock("session-a", "branch-1");
        _manager.TryAcquireStreamLock("session-a", "branch-2");
        _manager.TryAcquireStreamLock("session-a", "branch-3");
        _manager.TryAcquireStreamLock("session-b", "branch-1");

        _manager.ReleaseStreamLock("session-a", "branch-1");
        _manager.ReleaseStreamLock("session-a", "branch-2");
        _manager.ReleaseStreamLock("session-a", "branch-3");
        _manager.ReleaseStreamLock("session-b", "branch-1");

        _manager.RemoveSession("session-a");

        // session-a locks are gone — fresh semaphores created on acquire
        _manager.TryAcquireStreamLock("session-a", "branch-1").Should().BeTrue();
        _manager.TryAcquireStreamLock("session-a", "branch-2").Should().BeTrue();
        _manager.TryAcquireStreamLock("session-a", "branch-3").Should().BeTrue();

        // session-b lock was released above so reacquire is fine
        _manager.TryAcquireStreamLock("session-b", "branch-1").Should().BeTrue();
    }

    [Fact]
    public void RemoveSession_DoesNotCleanupOtherSessions_StreamLocks()
    {
        // Hold a lock on session-b
        _manager.TryAcquireStreamLock("session-b", "branch-z");

        // Remove a different session
        _manager.RemoveSession("session-a");

        // session-b lock must still be held
        _manager.TryAcquireStreamLock("session-b", "branch-z").Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // WithSessionLockAsync — non-generic (void) overload
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithSessionLockAsync_VoidOverload_ExecutesAction()
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
    public async Task WithSessionLockAsync_VoidOverload_PropagatesException()
    {
        Func<Task> act = async () => await _manager.WithSessionLockAsync("session-1", async () =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("test error");
        });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("test error");
    }

    [Fact]
    public async Task WithSessionLockAsync_VoidOverload_ReleasesLock_AfterException()
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

        // Lock released — generic overload must not deadlock
        var result = await _manager.WithSessionLockAsync("session-lock-release", () => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task WithSessionLockAsync_VoidOverload_IsExclusive_WithGenericOverload()
    {
        var order = new List<string>();
        var holdLock = new SemaphoreSlim(0, 1);   // void task waits on this to release the lock
        var lockAcquired = new SemaphoreSlim(0, 1); // signals that void task has the lock

        var voidTask = Task.Run(async () =>
        {
            await _manager.WithSessionLockAsync("session-exclusive", async () =>
            {
                order.Add("void-start");
                lockAcquired.Release();   // notify that we hold the lock
                await holdLock.WaitAsync(); // hold until told to release
                order.Add("void-end");
            });
        });

        // Wait until void task actually holds the lock before launching the generic task
        await lockAcquired.WaitAsync(TimeSpan.FromSeconds(5));

        var genericTask = Task.Run(async () =>
        {
            await _manager.WithSessionLockAsync<int>("session-exclusive", async () =>
            {
                order.Add("generic");
                return await Task.FromResult(1);
            });
        });

        // Give the generic task time to reach the lock contention point
        await Task.Delay(50);
        order.Should().NotContain("generic", "generic task should be blocked while void task holds lock");

        holdLock.Release();
        await Task.WhenAll(voidTask, genericTask);

        order.Should().ContainInOrder("void-start", "void-end", "generic");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test double
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TestSessionManagerImpl : SessionManager
    {
        public TestSessionManagerImpl(ISessionStore store) : base(store) { }
    }
}
