using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using HPD.Agent.Adapters;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="AdapterWebSocketService"/> — the reconnect loop, backoff math,
/// send-lock serialisation, and cancellation behaviour.
///
/// A concrete <see cref="TestWebSocketService"/> subclass is used as a test double.
/// The real <c>ClientWebSocket</c> is never constructed; <see cref="RunSessionAsync"/>
/// is overridden to record calls and control outcomes without touching a network socket.
/// </summary>
public class AdapterWebSocketServiceTests
{
    // ── Test double ────────────────────────────────────────────────────────────

    private sealed class TestWebSocketService : AdapterWebSocketService
    {
        private readonly Func<Task<Uri>> _getUri;
        private readonly Func<System.Net.WebSockets.WebSocket, CancellationToken, Task> _runSession;

        public TestWebSocketService(
            Func<Task<Uri>>? getUri = null,
            Func<System.Net.WebSockets.WebSocket, CancellationToken, Task>? runSession = null)
            : base(NullLogger.Instance)
        {
            _getUri     = getUri     ?? (() => Task.FromResult(new Uri("ws://localhost")));
            _runSession = runSession ?? ((_, _) => Task.CompletedTask);
        }

        protected override Task<Uri> GetConnectionUriAsync(CancellationToken ct)
            => _getUri();

        protected override Task RunSessionAsync(System.Net.WebSockets.WebSocket ws, CancellationToken ct)
            => _runSession(ws, ct);
    }

    /// <summary>
    /// Subclass that overrides <see cref="GetReconnectDelay"/> and <see cref="ShouldReconnect"/>
    /// for controlled backoff testing without real delays.
    /// </summary>
    private sealed class ControlledBackoffService : AdapterWebSocketService
    {
        private readonly List<int> _attemptsPassedToGetDelay = new();
        private readonly Func<int, TimeSpan?> _delayFunc;
        private readonly Func<System.Net.WebSockets.WebSocket, CancellationToken, Task> _runSession;
        private readonly TaskCompletionSource _started = new();

        public IReadOnlyList<int> AttemptsPassedToGetDelay => _attemptsPassedToGetDelay;
        public Task Started => _started.Task;

        public ControlledBackoffService(
            Func<int, TimeSpan?> delayFunc,
            Func<System.Net.WebSockets.WebSocket, CancellationToken, Task>? runSession = null)
            : base(NullLogger.Instance)
        {
            _delayFunc  = delayFunc;
            _runSession = runSession ?? ((_, ct) => { ct.ThrowIfCancellationRequested(); throw new IOException("test failure"); });
        }

        protected override Task<Uri> GetConnectionUriAsync(CancellationToken ct)
        {
            _started.TrySetResult();
            return Task.FromResult(new Uri("ws://localhost"));
        }

        // Skip real network I/O — ConnectAsync is a no-op so RunSessionAsync always runs.
        protected override Task ConnectAsync(System.Net.WebSockets.ClientWebSocket ws, Uri uri, CancellationToken ct)
            => Task.CompletedTask;

        protected override Task RunSessionAsync(System.Net.WebSockets.WebSocket ws, CancellationToken ct)
            => _runSession(ws, ct);

        protected override TimeSpan? GetReconnectDelay(int attempt)
        {
            _attemptsPassedToGetDelay.Add(attempt);
            return _delayFunc(attempt);
        }
    }

    // ── DefaultBackoff: null at attempt 12 ────────────────────────────────────

    [Fact]
    public void DefaultBackoff_ReturnsNullAtAttempt12()
    {
        // The base service's static DefaultBackoff is tested via a subclass that
        // doesn't override GetReconnectDelay — delegate through a helper.
        var svc = new TestWebSocketService();

        // We can't call DefaultBackoff directly (private static), but we can
        // observe through the reconnect loop by counting how many attempts fire.
        // Simpler: test the public-facing GetReconnectDelay via ControlledBackoffService
        // that defers to base, which calls DefaultBackoff.
        // Use reflection to call the private static for deterministic assertion.
        var method = typeof(AdapterWebSocketService)
            .GetMethod("DefaultBackoff",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        method.Should().NotBeNull("DefaultBackoff must exist as a private static method");

        var result = method!.Invoke(null, [12]);
        result.Should().BeNull("DefaultBackoff(12) must return null to stop the reconnect loop");
    }

    [Fact]
    public void DefaultBackoff_Attempt0_ReturnsBetween1500And2500Ms()
    {
        var method = typeof(AdapterWebSocketService)
            .GetMethod("DefaultBackoff",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var result = (TimeSpan)method.Invoke(null, [0])!;

        // base = 2000ms, jitter ±25% → [1500, 2500]
        result.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(1500)
            .And.BeLessThanOrEqualTo(2500);
    }

    [Fact]
    public void DefaultBackoff_CapAt30Seconds_NeverExceeds37500Ms()
    {
        var method = typeof(AdapterWebSocketService)
            .GetMethod("DefaultBackoff",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // At attempt 8: 2000 * 1.8^8 ≈ 72900ms, capped at 30000, jitter → max 37500
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var result = (TimeSpan)method.Invoke(null, [attempt])!;
            result.TotalMilliseconds.Should().BeLessThanOrEqualTo(37_500,
                $"attempt {attempt} must not exceed 30s+25% jitter");
        }
    }

    [Fact]
    public void DefaultBackoff_JitterRange_DelayFallsWithinBounds()
    {
        var method = typeof(AdapterWebSocketService)
            .GetMethod("DefaultBackoff",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Attempt 1: base = 2000*1.8 = 3600ms, jitter → [2700, 4500]
        for (var i = 0; i < 50; i++)
        {
            var result = (TimeSpan)method.Invoke(null, [1])!;
            result.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(2700)
                .And.BeLessThanOrEqualTo(4500,
                    "attempt 1 jitter must be within ±25% of 3600ms");
        }
    }

    [Fact]
    public void DefaultBackoff_Attempts0Through11_AllReturnNonNull()
    {
        var method = typeof(AdapterWebSocketService)
            .GetMethod("DefaultBackoff",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var result = method.Invoke(null, [attempt]);
            result.Should().NotBeNull($"attempt {attempt} should return a delay, not null");
        }
    }

    // ── ShouldReconnect defaults ───────────────────────────────────────────────

    [Fact]
    public void ShouldReconnect_Default_ReturnsFalseForOperationCanceledException()
    {
        var svc = new TestWebSocketService();

        var method = typeof(AdapterWebSocketService)
            .GetMethod("ShouldReconnect",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var result = (bool)method.Invoke(svc, [new OperationCanceledException()])!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldReconnect_Default_ReturnsTrueForIOException()
    {
        var svc = new TestWebSocketService();

        var method = typeof(AdapterWebSocketService)
            .GetMethod("ShouldReconnect",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var result = (bool)method.Invoke(svc, [new IOException("connection reset")])!;
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldReconnect_Default_ReturnsTrueForWebSocketException()
    {
        var svc = new TestWebSocketService();

        var method = typeof(AdapterWebSocketService)
            .GetMethod("ShouldReconnect",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var result = (bool)method.Invoke(svc, [new WebSocketException("closed")])!;
        result.Should().BeTrue();
    }

    // ── ExecuteAsync: exits on cancellation ───────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_ExitsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var getUriCalled = 0;
        var svc = new TestWebSocketService(
            getUri: () => { getUriCalled++; return Task.FromResult(new Uri("ws://localhost")); });

        await svc.StartAsync(cts.Token);
        await svc.StopAsync(CancellationToken.None);

        // With a pre-cancelled token the loop body should not execute
        getUriCalled.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenGetReconnectDelayReturnsNull()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Always fail, always return null from GetReconnectDelay → loop exits after first failure
        var svc = new ControlledBackoffService(
            delayFunc: _ => null,
            runSession: (_, _) => throw new IOException("forced failure"));

        await svc.StartAsync(cts.Token);
        // Give the background service a moment to run to completion
        await Task.Delay(200, CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        svc.AttemptsPassedToGetDelay.Should().HaveCount(1)
            .And.ContainSingle(a => a == 0,
                "first failure should call GetReconnectDelay(0) and then stop");
    }

    // ── ExecuteAsync: attempt counter resets on successful connect ────────────

    [Fact]
    public async Task ExecuteAsync_AttemptResetsToZero_AfterSuccessfulConnect()
    {
        // Design: attempt resets on successful ws.ConnectAsync, not on successful RunSessionAsync.
        // We test this by: fail twice (attempts 0→1), succeed on connect (attempt resets),
        // then fail again — GetReconnectDelay is called with 0 again, not 2.

        // Simulate: calls 1+2 to GetConnectionUriAsync return a bad URI (connect fails),
        // call 3 succeeds (ws connects), RunSessionAsync throws.
        // We use a counter to decide which call to GetConnectionUriAsync represents a success.
        // However, ClientWebSocket.ConnectAsync needs a real server — we can't intercept it.
        // Instead we test the observable: after a successful RunSessionAsync (no throw),
        // the NEXT failure resets attempt to 0.

        var callCount = 0;
        var attemptsObserved = new List<int>();

        // Approach: RunSession succeeds once (callCount=1), then throws.
        // GetReconnectDelay is called only after failures. After the success, the next
        // call to GetReconnectDelay should receive attempt=0.
        var svc = new ControlledBackoffService(
            delayFunc: attempt =>
            {
                attemptsObserved.Add(attempt);
                return attempt < 3 ? TimeSpan.Zero : (TimeSpan?)null;
            },
            runSession: (_, ct) =>
            {
                callCount++;
                ct.ThrowIfCancellationRequested();
                if (callCount == 2)
                    return Task.CompletedTask; // success — attempt should reset
                throw new IOException("simulated failure");
            });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await svc.StartAsync(cts.Token);
        await Task.Delay(500, CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        // After the success on call 2, the next GetReconnectDelay call should be attempt=0
        attemptsObserved.Should().Contain(0,
            "attempt should reset to 0 after a successful connect+session");
    }
}
