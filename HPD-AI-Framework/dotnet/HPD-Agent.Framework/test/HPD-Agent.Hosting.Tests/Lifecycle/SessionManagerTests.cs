using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

/// <summary>
/// Tests for the SessionManager abstract base class.
/// Covers session lifecycle, branch operation locks, session locks, and RemoveSession behaviour.
/// </summary>
public class SessionManagerTests : IDisposable
{
    private readonly ISessionRepository _repository;
    private readonly TestSessionManagerImpl _manager;

    public SessionManagerTests()
    {
        _repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        _manager = new TestSessionManagerImpl(_repository);
    }

    public void Dispose() => _manager.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // Session lifecycle
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessionAsync_CreatesSession_WithGeneratedId()
    {
        var (sessionId, branchId) = await _manager.CreateSessionAsync();

        sessionId.Should().NotBeNullOrWhiteSpace();
        branchId.Should().Be("main");
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesSession_WithExplicitId()
    {
        var (sessionId, _) = await _manager.CreateSessionAsync("my-explicit-id");

        sessionId.Should().Be("my-explicit-id");
    }

    [Fact]
    public async Task CreateSessionAsync_CreatesMainBranch()
    {
        var (sessionId, _) = await _manager.CreateSessionAsync();

        var branch = await _repository.LoadBranchAsync(sessionId, "main");
        branch.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateSessionAsync_PersistsMetadata()
    {
        var meta = new Dictionary<string, object> { ["source"] = "test" };
        var (sessionId, _) = await _manager.CreateSessionAsync(metadata: meta);

        var session = await _repository.LoadSessionAsync(sessionId);
        session.Should().NotBeNull();
        session!.Metadata.Should().ContainKey("source");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RemoveSession
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveSession_CleansBranchOperationLocks_ForSession()
    {
        var (sid, _) = await _manager.CreateSessionAsync();

        _manager.TryAcquireBranchOperationLock(sid, "branch-a");
        _manager.TryAcquireBranchOperationLock(sid, "branch-b");
        _manager.ReleaseBranchOperationLock(sid, "branch-a");
        _manager.ReleaseBranchOperationLock(sid, "branch-b");

        _manager.RemoveSession(sid);

        // After removal fresh semaphores should be created — acquisition succeeds
        _manager.TryAcquireBranchOperationLock(sid, "branch-a").Should().BeTrue();
        _manager.TryAcquireBranchOperationLock(sid, "branch-b").Should().BeTrue();
    }

    [Fact]
    public async Task RemoveSession_DoesNotCleanLocks_ForOtherSessions()
    {
        var (sidA, _) = await _manager.CreateSessionAsync();
        var (sidB, _) = await _manager.CreateSessionAsync();

        // Hold a lock on session B
        _manager.TryAcquireBranchOperationLock(sidB, "branch-z");

        // Remove session A
        _manager.RemoveSession(sidA);

        // Session B lock should still be held
        _manager.TryAcquireBranchOperationLock(sidB, "branch-z").Should().BeFalse();
    }

    [Fact]
    public async Task RemoveSession_DoesNotDeleteRepositoryData()
    {
        var (sessionId, _) = await _manager.CreateSessionAsync();

        _manager.RemoveSession(sessionId);

        var session = await _repository.LoadSessionAsync(sessionId);
        session.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveSession_ClearsActiveBranchRuns_ForSession()
    {
        var (sidA, _) = await _manager.CreateSessionAsync();
        var (sidB, _) = await _manager.CreateSessionAsync();

        _manager.TryStartBranchRun("agent", sidA, "main", out _).Should().BeTrue();
        _manager.TryStartBranchRun("agent", sidB, "main", out _).Should().BeTrue();

        _manager.RemoveSession(sidA);

        _manager.GetActiveBranchRun(sidA, "main").Should().BeNull();
        _manager.GetActiveBranchRun(sidB, "main").Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Branch operation locks
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryAcquireBranchOperationLock_ReturnsTrue_FirstAcquisition()
    {
        _manager.TryAcquireBranchOperationLock("session-1", "branch-1").Should().BeTrue();
    }

    [Fact]
    public void TryAcquireBranchOperationLock_ReturnsFalse_WhenAlreadyHeld()
    {
        _manager.TryAcquireBranchOperationLock("session-1", "branch-1");
        _manager.TryAcquireBranchOperationLock("session-1", "branch-1").Should().BeFalse();
    }

    [Fact]
    public void TryAcquireBranchOperationLock_AllowsConcurrentLocks_DifferentBranches()
    {
        var l1 = _manager.TryAcquireBranchOperationLock("session-1", "branch-1");
        var l2 = _manager.TryAcquireBranchOperationLock("session-1", "branch-2");

        l1.Should().BeTrue();
        l2.Should().BeTrue();
    }

    [Fact]
    public void TryAcquireBranchOperationLock_AllowsConcurrentLocks_DifferentSessions()
    {
        var l1 = _manager.TryAcquireBranchOperationLock("session-1", "branch-1");
        var l2 = _manager.TryAcquireBranchOperationLock("session-2", "branch-1");

        l1.Should().BeTrue();
        l2.Should().BeTrue();
    }

    [Fact]
    public void ReleaseBranchOperationLock_AllowsReacquisition()
    {
        _manager.TryAcquireBranchOperationLock("session-1", "branch-1");
        _manager.ReleaseBranchOperationLock("session-1", "branch-1");

        _manager.TryAcquireBranchOperationLock("session-1", "branch-1").Should().BeTrue();
    }

    [Fact]
    public void ReleaseBranchOperationLock_IsIdempotent_WhenNotHeld()
    {
        var act = () => _manager.ReleaseBranchOperationLock("session-1", "branch-1");
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveBranchOperationLock_AllowsReacquisition_AfterRelease()
    {
        _manager.TryAcquireBranchOperationLock("session-1", "branch-a");
        _manager.ReleaseBranchOperationLock("session-1", "branch-a");
        _manager.RemoveBranchOperationLock("session-1", "branch-a");

        _manager.TryAcquireBranchOperationLock("session-1", "branch-a").Should().BeTrue();
    }

    [Fact]
    public void RemoveBranchOperationLock_IsIdempotent_WhenKeyNotPresent()
    {
        var act = () => _manager.RemoveBranchOperationLock("session-x", "branch-x");
        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Active branch runs
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryStartBranchRun_ReturnsFalse_WhenBranchAlreadyHasActiveRun()
    {
        _manager.TryStartBranchRun("agent", "session-1", "branch-1", out var first).Should().BeTrue();
        _manager.TryStartBranchRun("agent", "session-1", "branch-1", out var second).Should().BeFalse();

        second.Should().Be(first);
    }

    [Fact]
    public void TryStartBranchRun_AllowsDifferentBranchesAndSessions()
    {
        _manager.TryStartBranchRun("agent", "session-1", "branch-1", out _).Should().BeTrue();

        _manager.TryStartBranchRun("agent", "session-1", "branch-2", out _).Should().BeTrue();
        _manager.TryStartBranchRun("agent", "session-2", "branch-1", out _).Should().BeTrue();
    }

    [Fact]
    public void CompleteBranchRun_OnlyCompletesMatchingRuntimeRun()
    {
        _manager.TryStartBranchRun("agent", "session-1", "branch-1", out var run).Should().BeTrue();

        _manager.CompleteBranchRun("session-1", "branch-1", "other-run").Should().BeFalse();
        _manager.GetActiveBranchRun("session-1", "branch-1").Should().Be(run);

        _manager.CompleteBranchRun("session-1", "branch-1", run.RuntimeRunId).Should().BeTrue();
        _manager.GetActiveBranchRun("session-1", "branch-1").Should().BeNull();
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
    // AllowRecursiveBranchDelete
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllowRecursiveBranchDelete_DefaultsToFalse()
    {
        _manager.AllowRecursiveBranchDelete.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test double
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TestSessionManagerImpl : SessionManager
    {
        public TestSessionManagerImpl(ISessionRepository repository) : base(repository) { }
    }
}
