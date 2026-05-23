using HPD.Events;
using HPD.Events.Core;

namespace Rhodium.Connectivity.Tests;

public class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsWallClockTime()
    {
        using var clock = new SystemClock();

        var before = DateTimeOffset.UtcNow;
        var now = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(now, before.AddSeconds(-1), after.AddSeconds(1));
        Assert.True(clock.UnixNanos > 0);
    }

    [Fact]
    public void UnixNanos_UsesTickPrecision()
    {
        using var clock = new SystemClock();

        var now = clock.UtcNow;
        var unixNanos = clock.UnixNanos;
        var expected = (now.ToUniversalTime().Ticks - DateTimeOffset.UnixEpoch.Ticks) * 100L;

        Assert.InRange(unixNanos, expected - 1_000_000_000L, expected + 1_000_000_000L);
        Assert.Equal(0, unixNanos % 100);
    }

    [Fact]
    public async Task SetAlert_FiresOnceAndDeactivates()
    {
        using var clock = new SystemClock();
        var fired = new TaskCompletionSource<TimeEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handle = clock.SetAlert(
            "open",
            TimeSpan.FromMilliseconds(10),
            evt => fired.TrySetResult(evt));

        var completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(fired.Task, completed);
        Assert.Equal("open", fired.Task.Result.TimerName);
        Assert.False(handle.IsActive);
        Assert.Empty(clock.TimerNames);
    }

    [Fact]
    public void CancelTimer_RemovesActiveTimer()
    {
        using var clock = new SystemClock();
        var callbackCount = 0;

        var handle = clock.SetTimer(
            "heartbeat",
            TimeSpan.FromSeconds(10),
            _ => Interlocked.Increment(ref callbackCount));

        Assert.True(handle.IsActive);
        Assert.Contains("heartbeat", clock.TimerNames);

        clock.CancelTimer("heartbeat");

        Assert.False(handle.IsActive);
        Assert.Empty(clock.TimerNames);
        Assert.Equal(0, callbackCount);
    }
}
