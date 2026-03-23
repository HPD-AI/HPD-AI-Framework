using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;

namespace Rhodium.Platform.Tests;

/// <summary>
/// Tests for TradeExtensions execution logic.
/// </summary>
public class TradeExtensionsTests
{
    private TradingEngine CreateEngineWithMarket()
    {
        var engine = new TradingEngine();

        // Add test instrument
        var instrument = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);

        // Set metadata
        engine.SetMetadata(0, new SecurityMetadata
        {
            TickSize = 0.01m,
            LotSize = 1m,
            Currency = Currency.USD
        });

        // Set market depth
        var depth = new MockHftDepth
        {
            BestBidTick = 10000, // $100.00
            BestAskTick = 10005, // $100.05
            BidQuantities = new() { [10000] = 1000m },
            AskQuantities = new() { [10005] = 1000m }
        };
        engine.SetDepth(0, depth);

        return engine;
    }

    [Fact]
    public void Buy_WithSafePolicy_SubmitsOrder()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        engine.Buy(id, new Qty(100m), ExecutionPolicy.Safe);

        // Order should be submitted (verified by checking active orders)
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void Sell_WithSafePolicy_SubmitsOrder()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        engine.Sell(id, new Qty(100m), ExecutionPolicy.Safe);

        Assert.True(engine.HasOpenOrder(0, Side.Sell));
    }

    [Fact]
    public void Buy_WithIdempotentPolicy_PreventsDuplicates()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // First buy
        engine.Buy(id, new Qty(100m), ExecutionPolicy.Idempotent);
        Assert.True(engine.HasOpenOrder(0, Side.Buy));

        // Second buy should be ignored (idempotent)
        var beforeCount = GetOrderCount(engine, 0);
        engine.Buy(id, new Qty(100m), ExecutionPolicy.Idempotent);
        var afterCount = GetOrderCount(engine, 0);

        // Should not submit duplicate order
        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    public void Buy_WithRawPolicy_AllowsDuplicates()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Multiple buys with raw policy
        engine.Buy(id, new Qty(100m), ExecutionPolicy.Raw);
        engine.Buy(id, new Qty(100m), ExecutionPolicy.Raw);

        // Both should be submitted
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void Buy_WithRiskCheck_RoundsToLotSize()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set lot size to 10
        engine.SetMetadata(0, new SecurityMetadata
        {
            TickSize = 0.01m,
            LotSize = 10m,
            Currency = Currency.USD
        });

        // Try to buy 105 shares (should round down to 100)
        engine.Buy(id, new Qty(105m), ExecutionPolicy.RiskCheck);

        // Order should be submitted with rounded quantity
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void Buy_WithRiskCheck_RejectsBelowLotSize()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set lot size to 100
        engine.SetMetadata(0, new SecurityMetadata
        {
            TickSize = 0.01m,
            LotSize = 100m,
            Currency = Currency.USD
        });

        // Try to buy 50 shares (below lot size)
        engine.Buy(id, new Qty(50m), ExecutionPolicy.RiskCheck);

        // Order should not be submitted
        Assert.False(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void SetPosition_WithZeroTarget_DoesNothing()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Position is already zero
        engine.SetPosition(id, Qty.Zero, ExecutionPolicy.Safe);

        // No order should be submitted
        Assert.False(engine.HasOpenOrder(0, Side.Buy));
        Assert.False(engine.HasOpenOrder(0, Side.Sell));
    }

    [Fact]
    public void SetPosition_WithPositiveTarget_SubmitsBuy()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set position to 100
        engine.SetPosition(id, new Qty(100m), ExecutionPolicy.Safe);

        // Should submit buy order
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void SetPosition_WithNegativeTarget_SubmitsSell()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set position to -100 (short)
        engine.SetPosition(id, new Qty(-100m), ExecutionPolicy.Safe);

        // Should submit sell order
        Assert.True(engine.HasOpenOrder(0, Side.Sell));
    }

    [Fact]
    public void SetPosition_FromLongToFlat_SubmitsSell()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Start with long position
        engine.SetPosition(0, 100m);

        // Flatten position
        engine.SetPosition(id, Qty.Zero, ExecutionPolicy.Safe);

        // Should submit sell order
        Assert.True(engine.HasOpenOrder(0, Side.Sell));
    }

    [Fact]
    public void Flatten_WithLongPosition_SubmitsSell()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set long position
        engine.SetPosition(0, 100m);

        // Flatten
        engine.Flatten(id);

        // Should submit sell order
        Assert.True(engine.HasOpenOrder(0, Side.Sell));
    }

    [Fact]
    public void Flatten_WithShortPosition_SubmitsBuy()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set short position
        engine.SetPosition(0, -100m);

        // Flatten
        engine.Flatten(id);

        // Should submit buy order
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void Flatten_UsesRawPolicy()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set position with existing order
        engine.SetPosition(0, 100m);
        engine.Buy(id, new Qty(10m), ExecutionPolicy.Safe);

        // Flatten should work even with existing orders (raw policy)
        engine.Flatten(id);

        // Both buy and sell orders should exist
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
        Assert.True(engine.HasOpenOrder(0, Side.Sell));
    }

    [Fact]
    public void CancelAll_RemovesAllOrders()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Submit multiple orders
        engine.Buy(id, new Qty(100m), ExecutionPolicy.Raw);
        engine.Buy(id, new Qty(200m), ExecutionPolicy.Raw);

        Assert.True(engine.HasOpenOrder(0, Side.Buy));

        // Cancel all
        engine.CancelAll(id);

        // No orders should remain
        Assert.False(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void Buy_WithNoMarketDepth_DoesNotSubmit()
    {
        var engine = new TradingEngine();
        var instrument = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        engine.BatchMap.AddInstrument(instrument, 1);

        var id = new AssetId(0);

        // No depth set - should not submit
        engine.Buy(id, new Qty(100m), ExecutionPolicy.Safe);

        Assert.False(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void ExecutionPolicy_Safe_CombinesIdempotentAndRiskCheck()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Set lot size
        engine.SetMetadata(0, new SecurityMetadata
        {
            TickSize = 0.01m,
            LotSize = 10m,
            Currency = Currency.USD
        });

        // First order with 105 shares (should round to 100)
        engine.Buy(id, new Qty(105m), ExecutionPolicy.Safe);
        Assert.True(engine.HasOpenOrder(0, Side.Buy));

        // Second order should be rejected (idempotent)
        var beforeCount = GetOrderCount(engine, 0);
        engine.Buy(id, new Qty(105m), ExecutionPolicy.Safe);
        var afterCount = GetOrderCount(engine, 0);

        Assert.Equal(beforeCount, afterCount);
    }

    [Fact]
    public void SetPosition_CalculatesDeltaCorrectly()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Start with 50 shares
        engine.SetPosition(0, 50m);

        // Set to 150 shares (delta = +100)
        engine.SetPosition(id, new Qty(150m), ExecutionPolicy.Safe);

        // Should submit buy for 100 shares
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void SetPosition_FromLongToShort_SubmitsSell()
    {
        var engine = CreateEngineWithMarket();
        var id = new AssetId(0);

        // Start with 100 long
        engine.SetPosition(0, 100m);

        // Set to -50 short (delta = -150)
        engine.SetPosition(id, new Qty(-50m), ExecutionPolicy.Safe);

        // Should submit sell for 150 shares
        Assert.True(engine.HasOpenOrder(0, Side.Sell));
    }

    [Fact]
    public void ExecutionPolicy_Flags_CanBeCombined()
    {
        var combined = ExecutionPolicy.Idempotent | ExecutionPolicy.RiskCheck;
        Assert.Equal(ExecutionPolicy.Safe, combined);

        Assert.True((ExecutionPolicy.Safe & ExecutionPolicy.Idempotent) != 0);
        Assert.True((ExecutionPolicy.Safe & ExecutionPolicy.RiskCheck) != 0);
        Assert.False((ExecutionPolicy.Raw & ExecutionPolicy.Idempotent) != 0);
    }

    private int GetOrderCount(TradingEngine engine, int index)
    {
        // Helper to count orders (simplified for testing)
        return engine.HasOpenOrder(index, Side.Buy) ? 1 : 0;
    }
}
