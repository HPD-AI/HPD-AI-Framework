using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class BarTests
{
    [Fact]
    public void Bar_ShouldStoreOHLCV()
    {
        // Arrange
        var open = new Price(100m);
        var high = new Price(105m);
        var low = new Price(99m);
        var close = new Price(103m);
        var volume = new Qty(10000m);
        var time = Instant.Now;
        var period = Duration.FromMinutes(5);

        // Act
        var bar = new Bar(open, high, low, close, volume, time, period);

        // Assert
        Assert.Equal(open, bar.Open);
        Assert.Equal(high, bar.High);
        Assert.Equal(low, bar.Low);
        Assert.Equal(close, bar.Close);
        Assert.Equal(volume, bar.Volume);
        Assert.Equal(time, bar.Time);
        Assert.Equal(period, bar.Period);
    }

    [Fact]
    public void Bar_Typical_ShouldCalculateCorrectly()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var typical = bar.Typical;

        // Assert - (105 + 99 + 103) / 3 = 102.333...
        Assert.Equal(102.333333333333333333333333333m, typical.Value);
    }

    [Fact]
    public void Bar_Median_ShouldCalculateCorrectly()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var median = bar.Median;

        // Assert - (105 + 99) / 2 = 102
        Assert.Equal(102m, median.Value);
    }

    [Fact]
    public void Bar_Range_ShouldCalculateCorrectly()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var range = bar.Range;

        // Assert - 105 - 99 = 6
        Assert.Equal(6m, range.Value);
    }

    [Fact]
    public void Bar_Body_ShouldCalculateCorrectly()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var body = bar.Body;

        // Assert - |103 - 100| = 3
        Assert.Equal(3m, body.Value);
    }

    [Fact]
    public void Bar_UpperWick_ShouldCalculateCorrectly()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var upperWick = bar.UpperWick;

        // Assert - 105 - max(100, 103) = 105 - 103 = 2
        Assert.Equal(2m, upperWick.Value);
    }

    [Fact]
    public void Bar_LowerWick_ShouldCalculateCorrectly()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var lowerWick = bar.LowerWick;

        // Assert - min(100, 103) - 99 = 100 - 99 = 1
        Assert.Equal(1m, lowerWick.Value);
    }

    [Fact]
    public void Bar_IsBullish_ShouldReturnTrueWhenCloseAboveOpen()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act & Assert
        Assert.True(bar.IsBullish);
        Assert.False(bar.IsBearish);
    }

    [Fact]
    public void Bar_IsBearish_ShouldReturnTrueWhenCloseBelowOpen()
    {
        // Arrange
        var bar = new Bar(
            new Price(103m),
            new Price(105m),
            new Price(99m),
            new Price(100m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act & Assert
        Assert.True(bar.IsBearish);
        Assert.False(bar.IsBullish);
    }

    [Fact]
    public void Bar_IsDoji_ShouldReturnTrueForSmallBody()
    {
        // Arrange - Body 0.5, Range 10 = 5% (less than 10%)
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(95m),
            new Price(100.5m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act & Assert
        Assert.True(bar.IsDoji);
    }

    [Fact]
    public void Bar_Change_ShouldCalculatePercentageChange()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var change = bar.Change;

        // Assert - (103 - 100) / 100 = 0.03
        Assert.Equal(0.03m, change);
    }

    [Fact]
    public void Bar_ChangeAbs_ShouldCalculateAbsoluteChange()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(10000m),
            Instant.Now,
            Duration.FromMinutes(1)
        );

        // Act
        var changeAbs = bar.ChangeAbs;

        // Assert - 103 - 100 = 3
        Assert.Equal(3m, changeAbs);
    }

    [Fact]
    public void Bar_Update_ShouldUpdateHighLowCloseVolume()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(5000m),
            Instant.FromUnixSeconds(1000),
            Duration.FromMinutes(1)
        );
        var newPrice = new Price(106m);
        var newVolume = new Qty(1000m);
        var newTime = Instant.FromUnixSeconds(1010);

        // Act
        var updated = bar.Update(newPrice, newVolume, newTime);

        // Assert
        Assert.Equal(new Price(100m), updated.Open); // Unchanged
        Assert.Equal(new Price(106m), updated.High); // Updated (higher than 105)
        Assert.Equal(new Price(99m), updated.Low); // Unchanged
        Assert.Equal(newPrice, updated.Close); // Updated
        Assert.Equal(new Qty(6000m), updated.Volume); // 5000 + 1000
        Assert.Equal(newTime, updated.Time); // Updated
    }

    [Fact]
    public void Bar_Update_ShouldUpdateLowIfLower()
    {
        // Arrange
        var bar = new Bar(
            new Price(100m),
            new Price(105m),
            new Price(99m),
            new Price(103m),
            new Qty(5000m),
            Instant.FromUnixSeconds(1000),
            Duration.FromMinutes(1)
        );
        var newPrice = new Price(98m);
        var newVolume = new Qty(1000m);
        var newTime = Instant.FromUnixSeconds(1010);

        // Act
        var updated = bar.Update(newPrice, newVolume, newTime);

        // Assert
        Assert.Equal(new Price(98m), updated.Low); // Updated (lower than 99)
        Assert.Equal(newPrice, updated.Close);
    }

    [Fact]
    public void Bar_Create_ShouldCreateBarWithSameOHLC()
    {
        // Arrange
        var price = new Price(100m);
        var volume = new Qty(1000m);
        var time = Instant.Now;
        var period = Duration.FromMinutes(5);

        // Act
        var bar = Bar.Create(price, volume, time, period);

        // Assert
        Assert.Equal(price, bar.Open);
        Assert.Equal(price, bar.High);
        Assert.Equal(price, bar.Low);
        Assert.Equal(price, bar.Close);
        Assert.Equal(volume, bar.Volume);
        Assert.Equal(time, bar.Time);
        Assert.Equal(period, bar.Period);
    }
}
