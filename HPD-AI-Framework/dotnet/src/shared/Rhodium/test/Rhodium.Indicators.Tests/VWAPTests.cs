using Rhodium.Primitives;
using Rhodium.Indicators;
using Xunit;

namespace Rhodium.Indicators.Tests;

/// <summary>
/// Tests for Volume Weighted Average Price (VWAP) indicator.
/// </summary>
public class VWAPTests
{
    [Fact]
    public void VWAP_CalculatesCorrectValue_WithSimpleBars()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Bar 1: typical = (100+110+90+100)/3 = 100, volume = 1000
        // Bar 2: typical = (100+120+95+110)/3 = 108.33, volume = 2000
        var bar1 = TestHelpers.CreateBar(100m, 110m, 90m, 100m, 1000m);
        var bar2 = TestHelpers.CreateBar(100m, 120m, 95m, 110m, 2000m);

        // Act
        vwap.Update(bar1);
        vwap.Update(bar2);

        // Assert
        // VWAP = (100*1000 + 108.33*2000) / (1000+2000)
        // VWAP = (100000 + 216666.67) / 3000 = 105.56
        TestHelpers.AssertApproximately(105.56m, vwap.Value, 0.01m);
    }

    [Fact]
    public void VWAP_IsReadyAfterFirstBar()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Act & Assert
        TestHelpers.AssertNotReady(vwap);

        vwap.Update(TestHelpers.CreateBar(100m, 1000m));
        TestHelpers.AssertReady(vwap);
    }

    [Fact]
    public void VWAP_ResetsCorrectly()
    {
        // Arrange
        var vwap = Indicators.VWAP();
        TestHelpers.UpdateBars(vwap, TestHelpers.CreateBars(100m, 110m, 120m));

        // Act
        vwap.Reset();

        // Assert
        TestHelpers.AssertNotReady(vwap);
        TestHelpers.AssertCount(0, vwap);
        Assert.Equal(0m, vwap.Value);
    }

    [Fact]
    public void VWAP_RespondsToVolumeChanges()
    {
        // Arrange
        var vwap1 = Indicators.VWAP();
        var vwap2 = Indicators.VWAP();

        // Same prices, different volumes
        var bar1 = TestHelpers.CreateBar(100m, 1000m);
        var bar2 = TestHelpers.CreateBar(110m, 1000m); // Equal volume

        var bar3 = TestHelpers.CreateBar(100m, 1000m);
        var bar4 = TestHelpers.CreateBar(110m, 10000m); // Much higher volume

        // Act
        vwap1.Update(bar1);
        vwap1.Update(bar2);

        vwap2.Update(bar3);
        vwap2.Update(bar4);

        // Assert - Higher volume on second bar should pull VWAP closer to 110
        Assert.True(vwap2.Value > vwap1.Value,
            $"VWAP with higher volume on second bar ({vwap2.Value}) should be greater than equal volume ({vwap1.Value})");
    }

    [Fact]
    public void VWAP_AccumulatesCorrectly()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Act - All bars at same typical price
        for (int i = 0; i < 10; i++)
        {
            vwap.Update(TestHelpers.CreateBar(100m, 1000m));
        }

        // Assert - VWAP should equal the constant price
        TestHelpers.AssertApproximately(100m, vwap.Value, 0.01m);
    }

    [Fact]
    public void VWAP_HandlesZeroVolume()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Act - Bar with zero volume
        vwap.Update(TestHelpers.CreateBar(100m, 0m));
        vwap.Update(TestHelpers.CreateBar(110m, 1000m));

        // Assert - Should handle gracefully
        TestHelpers.AssertReady(vwap);
        TestHelpers.AssertApproximately(110m, vwap.Value, 0.01m);
    }

    [Fact]
    public void VWAP_CalculatesWithBullishBars()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Act
        vwap.Update(TestHelpers.CreateBullishBar(100m, 110m, volume: 1000m));
        vwap.Update(TestHelpers.CreateBullishBar(110m, 120m, volume: 2000m));

        // Assert
        TestHelpers.AssertReady(vwap);
        Assert.True(vwap.Value > 0m);
    }

    [Fact]
    public void VWAP_CalculatesWithBearishBars()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Act
        vwap.Update(TestHelpers.CreateBearishBar(110m, 100m, volume: 1000m));
        vwap.Update(TestHelpers.CreateBearishBar(120m, 110m, volume: 2000m));

        // Assert
        TestHelpers.AssertReady(vwap);
        Assert.True(vwap.Value > 0m);
    }

    [Fact]
    public void VWAP_CountIncrementsCorrectly()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Act & Assert
        Assert.Equal(0, vwap.Count);

        vwap.Update(TestHelpers.CreateBar(100m));
        Assert.Equal(1, vwap.Count);

        vwap.Update(TestHelpers.CreateBar(110m));
        Assert.Equal(2, vwap.Count);

        vwap.Update(TestHelpers.CreateBar(120m));
        Assert.Equal(3, vwap.Count);
    }

    [Fact]
    public void VWAP_ManualCalculationVerification()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Manual calculation using Typical Price = (H + L + C) / 3:
        // Bar 1: open=100, high=105, low=95, close=102, volume=1000
        // typical1 = (105 + 95 + 102) / 3 = 302 / 3 = 100.666...
        // Bar 2: open=102, high=108, low=100, close=106, volume=1500
        // typical2 = (108 + 100 + 106) / 3 = 314 / 3 = 104.666...
        // Bar 3: open=106, high=112, low=104, close=110, volume=2000
        // typical3 = (112 + 104 + 110) / 3 = 326 / 3 = 108.666...

        var bar1 = TestHelpers.CreateBar(100m, 105m, 95m, 102m, 1000m);
        var bar2 = TestHelpers.CreateBar(102m, 108m, 100m, 106m, 1500m);
        var bar3 = TestHelpers.CreateBar(106m, 112m, 104m, 110m, 2000m);

        // Act
        vwap.Update(bar1);
        var vwap1 = vwap.Value;

        vwap.Update(bar2);
        var vwap2 = vwap.Value;

        vwap.Update(bar3);
        var vwap3 = vwap.Value;

        // Assert
        // After bar1: VWAP = (100.666... * 1000) / 1000 = 100.666...
        var typical1 = (105m + 95m + 102m) / 3m;
        TestHelpers.AssertApproximately(typical1, vwap1, 0.01m);

        // After bar2: VWAP = (100.666*1000 + 104.666*1500) / 2500
        var typical2 = (108m + 100m + 106m) / 3m;
        var expectedVwap2 = (typical1 * 1000m + typical2 * 1500m) / 2500m;
        TestHelpers.AssertApproximately(expectedVwap2, vwap2, 0.01m);

        // After bar3: VWAP = (100.666*1000 + 104.666*1500 + 108.666*2000) / 4500
        var typical3 = (112m + 104m + 110m) / 3m;
        var expectedVwap3 = (typical1 * 1000m + typical2 * 1500m + typical3 * 2000m) / 4500m;
        TestHelpers.AssertApproximately(expectedVwap3, vwap3, 0.01m);
    }

    [Fact]
    public void VWAP_WithHighVolumeSpike()
    {
        // Arrange
        var vwap = Indicators.VWAP();

        // Act - Regular volume then huge spike
        vwap.Update(TestHelpers.CreateBar(100m, 1000m));
        vwap.Update(TestHelpers.CreateBar(110m, 1000m));
        vwap.Update(TestHelpers.CreateBar(120m, 100000m)); // Volume spike

        // Assert - VWAP should be pulled strongly toward the high volume bar
        TestHelpers.AssertApproximately(120m, vwap.Value, 1m);
    }
}
