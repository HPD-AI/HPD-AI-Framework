using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class BarTypeTests
{
    private static readonly Instrument BTC = new(new Asset("BTC", AssetClass.Crypto), Venue.Coinbase);
    private static readonly Instrument ETH = new(new Asset("ETH", AssetClass.Crypto), Venue.Binance);

    [Fact]
    public void BarType_Constructor()
    {
        var barType = new BarType(BTC, BarPeriod.M5);

        Assert.Equal(BTC, barType.Instrument);
        Assert.Equal(BarPeriod.M5, barType.Period);
    }

    [Fact]
    public void BarType_CreateFactory()
    {
        var barType = BarType.Create(BTC, BarPeriod.H1);

        Assert.Equal(BTC, barType.Instrument);
        Assert.Equal(BarPeriod.H1, barType.Period);
    }

    [Fact]
    public void BarType_M1Factory()
    {
        var barType = BarType.M1(BTC);

        Assert.Equal(BTC, barType.Instrument);
        Assert.Equal(BarPeriod.M1, barType.Period);
    }

    [Fact]
    public void BarType_M5Factory()
    {
        var barType = BarType.M5(BTC);

        Assert.Equal(BTC, barType.Instrument);
        Assert.Equal(BarPeriod.M5, barType.Period);
    }

    [Fact]
    public void BarType_H1Factory()
    {
        var barType = BarType.H1(BTC);

        Assert.Equal(BTC, barType.Instrument);
        Assert.Equal(BarPeriod.H1, barType.Period);
    }

    [Fact]
    public void BarType_D1Factory()
    {
        var barType = BarType.D1(BTC);

        Assert.Equal(BTC, barType.Instrument);
        Assert.Equal(BarPeriod.D1, barType.Period);
    }

    [Fact]
    public void BarType_ToString()
    {
        var barType = BarType.M5(BTC);
        var str = barType.ToString();

        Assert.Contains("BTC", str);
        Assert.Contains("5m", str);
        Assert.Contains(":", str);
    }

    [Fact]
    public void BarType_Equality()
    {
        var bt1 = BarType.M5(BTC);
        var bt2 = new BarType(BTC, BarPeriod.Minutes(5));

        Assert.Equal(bt1, bt2);
    }

    [Fact]
    public void BarType_InequalityDifferentInstrument()
    {
        var bt1 = BarType.M5(BTC);
        var bt2 = BarType.M5(ETH);

        Assert.NotEqual(bt1, bt2);
    }

    [Fact]
    public void BarType_InequalityDifferentPeriod()
    {
        var bt1 = BarType.M5(BTC);
        var bt2 = BarType.H1(BTC);

        Assert.NotEqual(bt1, bt2);
    }

    [Fact]
    public void BarType_CanBeUsedAsDictionaryKey()
    {
        var dict = new Dictionary<BarType, int>
        {
            [BarType.M1(BTC)] = 1,
            [BarType.M5(BTC)] = 5,
            [BarType.H1(ETH)] = 60
        };

        Assert.Equal(1, dict[BarType.M1(BTC)]);
        Assert.Equal(5, dict[BarType.M5(BTC)]);
        Assert.Equal(60, dict[BarType.H1(ETH)]);
    }

    [Fact]
    public void BarType_Deconstruction()
    {
        var barType = BarType.M5(BTC);
        var (instrument, period) = barType;

        Assert.Equal(BTC, instrument);
        Assert.Equal(BarPeriod.M5, period);
    }
}
