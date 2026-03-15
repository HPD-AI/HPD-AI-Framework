using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests;

public class SubscriptionTests
{
    private static Instrument CreateInstrument(string symbol = "AAPL") =>
        new(new Asset(symbol, AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void Constructor_SetsProperties()
    {
        var instrument = CreateInstrument();
        var subscription = new Subscription(instrument, SubscriptionType.Trades, SubscriptionDepth.L2_10);

        Assert.Equal(instrument, subscription.Instrument);
        Assert.Equal(SubscriptionType.Trades, subscription.Type);
        Assert.Equal(SubscriptionDepth.L2_10, subscription.Depth);
    }

    [Fact]
    public void Constructor_DefaultsToTopOfBook()
    {
        var instrument = CreateInstrument();
        var subscription = new Subscription(instrument, SubscriptionType.Quotes);

        Assert.Equal(SubscriptionDepth.Top, subscription.Depth);
    }

    [Fact]
    public void Equality_WorksCorrectly()
    {
        var instrument = CreateInstrument();
        var sub1 = new Subscription(instrument, SubscriptionType.Trades, SubscriptionDepth.L2_5);
        var sub2 = new Subscription(instrument, SubscriptionType.Trades, SubscriptionDepth.L2_5);
        var sub3 = new Subscription(instrument, SubscriptionType.Quotes, SubscriptionDepth.L2_5);

        Assert.Equal(sub1, sub2);
        Assert.NotEqual(sub1, sub3);
    }
}

public class SubscriptionTypeTests
{
    [Fact]
    public void Trades_HasCorrectValue()
    {
        Assert.Equal(0, (int)SubscriptionType.Trades);
    }

    [Fact]
    public void Quotes_HasCorrectValue()
    {
        Assert.Equal(1, (int)SubscriptionType.Quotes);
    }

    [Fact]
    public void Depth_HasCorrectValue()
    {
        Assert.Equal(2, (int)SubscriptionType.Depth);
    }

    [Fact]
    public void Bars_HasCorrectValue()
    {
        Assert.Equal(3, (int)SubscriptionType.Bars);
    }
}

public class SubscriptionDepthTests
{
    [Fact]
    public void Top_HasCorrectValue()
    {
        Assert.Equal(1, (int)SubscriptionDepth.Top);
    }

    [Fact]
    public void L2_5_HasCorrectValue()
    {
        Assert.Equal(5, (int)SubscriptionDepth.L2_5);
    }

    [Fact]
    public void L2_10_HasCorrectValue()
    {
        Assert.Equal(10, (int)SubscriptionDepth.L2_10);
    }

    [Fact]
    public void L2_20_HasCorrectValue()
    {
        Assert.Equal(20, (int)SubscriptionDepth.L2_20);
    }

    [Fact]
    public void Full_HasCorrectValue()
    {
        Assert.Equal(255, (int)SubscriptionDepth.Full);
    }
}
