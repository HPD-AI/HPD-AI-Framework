using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class BarPeriodTests
{
    [Fact]
    public void BarPeriod_SecondsFactory()
    {
        var period = BarPeriod.Seconds(30);
        Assert.Equal(30, period.Duration.TotalSeconds);
    }

    [Fact]
    public void BarPeriod_MinutesFactory()
    {
        var period = BarPeriod.Minutes(5);
        Assert.Equal(5, period.Duration.TotalMinutes);
    }

    [Fact]
    public void BarPeriod_HoursFactory()
    {
        var period = BarPeriod.Hours(4);
        Assert.Equal(4, period.Duration.TotalHours);
    }

    [Fact]
    public void BarPeriod_DaysFactory()
    {
        var period = BarPeriod.Days(7);
        Assert.Equal(7, period.Duration.TotalDays);
    }

    [Fact]
    public void BarPeriod_M1()
    {
        var period = BarPeriod.M1;
        Assert.Equal(1, period.Duration.TotalMinutes);
    }

    [Fact]
    public void BarPeriod_M5()
    {
        var period = BarPeriod.M5;
        Assert.Equal(5, period.Duration.TotalMinutes);
    }

    [Fact]
    public void BarPeriod_M15()
    {
        var period = BarPeriod.M15;
        Assert.Equal(15, period.Duration.TotalMinutes);
    }

    [Fact]
    public void BarPeriod_M30()
    {
        var period = BarPeriod.M30;
        Assert.Equal(30, period.Duration.TotalMinutes);
    }

    [Fact]
    public void BarPeriod_H1()
    {
        var period = BarPeriod.H1;
        Assert.Equal(1, period.Duration.TotalHours);
    }

    [Fact]
    public void BarPeriod_H4()
    {
        var period = BarPeriod.H4;
        Assert.Equal(4, period.Duration.TotalHours);
    }

    [Fact]
    public void BarPeriod_D1()
    {
        var period = BarPeriod.D1;
        Assert.Equal(1, period.Duration.TotalDays);
    }

    [Fact]
    public void BarPeriod_ToStringSeconds()
    {
        var period = BarPeriod.Seconds(30);
        Assert.Equal("30s", period.ToString());
    }

    [Fact]
    public void BarPeriod_ToStringMinutes()
    {
        var period = BarPeriod.Minutes(15);
        Assert.Equal("15m", period.ToString());
    }

    [Fact]
    public void BarPeriod_ToStringHours()
    {
        var period = BarPeriod.Hours(4);
        Assert.Equal("4h", period.ToString());
    }

    [Fact]
    public void BarPeriod_ToStringDays()
    {
        var period = BarPeriod.Days(7);
        Assert.Equal("7d", period.ToString());
    }

    [Fact]
    public void BarPeriod_Equality()
    {
        var p1 = BarPeriod.M5;
        var p2 = BarPeriod.Minutes(5);

        Assert.Equal(p1, p2);
    }

    [Fact]
    public void BarPeriod_Inequality()
    {
        var p1 = BarPeriod.M5;
        var p2 = BarPeriod.M15;

        Assert.NotEqual(p1, p2);
    }
}
