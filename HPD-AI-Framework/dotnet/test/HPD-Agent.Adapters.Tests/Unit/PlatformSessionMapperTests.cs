using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Adapters.Session;
using HPD.Agent.Adapters.Tests.TestInfrastructure;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="PlatformSessionMapper"/>.
/// Uses <see cref="InMemorySessionStore"/> and <see cref="TestSessionManager"/>
/// to exercise session resolution and reset without external dependencies.
/// </summary>
public class PlatformSessionMapperTests : IDisposable
{
    private readonly InMemorySessionStore _store;
    private readonly TestSessionManager   _manager;
    private readonly PlatformSessionMapper _mapper;

    public PlatformSessionMapperTests()
    {
        _store   = new InMemorySessionStore();
        _manager = new TestSessionManager(_store);
        _mapper  = new PlatformSessionMapper(_manager);
    }

    public void Dispose() => _manager.Dispose();

    // ── Constructor guards ─────────────────────────────────────────────

    [Fact]
    public void Constructor_NullManager_ThrowsArgumentNullException()
    {
        var act = () => new PlatformSessionMapper(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("manager");
    }

    // ── ResolveAsync argument validation ──────────────────────────────

    [Fact]
    public async Task ResolveAsync_NullPlatformKey_ThrowsArgumentException()
    {
        var act = () => _mapper.ResolveAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveAsync_EmptyPlatformKey_ThrowsArgumentException()
    {
        var act = () => _mapper.ResolveAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveAsync_WhitespacePlatformKey_ThrowsArgumentException()
    {
        var act = () => _mapper.ResolveAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── ResolveAsync — cache miss (no existing sessions) ──────────────

    [Fact]
    public async Task ResolveAsync_NoExistingSessions_CreatesNewSession()
    {
        var (sessionId, branchId) = await _mapper.ResolveAsync("slack:C123:111.000");

        sessionId.Should().NotBeNullOrEmpty();
        branchId.Should().Be("main");
    }

    [Fact]
    public async Task ResolveAsync_CreatesSession_WithPlatformKeyInMetadata()
    {
        const string key = "slack:C123:111.000";

        var (sessionId, _) = await _mapper.ResolveAsync(key);

        var session = await _store.LoadSessionAsync(sessionId);
        session.Should().NotBeNull();
        session!.Metadata.Should().ContainKey("platformKey")
            .WhoseValue.Should().Be(key);
    }

    // ── ResolveAsync — cache hit ───────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ExistingSession_ReturnsExistingSessionId()
    {
        const string key = "slack:C100:222.000";

        // First call creates the session
        var (firstId, _) = await _mapper.ResolveAsync(key);

        // Second call must return the same sessionId
        var (secondId, _) = await _mapper.ResolveAsync(key);

        secondId.Should().Be(firstId);
    }

    [Fact]
    public async Task ResolveAsync_ExistingSession_ReturnsBranchId()
    {
        const string key = "slack:C100:333.000";

        var (_, branchId) = await _mapper.ResolveAsync(key);

        branchId.Should().Be("main");
    }

    [Fact]
    public async Task ResolveAsync_MultipleSessionsOneMatch_ReturnsCorrectOne()
    {
        // Pre-create several sessions
        await _mapper.ResolveAsync("slack:C1:001.000");
        await _mapper.ResolveAsync("slack:C2:002.000");
        var (target, _) = await _mapper.ResolveAsync("slack:C3:003.000");
        await _mapper.ResolveAsync("slack:C4:004.000");

        // Resolve the target key — should find C3's session
        var (resolved, _) = await _mapper.ResolveAsync("slack:C3:003.000");

        resolved.Should().Be(target);
    }

    [Fact]
    public async Task ResolveAsync_DifferentKeys_CreatesSeparateSessions()
    {
        var (id1, _) = await _mapper.ResolveAsync("slack:C1:100.000");
        var (id2, _) = await _mapper.ResolveAsync("slack:C2:200.000");

        id1.Should().NotBe(id2);
    }

    // ── ResolveAsync — metadata edge cases ───────────────────────────

    [Fact]
    public async Task ResolveAsync_SessionWithMissingPlatformKey_NotMatchedCreatesNew()
    {
        // Create a session without any platformKey metadata
        await _manager.CreateSessionAsync();
        var sessionCount = (await _store.ListSessionIdsAsync()).Count;

        // Resolve with a key that no session has
        var (newSessionId, _) = await _mapper.ResolveAsync("slack:C99:999.000");

        // A new session should have been created
        var allIds = await _store.ListSessionIdsAsync();
        allIds.Should().HaveCount(sessionCount + 1);
        allIds.Should().Contain(newSessionId);
    }

    [Fact]
    public async Task ResolveAsync_MatchedSession_NoBranches_FallsBackToMain()
    {
        // Create a session via the mapper (which creates the "main" branch)
        const string key = "slack:C5:500.000";
        var (sessionId, _) = await _mapper.ResolveAsync(key);

        // Delete branches from the store to simulate a session with no branches
        var branches = await _store.ListBranchIdsAsync(sessionId);
        foreach (var b in branches)
            await _store.DeleteBranchAsync(sessionId, b);

        // Resolve again — no branches means fallback to "main"
        var (_, branchId) = await _mapper.ResolveAsync(key);

        branchId.Should().Be("main");
    }

    // ── ResolveAsync — cancellation ───────────────────────────────────

    [Fact]
    public async Task ResolveAsync_CancelledToken_CompletesOrThrows()
    {
        // InMemorySessionStore is synchronous — it may or may not check the token
        // before completing. This test verifies that passing a cancelled token does
        // not cause an unhandled exception (ArgumentException, ObjectDisposedException, etc.)
        // and either completes normally or throws OperationCanceledException.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await _mapper.ResolveAsync("slack:C1:1.0", cts.Token);
            // Completed without throwing — acceptable for synchronous in-memory store
        }
        catch (OperationCanceledException)
        {
            // Also acceptable
        }
    }

    // ── ResetAsync argument validation ───────────────────────────────

    [Fact]
    public async Task ResetAsync_NullPlatformKey_ThrowsArgumentException()
    {
        var act = () => _mapper.ResetAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResetAsync_EmptyPlatformKey_ThrowsArgumentException()
    {
        var act = () => _mapper.ResetAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── ResetAsync — no existing session ─────────────────────────────

    [Fact]
    public async Task ResetAsync_NoExistingSession_CreatesNewSession()
    {
        var (sessionId, branchId) = await _mapper.ResetAsync("slack:C6:600.000");

        sessionId.Should().NotBeNullOrEmpty();
        branchId.Should().Be("main");
    }

    // ── ResetAsync — existing session ─────────────────────────────────

    [Fact]
    public async Task ResetAsync_ExistingSession_DeletesOldAndCreatesNew()
    {
        const string key = "slack:C7:700.000";
        var (oldId, _) = await _mapper.ResolveAsync(key);

        var (newId, _) = await _mapper.ResetAsync(key);

        newId.Should().NotBe(oldId);
        // Old session should no longer exist
        var oldSession = await _store.LoadSessionAsync(oldId);
        oldSession.Should().BeNull();
    }

    [Fact]
    public async Task ResetAsync_NewSession_HasPlatformKeyInMetadata()
    {
        const string key = "slack:C8:800.000";
        await _mapper.ResolveAsync(key);

        var (newId, _) = await _mapper.ResetAsync(key);

        var session = await _store.LoadSessionAsync(newId);
        session.Should().NotBeNull();
        session!.Metadata.Should().ContainKey("platformKey")
            .WhoseValue.Should().Be(key);
    }

    [Fact]
    public async Task ResetAsync_AfterReset_ResolveSameKeyReturnsNewSession()
    {
        const string key = "slack:C9:900.000";
        await _mapper.ResolveAsync(key);
        var (resetId, _) = await _mapper.ResetAsync(key);

        // Subsequent resolve returns the new session, not the old one
        var (resolvedId, _) = await _mapper.ResolveAsync(key);

        resolvedId.Should().Be(resetId);
    }

    [Fact]
    public async Task ResetAsync_NoMatch_StillCreatesNewSession()
    {
        // Populate with unrelated sessions
        await _mapper.ResolveAsync("slack:other:1.0");
        await _mapper.ResolveAsync("slack:other:2.0");

        var (newId, _) = await _mapper.ResetAsync("slack:notexist:999.000");

        newId.Should().NotBeNullOrEmpty();
    }
}
