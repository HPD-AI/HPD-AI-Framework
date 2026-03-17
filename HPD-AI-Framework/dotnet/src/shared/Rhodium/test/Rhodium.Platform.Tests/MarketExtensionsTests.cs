using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;

namespace Rhodium.Platform.Tests;

/// <summary>
/// Tests for MarketExtensions L1/L2 market data accessors.
/// </summary>
public class MarketExtensionsTests
{
    private TradingEngine CreateEngineWithDepth()
    {
        var engine = new TradingEngine();

        // Add a test instrument
        var instrument = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);

        return engine;
    }

    [Fact]
    public void TryGetBid_WithValidBid_ReturnsTrue()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        // Create mock depth with bid
        var depth = new MockHftDepth { BestBidTick = 1000 };
        engine.SetDepth(0, depth);

        bool success = engine.TryGetBid(id, out long ticks);

        Assert.True(success);
        Assert.Equal(1000, ticks);
    }

    [Fact]
    public void TryGetBid_WithNoBid_ReturnsFalse()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        // No depth set
        bool success = engine.TryGetBid(id, out long ticks);

        Assert.False(success);
        Assert.Equal(0, ticks);
    }

    [Fact]
    public void TryGetAsk_WithValidAsk_ReturnsTrue()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth { BestAskTick = 1005 };
        engine.SetDepth(0, depth);

        bool success = engine.TryGetAsk(id, out long ticks);

        Assert.True(success);
        Assert.Equal(1005, ticks);
    }

    [Fact]
    public void TryGetAsk_WithNoAsk_ReturnsFalse()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        bool success = engine.TryGetAsk(id, out long ticks);

        Assert.False(success);
        Assert.Equal(0, ticks);
    }

    [Fact]
    public void GetBidDepth_WithValidBid_ReturnsQuantity()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth
        {
            BestBidTick = 1000,
            BidQuantities = new() { [1000] = 500m }
        };
        engine.SetDepth(0, depth);

        var qty = engine.GetBidDepth(id);

        Assert.Equal(500m, qty);
    }

    [Fact]
    public void GetBidDepth_WithNoBid_ReturnsZero()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var qty = engine.GetBidDepth(id);

        Assert.Equal(0m, qty);
    }

    [Fact]
    public void GetAskDepth_WithValidAsk_ReturnsQuantity()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth
        {
            BestAskTick = 1005,
            AskQuantities = new() { [1005] = 750m }
        };
        engine.SetDepth(0, depth);

        var qty = engine.GetAskDepth(id);

        Assert.Equal(750m, qty);
    }

    [Fact]
    public void GetAskDepth_WithNoAsk_ReturnsZero()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var qty = engine.GetAskDepth(id);

        Assert.Equal(0m, qty);
    }

    [Fact]
    public void GetSpreadTicks_WithValidQuote_ReturnsSpread()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth
        {
            BestBidTick = 1000,
            BestAskTick = 1005
        };
        engine.SetDepth(0, depth);

        var spread = engine.GetSpreadTicks(id);

        Assert.Equal(5, spread);
    }

    [Fact]
    public void GetSpreadTicks_WithNoBid_ReturnsZero()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth { BestAskTick = 1005 };
        engine.SetDepth(0, depth);

        var spread = engine.GetSpreadTicks(id);

        Assert.Equal(0, spread);
    }

    [Fact]
    public void GetSpreadTicks_WithNoAsk_ReturnsZero()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth { BestBidTick = 1000 };
        engine.SetDepth(0, depth);

        var spread = engine.GetSpreadTicks(id);

        Assert.Equal(0, spread);
    }

    [Fact]
    public void GetMidTick_WithValidQuote_ReturnsMidPrice()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth
        {
            BestBidTick = 1000,
            BestAskTick = 1010
        };
        engine.SetDepth(0, depth);

        var mid = engine.GetMidTick(id);

        Assert.NotNull(mid);
        Assert.Equal(1005, mid.Value);
    }

    [Fact]
    public void GetMidTick_WithNoBid_ReturnsNull()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth { BestAskTick = 1005 };
        engine.SetDepth(0, depth);

        var mid = engine.GetMidTick(id);

        Assert.Null(mid);
    }

    [Fact]
    public void GetMidTick_WithNoAsk_ReturnsNull()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth { BestBidTick = 1000 };
        engine.SetDepth(0, depth);

        var mid = engine.GetMidTick(id);

        Assert.Null(mid);
    }

    [Fact]
    public void GetMidTick_WithOddSpread_RoundsDown()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth
        {
            BestBidTick = 1000,
            BestAskTick = 1005
        };
        engine.SetDepth(0, depth);

        var mid = engine.GetMidTick(id);

        Assert.Equal(1002, mid.Value); // (1000 + 1005) / 2 = 1002 (integer division)
    }

    [Fact]
    public void MarketExtensions_MultipleAssets_IndependentDepth()
    {
        var engine = CreateEngineWithDepth();

        // Add more instruments
        var inst2 = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(inst2, 1);

        var id0 = new AssetId(0);
        var id1 = new AssetId(1);

        var depth0 = new MockHftDepth
        {
            BestBidTick = 1000,
            BestAskTick = 1005
        };
        var depth1 = new MockHftDepth
        {
            BestBidTick = 2000,
            BestAskTick = 2010
        };

        engine.SetDepth(0, depth0);
        engine.SetDepth(1, depth1);

        Assert.True(engine.TryGetBid(id0, out long bid0));
        Assert.Equal(1000, bid0);

        Assert.True(engine.TryGetBid(id1, out long bid1));
        Assert.Equal(2000, bid1);
    }

    [Fact]
    public void GetBidDepth_LargeQuantity_ReturnsCorrectValue()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        var depth = new MockHftDepth
        {
            BestBidTick = 1000,
            BidQuantities = new() { [1000] = 1_000_000m }
        };
        engine.SetDepth(0, depth);

        var qty = engine.GetBidDepth(id);

        Assert.Equal(1_000_000m, qty);
    }

    [Fact]
    public void GetSpreadTicks_LockedMarket_ReturnsZero()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        // Locked market (bid == ask)
        var depth = new MockHftDepth
        {
            BestBidTick = 1000,
            BestAskTick = 1000
        };
        engine.SetDepth(0, depth);

        var spread = engine.GetSpreadTicks(id);

        Assert.Equal(0, spread);
    }

    [Fact]
    public void GetSpreadTicks_CrossedMarket_ReturnsNegative()
    {
        var engine = CreateEngineWithDepth();
        var id = new AssetId(0);

        // Crossed market (bid > ask)
        var depth = new MockHftDepth
        {
            BestBidTick = 1005,
            BestAskTick = 1000
        };
        engine.SetDepth(0, depth);

        var spread = engine.GetSpreadTicks(id);

        Assert.Equal(-5, spread);
    }
}

/// <summary>
/// Mock implementation of IHftDepth for testing.
/// </summary>
internal class MockHftDepth : IHftDepth
{
    public decimal TickSize { get; set; } = 0.01m;
    public decimal LotSize { get; set; } = 1m;
    public long? BestBidTick { get; set; }
    public long? BestAskTick { get; set; }
    public Dictionary<long, decimal> BidQuantities { get; set; } = new();
    public Dictionary<long, decimal> AskQuantities { get; set; } = new();

    public decimal QtyAtTick(Side side, long tick)
    {
        var dict = side == Side.Buy ? BidQuantities : AskQuantities;
        return dict.TryGetValue(tick, out var qty) ? qty : 0m;
    }

    public void Update(Side side, long priceTick, decimal qty, Instant timestamp)
    {
        var dict = side == Side.Buy ? BidQuantities : AskQuantities;
        if (qty > 0)
        {
            dict[priceTick] = qty;
        }
        else
        {
            dict.Remove(priceTick);
        }
    }

    public void Clear(Side side = Side.None)
    {
        if (side == Side.Buy || side == Side.None)
        {
            BidQuantities.Clear();
            BestBidTick = null;
        }
        if (side == Side.Sell || side == Side.None)
        {
            AskQuantities.Clear();
            BestAskTick = null;
        }
    }

    public int BidLevels => BidQuantities.Count;
    public int AskLevels => AskQuantities.Count;
}
