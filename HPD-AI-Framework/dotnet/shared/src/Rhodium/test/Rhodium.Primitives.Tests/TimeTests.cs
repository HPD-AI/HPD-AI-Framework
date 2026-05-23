using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class InstantTests
{
    [Fact]
    public void Instant_ShouldStoreNanoseconds()
    {
        // Arrange & Act
        var instant = new Instant(1000000000); // 1 second

        // Assert
        Assert.Equal(1000000000, instant.Nanos);
    }

    [Fact]
    public void Instant_ShouldConvertFromDateTimeOffset()
    {
        // Arrange
        var dto = new DateTimeOffset(2026, 1, 30, 12, 0, 0, TimeSpan.Zero);

        // Act
        var instant = Instant.FromDateTimeOffset(dto);

        // Assert
        Assert.Equal(dto, instant.ToDateTimeOffset());
    }

    [Fact]
    public void Instant_ShouldPreserveDateTimeOffsetTickPrecision()
    {
        var dto = new DateTimeOffset(2026, 1, 30, 12, 0, 0, TimeSpan.Zero)
            .AddTicks(1234);

        var instant = Instant.FromDateTimeOffset(dto);

        Assert.Equal(123_400, instant.Nanos % 1_000_000);
        Assert.Equal(dto, instant.ToDateTimeOffset());
    }

    [Fact]
    public void Instant_ShouldConvertFromUnixMillis()
    {
        // Arrange & Act
        var instant = Instant.FromUnixMillis(1000);

        // Assert
        Assert.Equal(1000 * 1_000_000, instant.Nanos);
    }

    [Fact]
    public void Instant_ShouldConvertFromUnixSeconds()
    {
        // Arrange & Act
        var instant = Instant.FromUnixSeconds(1);

        // Assert
        Assert.Equal(1_000_000_000, instant.Nanos);
    }

    [Fact]
    public void Instant_ShouldSupportComparison()
    {
        // Arrange
        var earlier = Instant.FromUnixSeconds(100);
        var later = Instant.FromUnixSeconds(200);

        // Act & Assert
        Assert.True(later > earlier);
        Assert.True(earlier < later);
        Assert.True(later >= earlier);
        Assert.True(earlier <= later);
    }

    [Fact]
    public void Instant_ShouldSupportMaxMin()
    {
        // Arrange
        var a = Instant.FromUnixSeconds(100);
        var b = Instant.FromUnixSeconds(200);

        // Act & Assert
        Assert.Equal(b, Instant.Max(a, b));
        Assert.Equal(a, Instant.Min(a, b));
    }

    [Fact]
    public void Instant_ShouldSupportDurationArithmetic()
    {
        // Arrange
        var instant = Instant.FromUnixSeconds(100);
        var duration = Duration.FromSeconds(50);

        // Act & Assert
        Assert.Equal(Instant.FromUnixSeconds(150), instant + duration);
        Assert.Equal(Instant.FromUnixSeconds(50), instant - duration);
    }

    [Fact]
    public void Instant_ShouldComputeDurationBetweenInstants()
    {
        // Arrange
        var earlier = Instant.FromUnixSeconds(100);
        var later = Instant.FromUnixSeconds(150);

        // Act
        var duration = later - earlier;

        // Assert
        Assert.Equal(Duration.FromSeconds(50), duration);
    }

    [Fact]
    public void Instant_ShouldHaveConstants()
    {
        // Assert
        Assert.Equal(long.MinValue, Instant.MinValue.Nanos);
        Assert.Equal(long.MaxValue, Instant.MaxValue.Nanos);
        Assert.Equal(0, Instant.Epoch.Nanos);
    }
}

public class DurationTests
{
    [Fact]
    public void Duration_ShouldStoreNanoseconds()
    {
        // Arrange & Act
        var duration = new Duration(1000000000); // 1 second

        // Assert
        Assert.Equal(1000000000, duration.Nanos);
    }

    [Fact]
    public void Duration_ShouldConvertFromVariousUnits()
    {
        // Act & Assert
        Assert.Equal(1000L, Duration.FromNanos(1000).Nanos);
        Assert.Equal(1000L * 1_000, Duration.FromMicros(1000).Nanos);
        Assert.Equal(1000L * 1_000_000, Duration.FromMillis(1000).Nanos);
        Assert.Equal(5L * 1_000_000_000, Duration.FromSeconds(5).Nanos);
        Assert.Equal(5L * 60 * 1_000_000_000, Duration.FromMinutes(5).Nanos);
        Assert.Equal(2L * 60 * 60 * 1_000_000_000, Duration.FromHours(2).Nanos);
    }

    [Fact]
    public void Duration_ShouldConvertFromFractionalUnits()
    {
        // Act & Assert
        Assert.Equal(1_500_000_000L, Duration.Seconds(1.5).Nanos);
        Assert.Equal(90_000_000_000L, Duration.Minutes(1.5).Nanos);
    }

    [Fact]
    public void Duration_ShouldConvertToTimeSpan()
    {
        // Arrange
        var duration = Duration.FromSeconds(100);

        // Act
        var timeSpan = duration.ToTimeSpan();

        // Assert
        Assert.Equal(100, timeSpan.TotalSeconds);
    }

    [Fact]
    public void Duration_ShouldProvideTotalProperties()
    {
        // Arrange
        var duration = Duration.Hours(2.5);

        // Act & Assert
        Assert.Equal(2.5, duration.TotalHours);
        Assert.Equal(150, duration.TotalMinutes);
        Assert.Equal(9000, duration.TotalSeconds);
    }

    [Fact]
    public void Duration_ShouldSupportArithmetic()
    {
        // Arrange
        var a = Duration.FromSeconds(100);
        var b = Duration.FromSeconds(50);

        // Act & Assert
        Assert.Equal(Duration.FromSeconds(150), a + b);
        Assert.Equal(Duration.FromSeconds(50), a - b);
        Assert.Equal(Duration.FromSeconds(200), a * 2);
        Assert.Equal(Duration.FromSeconds(50), a / 2);
    }

    [Fact]
    public void Duration_ShouldSupportComparison()
    {
        // Arrange
        var shorter = Duration.FromSeconds(50);
        var longer = Duration.FromSeconds(100);

        // Act & Assert
        Assert.True(longer > shorter);
        Assert.True(shorter < longer);
    }

    [Fact]
    public void Duration_ShouldSupportImplicitConversionFromTimeSpan()
    {
        // Arrange
        var timeSpan = TimeSpan.FromSeconds(100);

        // Act
        Duration duration = timeSpan;

        // Assert
        Assert.Equal(100, duration.TotalSeconds);
    }

    [Fact]
    public void Duration_ShouldHaveZeroConstant()
    {
        // Assert
        Assert.Equal(0, Duration.Zero.Nanos);
    }
}

public class DualTimestampTests
{
    [Fact]
    public void DualTimestamp_ShouldStoreExchangeAndLocalTime()
    {
        // Arrange
        var exchTime = Instant.FromUnixSeconds(1000);
        var localTime = Instant.FromUnixSeconds(1001);

        // Act
        var dual = new DualTimestamp(exchTime, localTime);

        // Assert
        Assert.Equal(exchTime, dual.ExchangeTime);
        Assert.Equal(localTime, dual.LocalTime);
    }

    [Fact]
    public void DualTimestamp_ShouldCalculateFeedLatency()
    {
        // Arrange
        var exchTime = Instant.FromUnixMillis(1000);
        var localTime = Instant.FromUnixMillis(1050);
        var dual = new DualTimestamp(exchTime, localTime);

        // Act
        var latency = dual.FeedLatency;

        // Assert
        Assert.Equal(Duration.FromMillis(50), latency);
    }

    [Fact]
    public void DualTimestamp_ShouldSupportSynchronized()
    {
        // Arrange
        var time = Instant.FromUnixSeconds(1000);

        // Act
        var dual = DualTimestamp.Synchronized(time);

        // Assert
        Assert.Equal(time, dual.ExchangeTime);
        Assert.Equal(time, dual.LocalTime);
        Assert.Equal(Duration.Zero, dual.FeedLatency);
    }

    [Fact]
    public void DualTimestamp_Now_ShouldCaptureCurrentLocal()
    {
        // Arrange
        var exchTime = Instant.FromUnixSeconds(1000);
        var before = Instant.Now;

        // Act
        var dual = DualTimestamp.Now(exchTime);

        var after = Instant.Now;

        // Assert
        Assert.Equal(exchTime, dual.ExchangeTime);
        Assert.True(dual.LocalTime >= before);
        Assert.True(dual.LocalTime <= after);
    }
}

public class DateRangeTests
{
    [Fact]
    public void DateRange_ShouldStoreStartAndEnd()
    {
        // Arrange
        var start = Instant.FromUnixSeconds(1000);
        var end = Instant.FromUnixSeconds(2000);

        // Act
        var range = new DateRange(start, end);

        // Assert
        Assert.Equal(start, range.Start);
        Assert.Equal(end, range.End);
    }

    [Fact]
    public void DateRange_ShouldCalculateDuration()
    {
        // Arrange
        var start = Instant.FromUnixSeconds(1000);
        var end = Instant.FromUnixSeconds(2000);
        var range = new DateRange(start, end);

        // Act
        var duration = range.Duration;

        // Assert
        Assert.Equal(Duration.FromSeconds(1000), duration);
    }

    [Fact]
    public void DateRange_ShouldCheckContainment()
    {
        // Arrange
        var range = new DateRange(
            Instant.FromUnixSeconds(1000),
            Instant.FromUnixSeconds(2000)
        );

        // Act & Assert
        Assert.True(range.Contains(Instant.FromUnixSeconds(1500)));
        Assert.True(range.Contains(Instant.FromUnixSeconds(1000))); // inclusive
        Assert.True(range.Contains(Instant.FromUnixSeconds(2000))); // inclusive
        Assert.False(range.Contains(Instant.FromUnixSeconds(500)));
        Assert.False(range.Contains(Instant.FromUnixSeconds(3000)));
    }

    [Fact]
    public void DateRange_ShouldCheckOverlaps()
    {
        // Arrange
        var range1 = new DateRange(
            Instant.FromUnixSeconds(1000),
            Instant.FromUnixSeconds(2000)
        );
        var range2 = new DateRange(
            Instant.FromUnixSeconds(1500),
            Instant.FromUnixSeconds(2500)
        );
        var range3 = new DateRange(
            Instant.FromUnixSeconds(3000),
            Instant.FromUnixSeconds(4000)
        );

        // Act & Assert
        Assert.True(range1.Overlaps(range2));
        Assert.False(range1.Overlaps(range3));
    }

    [Fact]
    public void DateRange_ShouldComputeIntersection()
    {
        // Arrange
        var range1 = new DateRange(
            Instant.FromUnixSeconds(1000),
            Instant.FromUnixSeconds(2000)
        );
        var range2 = new DateRange(
            Instant.FromUnixSeconds(1500),
            Instant.FromUnixSeconds(2500)
        );

        // Act
        var intersection = range1.Intersect(range2);

        // Assert
        Assert.NotNull(intersection);
        Assert.Equal(Instant.FromUnixSeconds(1500), intersection.Value.Start);
        Assert.Equal(Instant.FromUnixSeconds(2000), intersection.Value.End);
    }

    [Fact]
    public void DateRange_ShouldReturnNullForNoIntersection()
    {
        // Arrange
        var range1 = new DateRange(
            Instant.FromUnixSeconds(1000),
            Instant.FromUnixSeconds(2000)
        );
        var range2 = new DateRange(
            Instant.FromUnixSeconds(3000),
            Instant.FromUnixSeconds(4000)
        );

        // Act
        var intersection = range1.Intersect(range2);

        // Assert
        Assert.Null(intersection);
    }

    [Fact]
    public void DateRange_ShouldProvideFactoryMethods()
    {
        // Act
        var lastDays = DateRange.LastDays(7);
        var lastHours = DateRange.LastHours(24);

        // Assert
        Assert.True(lastDays.Duration >= Duration.FromDays(7));
        Assert.True(lastHours.Duration >= Duration.FromHours(24));
    }
}
