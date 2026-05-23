using HPD.Events.Core;

namespace HPD.Events.Tests;

public class ClockTests
{
    [Fact]
    public void SystemClock_UtcNowIsUtcAndUnixNanosIsPositive()
    {
        using var clock = new SystemClock();

        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
        Assert.True(clock.UnixNanos > 0);
    }

    [Fact]
    public void ManualClock_SetAndAdvanceMoveTimeDeterministically()
    {
        var start = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(start);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(start.AddSeconds(5), clock.UtcNow);

        var next = start.AddMinutes(1);
        clock.Set(next);
        Assert.Equal(next, clock.UtcNow);
    }

    [Fact]
    public void ManualClock_OneShotAlertFiresWhenAdvancedPastAlertTime()
    {
        var start = DateTimeOffset.UnixEpoch;
        var clock = new ManualClock(start);
        TimeEvent? fired = null;

        var handle = clock.SetAlert("open", start.AddSeconds(5), evt => fired = evt);
        clock.Advance(TimeSpan.FromSeconds(4));

        Assert.Null(fired);
        Assert.True(handle.IsActive);

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.NotNull(fired);
        Assert.Equal("open", fired.TimerName);
        Assert.Equal(start.AddSeconds(5), fired.TriggerTime);
        Assert.False(handle.IsActive);
        Assert.Empty(clock.TimerNames);
    }

    [Fact]
    public void ManualClock_RecurringTimerFiresDeterministicCounts()
    {
        var start = DateTimeOffset.UnixEpoch;
        var clock = new ManualClock(start);
        var fired = new List<DateTimeOffset>();

        clock.SetTimer("heartbeat", TimeSpan.FromSeconds(2), evt => fired.Add(evt.TriggerTime));
        clock.Advance(TimeSpan.FromSeconds(7));

        Assert.Equal(
            [start.AddSeconds(2), start.AddSeconds(4), start.AddSeconds(6)],
            fired);
    }
}
