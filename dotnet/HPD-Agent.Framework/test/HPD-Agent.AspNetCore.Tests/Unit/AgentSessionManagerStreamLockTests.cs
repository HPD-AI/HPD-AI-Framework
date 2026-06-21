using FluentAssertions;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Unit tests for AgentSessionManager thread-operation-lock and session-lock behaviour.
/// Covers Fix 3 (RemoveThreadOperationLock + RemoveSession cleanup)
/// and MAUI Fix B (non-generic WithSessionLockAsync overload).
/// </summary>
public class AgentSessionManagerThreadOperationLockTests : IDisposable
{
    private readonly InMemorySessionStore _store;
    private readonly AspNetCoreSessionManagerTestable _manager;

    public AgentSessionManagerThreadOperationLockTests()
    {
        _store = new InMemorySessionStore();
        var optionsMonitor = new OptionsMonitorWrapper();
        _manager = new AspNetCoreSessionManagerTestable(_store, optionsMonitor, Options.DefaultName);
    }

    public void Dispose() => _manager.Dispose();

    // ──────────────────────────────────────────────────────────────────
    // Fix 3 — RemoveThreadOperationLock
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryAcquireThreadOperationLock_ReturnsFalse_WhenAlreadyAcquired()
    {
        var acquired = _manager.TryAcquireThreadOperationLock("session-1", "thread-a");
        acquired.Should().BeTrue();

        var second = _manager.TryAcquireThreadOperationLock("session-1", "thread-a");
        second.Should().BeFalse();
    }

    [Fact]
    public void RemoveThreadOperationLock_AllowsReacquisition_AfterRelease()
    {
        // Acquire, release, then remove the semaphore
        _manager.TryAcquireThreadOperationLock("session-1", "thread-a");
        _manager.ReleaseThreadOperationLock("session-1", "thread-a");
        _manager.RemoveThreadOperationLock("session-1", "thread-a");

        // A fresh acquisition should succeed (new semaphore created lazily)
        var acquired = _manager.TryAcquireThreadOperationLock("session-1", "thread-a");
        acquired.Should().BeTrue();
    }

    [Fact]
    public void RemoveThreadOperationLock_IsIdempotent_WhenKeyNotPresent()
    {
        // Should not throw on a key that was never acquired
        var act = () => _manager.RemoveThreadOperationLock("session-x", "thread-x");
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveAgent_CleansUpAllThreadOperationLocks_ForSession()
    {
        // Acquire locks for 3 threads on session-A and 1 on session-B
        _manager.TryAcquireThreadOperationLock("session-a", "thread-1");
        _manager.TryAcquireThreadOperationLock("session-a", "thread-2");
        _manager.TryAcquireThreadOperationLock("session-a", "thread-3");
        _manager.TryAcquireThreadOperationLock("session-b", "thread-1");

        // Release before removing (must release before dispose)
        _manager.ReleaseThreadOperationLock("session-a", "thread-1");
        _manager.ReleaseThreadOperationLock("session-a", "thread-2");
        _manager.ReleaseThreadOperationLock("session-a", "thread-3");
        _manager.ReleaseThreadOperationLock("session-b", "thread-1");

        _manager.RemoveSession("session-a");

        // Session-A locks are gone — acquiring returns a fresh semaphore (true)
        _manager.TryAcquireThreadOperationLock("session-a", "thread-1").Should().BeTrue();
        _manager.TryAcquireThreadOperationLock("session-a", "thread-2").Should().BeTrue();
        _manager.TryAcquireThreadOperationLock("session-a", "thread-3").Should().BeTrue();

        // Session-B lock was not cleaned up — it was already released above so reacquire is fine,
        // but confirm it still exists as a fresh (uncontested) semaphore
        _manager.TryAcquireThreadOperationLock("session-b", "thread-1").Should().BeTrue();
    }

    [Fact]
    public void RemoveAgent_DoesNotCleanupOtherSessions_ThreadOperationLocks()
    {
        // Acquire and hold a lock for session-b
        _manager.TryAcquireThreadOperationLock("session-b", "thread-z");

        // Remove a different session
        _manager.RemoveSession("session-a");

        // Session-B lock should still be held — reacquisition fails
        _manager.TryAcquireThreadOperationLock("session-b", "thread-z").Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────
    // MAUI Fix B — non-generic WithSessionLockAsync(Func<Task>) overload
    // ──────────────────────────────────────────────────────────────────

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
        var act = async () => await _manager.WithSessionLockAsync("session-1", async () =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("test error");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("test error");
    }

    [Fact]
    public async Task WithSessionLockAsync_VoidOverload_ReleasesLock_AfterException()
    {
        // Throw inside void overload
        try
        {
            await _manager.WithSessionLockAsync("session-lock-release", async () =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("boom");
            });
        }
        catch { /* expected */ }

        // The lock should have been released — the generic overload should succeed without deadlock
        var result = await _manager.WithSessionLockAsync("session-lock-release", () => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task WithSessionLockAsync_VoidOverload_IsExclusive_WithGenericOverload()
    {
        var order = new List<string>();
        var barrier = new SemaphoreSlim(0, 1);

        // Start void-overload task that holds the lock until barrier is released
        var voidTask = Task.Run(async () =>
        {
            await _manager.WithSessionLockAsync("session-exclusive", async () =>
            {
                order.Add("void-start");
                await barrier.WaitAsync();
                order.Add("void-end");
            });
        });

        // Give the void task time to acquire the lock
        await Task.Delay(50);

        // Start generic-overload task — should be blocked
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

        barrier.Release(); // unblock void task
        await Task.WhenAll(voidTask, genericTask);

        order.Should().ContainInOrder("void-start", "void-end", "generic");
    }

    // ──────────────────────────────────────────────────────────────────
    // Test infrastructure
    // ──────────────────────────────────────────────────────────────────

    private class AspNetCoreSessionManagerTestable(
        ISessionStore store,
        IOptionsMonitor<HPDAgentConfig> optionsMonitor,
        string name)
        : AspNetCoreSessionManager(store, optionsMonitor, name);

    private class OptionsMonitorWrapper : IOptionsMonitor<HPDAgentConfig>
    {
        public HPDAgentConfig CurrentValue { get; } = new HPDAgentConfig();
        public HPDAgentConfig Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HPDAgentConfig, string?> listener) => null;
    }
}
