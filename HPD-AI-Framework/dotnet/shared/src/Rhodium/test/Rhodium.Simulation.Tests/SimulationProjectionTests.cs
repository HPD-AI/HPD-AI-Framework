using Rhodium.Events;
using Rhodium.Control;
using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Simulation.Projection;
using Rhodium.Tensor;

namespace Rhodium.Simulation.Tests;

public sealed class SimulationProjectionTests
{
    [Fact]
    public void MarketProjector_BarClosed_WritesMarketFields()
    {
        using var runtime = CreateRuntime(variantCount: 1, out var inst);
        var projector = new SimulationMarketProjector();
        var bar = new Bar(
            new Price(100m),
            new Price(101m),
            new Price(99m),
            new Price(100.5m),
            new Qty(1000m),
            Instant.Now,
            Duration.FromMinutes(1));

        var transition = projector.Apply(new BarClosed(inst, bar), runtime);

        Assert.True(transition.RequiresAdjustment);
        Assert.Equal(100.5, runtime.Tensors.GetScalar(Field.CloseRaw, 0).Value);
        Assert.Equal(1000, runtime.Tensors.GetScalar(Field.VolumeRaw, 0).Value);
    }

    [Fact]
    public void MarketProjector_BarClosed_ReturnsAdjustmentTransition()
    {
        using var runtime = CreateRuntime(variantCount: 1, out var inst);
        var projector = new SimulationMarketProjector();
        var bar = new Bar(
            new Price(100m),
            new Price(101m),
            new Price(99m),
            new Price(100.5m),
            new Qty(1000m),
            Instant.Now,
            Duration.FromMinutes(1));

        var transition = projector.Apply(new BarClosed(inst, bar), runtime);

        Assert.True(transition.RequiresAdjustment);
        Assert.Equal(100m, (decimal)runtime.Tensors.GetScalar(Field.OpenRaw, 0).Value);
        Assert.Equal(101m, (decimal)runtime.Tensors.GetScalar(Field.HighRaw, 0).Value);
        Assert.Equal(99m, (decimal)runtime.Tensors.GetScalar(Field.LowRaw, 0).Value);
        Assert.Equal(100.5m, (decimal)runtime.Tensors.GetScalar(Field.CloseRaw, 0).Value);
        Assert.Equal(1000m, (decimal)runtime.Tensors.GetScalar(Field.VolumeRaw, 0).Value);
    }

    [Fact]
    public void MarketProjector_TradeOccurred_WritesLastTradeFields()
    {
        using var runtime = CreateRuntime(variantCount: 2, out var inst);
        var projector = new SimulationMarketProjector();
        var trade = new Trade(
            new Price(100.25m, Currency.USD),
            new Qty(37m),
            Side.Buy,
            DualTimestamp.Synchronized(Instant.Now));

        var transition = projector.Apply(new TradeOccurred(inst, trade), runtime);

        Assert.True(transition.RequiresAdjustment);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(100.25m, (decimal)runtime.Tensors.GetScalar(Field.CloseRaw, i).Value);
            Assert.Equal(37m, (decimal)runtime.Tensors.GetScalar(Field.VolumeRaw, i).Value);
        }
    }

    [Fact]
    public void MarketProjector_BookSnapshotReceived_WritesTopOfBookFieldsAndDepth()
    {
        using var runtime = CreateRuntime(variantCount: 2, out var inst);
        var projector = new SimulationMarketProjector();
        var book = new Book
        {
            Instrument = inst,
            Time = Instant.Now,
            Bids =
            [
                new Level(new Price(100m, Currency.USD), new Qty(20m)),
                new Level(new Price(99.99m, Currency.USD), new Qty(30m))
            ],
            Asks =
            [
                new Level(new Price(100.01m, Currency.USD), new Qty(10m)),
                new Level(new Price(100.02m, Currency.USD), new Qty(40m))
            ]
        };

        var transition = projector.Apply(new BookSnapshotReceived(inst, book), runtime);
        var market = runtime.CreateMarketKernel();

        Assert.True(transition.RequiresAdjustment);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(100m, (decimal)runtime.Tensors.GetScalar(Field.BidRaw, i).Value);
            Assert.Equal(100.01m, (decimal)runtime.Tensors.GetScalar(Field.AskRaw, i).Value);
            Assert.Equal(20m, (decimal)runtime.Tensors.GetScalar(Field.BidSizeRaw, i).Value);
            Assert.Equal(10m, (decimal)runtime.Tensors.GetScalar(Field.AskSizeRaw, i).Value);
            var priceIncrement = market.GetPriceIncrement(new AssetId(i));
            Assert.Equal(100m, market.GetBestBidTick(new AssetId(i)) * priceIncrement);
            Assert.Equal(100.01m, market.GetBestAskTick(new AssetId(i)) * priceIncrement);
        }
    }

    [Fact]
    public void MarketProjector_BookOrderEvents_ProjectIntoL3TensorState()
    {
        using var runtime = CreateRuntime(variantCount: 1, out var inst);
        var projector = new SimulationMarketProjector();
        var order = new BookOrder(
            new BookOrderId(101),
            Side.Buy,
            new Price(100m, Currency.USD),
            new Qty(10m));

        projector.Apply(new BookOrderAdded(inst, order) { Time = Instant.FromUnixSeconds(1) }, runtime);

        var vi = runtime.L3Basis.GetVI(inst.ToString(), priceLevel: 0, orderSlot: 0);
        Assert.Equal(101.0, runtime.MarketState.GetScalar(MarketField.OrderId, vi).Value);
        Assert.Equal(10.0, runtime.MarketState.GetScalar(MarketField.OrderQty, vi).Value);
        Assert.Equal(1_000_000_000.0, runtime.MarketState.GetScalar(MarketField.OrderTimestamp, vi).Value);
        Assert.Equal(10.0, runtime.MarketState.GetScalar(MarketField.TotalQtyAtLevel, vi).Value);
        Assert.Equal(1.0, runtime.MarketState.GetScalar(MarketField.OrderCount, vi).Value);

        projector.Apply(new BookOrderExecuted(inst, new BookOrderId(101), new Qty(3m)), runtime);
        Assert.Equal(7.0, runtime.MarketState.GetScalar(MarketField.OrderQty, vi).Value);
        Assert.Equal(7.0, runtime.MarketState.GetScalar(MarketField.TotalQtyAtLevel, vi).Value);

        projector.Apply(new BookOrderDeleted(inst, new BookOrderId(101)), runtime);
        Assert.Equal(0.0, runtime.MarketState.GetScalar(MarketField.OrderId, vi).Value);
        Assert.Equal(0.0, runtime.MarketState.GetScalar(MarketField.OrderQty, vi).Value);
        Assert.Equal(0.0, runtime.MarketState.GetScalar(MarketField.TotalQtyAtLevel, vi).Value);
        Assert.Equal(0.0, runtime.MarketState.GetScalar(MarketField.OrderCount, vi).Value);
    }

    [Fact]
    public void PortfolioProjector_OrderFilled_RoutesToSubmittingStrategy()
    {
        using var runtime = CreateRuntime(variantCount: 1, out var inst);
        var projector = new SimulationPortfolioProjector();
        var strategyA = new StrategyId(1);
        var strategyB = new StrategyId(2);
        var fill = new OrderFilled(
            OrderId.New(),
            inst,
            0,
            strategyA,
            Side.Buy,
            new Qty(50m),
            new Price(100m),
            Money.Zero(Currency.USD));

        var transition = projector.Apply(fill, runtime);

        Assert.Equal(PositionTransitionKind.Opened, transition.PositionTransition.Kind);
        Assert.Equal(50m, runtime.WorldState.PositionAt(strategyA, 0).Quantity);
        Assert.Equal(0m, runtime.WorldState.PositionAt(strategyB, 0).Quantity);
    }

    [Fact]
    public void PortfolioProjector_OrderFilled_UsesExplicitAssetIdBeforeVariantFallback()
    {
        using var runtime = CreateRuntime(variantCount: 2, out var inst);
        var projector = new SimulationPortfolioProjector();
        var strategy = new StrategyId(1);
        var fill = new OrderFilled(
            OrderId.New(),
            inst,
            VariantId: 0,
            strategy,
            Side.Buy,
            new Qty(50m),
            new Price(100m),
            Money.Zero(Currency.USD),
            AssetId: new AssetId(1));

        var transition = projector.Apply(fill, runtime);

        Assert.Equal(new AssetId(1), transition.PositionTransition.AssetId);
        Assert.Equal(0m, runtime.WorldState.PositionAt(strategy, 0).Quantity);
        Assert.Equal(50m, runtime.WorldState.PositionAt(strategy, 1).Quantity);
    }

    [Fact]
    public void PortfolioProjector_PhysicalOptionDelivery_UsesSettlementPriceForDeliverableBasis()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new StrategyId(1);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American);
        var optionTerms = Assert.IsType<PayoffTerms.Option>(option.Payoff).Terms;
        option = option with
        {
            Settlement = new SettlementTerms.Physical(Currency.USD, underlying, SettlementDelay.Immediate()),
            Payoff = new PayoffTerms.Option(optionTerms.With(settlementStyle: OptionSettlementStyle.Physical))
        };
        AddContract(runtime, option);
        AddContract(runtime, Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD));
        var projector = new SimulationPortfolioProjector();
        var optionFill = new OrderFilled(
            OrderId.New(),
            option.Instrument,
            VariantId: 0,
            strategy,
            Side.Buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.Zero(Currency.USD));
        projector.Apply(optionFill, runtime);

        var lifecycle = new OptionLifecycleApplied(
            strategy,
            VariantId: 0,
            option.Instrument,
            OptionLifecycleKind.PhysicalDelivery,
            new Qty(1m),
            Money.USD(-10_000m),
            expiry,
            UnderlyingMark: new Price(105m, Currency.USD),
            Deliverable: underlying,
            DeliverableQuantity: new Qty(100m),
            SettlementPrice: new Price(100m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark);

        projector.Apply(lifecycle, runtime);

        var underlyingAssetId = new AssetId(runtime.BatchMap.GetInstrumentRange(underlying).Start);
        var position = runtime.WorldState.PositionAt(strategy, underlyingAssetId.VirtualIndex);
        Assert.Equal(100m, position.Quantity);
        Assert.Equal(100m, position.AvgEntryPrice);
    }

    [Fact]
    public void PortfolioProjector_PhysicalDeliveryUsesSettlementPriceForDeliverableBasis()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new StrategyId(1);
        var underlying = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPY261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.01m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.American);
        var optionTerms = Assert.IsType<PayoffTerms.Option>(option.Payoff).Terms;
        option = option with
        {
            Settlement = new SettlementTerms.Physical(Currency.USD, underlying, SettlementDelay.Immediate()),
            Payoff = new PayoffTerms.Option(optionTerms.With(settlementStyle: OptionSettlementStyle.Physical))
        };
        AddContract(runtime, option);
        AddContract(runtime, Contracts.Equity("SPY", Venue.NASDAQ, Currency.USD));
        var projector = new SimulationPortfolioProjector();
        projector.Apply(new OrderFilled(
            OrderId.New(),
            option.Instrument,
            VariantId: 0,
            strategy,
            Side.Buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.Zero(Currency.USD)), runtime);

        var lifecycle = new OptionLifecycleApplied(
            strategy,
            VariantId: 0,
            option.Instrument,
            OptionLifecycleKind.PhysicalDelivery,
            new Qty(1m),
            Money.USD(-10_000m),
            expiry,
            UnderlyingMark: new Price(105m, Currency.USD),
            Deliverable: underlying,
            DeliverableQuantity: new Qty(100m),
            SettlementPrice: new Price(100m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark);

        projector.Apply(lifecycle, runtime);

        var underlyingAssetId = new AssetId(runtime.BatchMap.GetInstrumentRange(underlying).Start);
        var position = runtime.WorldState.PositionAt(strategy, underlyingAssetId.VirtualIndex);
        Assert.Equal(100m, position.AvgEntryPrice);
        Assert.NotEqual(105m, position.AvgEntryPrice);
    }

    [Fact]
    public void PortfolioProjector_ZeroCashOptionSettlement_StillClosesOptionPosition()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new StrategyId(1);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        AddContract(runtime, option);
        var projector = new SimulationPortfolioProjector();
        var optionFill = new OrderFilled(
            OrderId.New(),
            option.Instrument,
            VariantId: 0,
            strategy,
            Side.Buy,
            new Qty(1m),
            new Price(2m, Currency.USD),
            Money.Zero(Currency.USD));
        projector.Apply(optionFill, runtime);

        var lifecycle = new OptionLifecycleApplied(
            strategy,
            VariantId: 0,
            option.Instrument,
            OptionLifecycleKind.CashSettlement,
            new Qty(1m),
            Money.Zero(Currency.USD),
            expiry,
            UnderlyingMark: new Price(102m, Currency.USD),
            SettlementPrice: new Price(102m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark);

        var transition = projector.Apply(lifecycle, runtime);

        Assert.Equal(PositionTransitionKind.Closed, transition.PositionTransition.Kind);
        Assert.Equal(0m, runtime.WorldState.PositionAt(strategy, 0).Quantity);
    }

    [Fact]
    public void PortfolioProjector_PartialOptionLifecycle_ClosesOnlyEventQuantity()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new StrategyId(1);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry,
            assignmentPolicy: OptionAssignmentPolicy.ProRata);
        AddContract(runtime, option);
        var projector = new SimulationPortfolioProjector();
        var optionFill = new OrderFilled(
            OrderId.New(),
            option.Instrument,
            VariantId: 0,
            strategy,
            Side.Sell,
            new Qty(2m),
            new Price(2m, Currency.USD),
            Money.Zero(Currency.USD));
        projector.Apply(optionFill, runtime);

        var assigned = new OptionLifecycleApplied(
            strategy,
            VariantId: 0,
            option.Instrument,
            OptionLifecycleKind.CashSettlement,
            new Qty(-1m),
            Money.USD(-500m),
            expiry,
            UnderlyingMark: new Price(105m, Currency.USD),
            SettlementPrice: new Price(105m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark);
        var unassigned = new OptionLifecycleApplied(
            strategy,
            VariantId: 0,
            option.Instrument,
            OptionLifecycleKind.ExpireUnassigned,
            new Qty(-1m),
            Money.Zero(Currency.USD),
            expiry,
            UnderlyingMark: new Price(105m, Currency.USD),
            SettlementPrice: new Price(105m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark);

        var assignedTransition = projector.Apply(assigned, runtime);
        var afterAssigned = runtime.WorldState.PositionAt(strategy, 0);
        var unassignedTransition = projector.Apply(unassigned, runtime);
        var afterUnassigned = runtime.WorldState.PositionAt(strategy, 0);

        Assert.Equal(PositionTransitionKind.Changed, assignedTransition.PositionTransition.Kind);
        Assert.Equal(-1m, afterAssigned.Quantity);
        Assert.Equal(PositionTransitionKind.Closed, unassignedTransition.PositionTransition.Kind);
        Assert.Equal(0m, afterUnassigned.Quantity);
    }

    [Fact]
    public void OptionLifecycleApplied_UnknownLifecycleKind_Throws()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new StrategyId(1);
        var underlying = new Instrument(new Asset("SPX", AssetClass.Index), new Venue("CBOE"));
        var expiry = Instant.FromUnixSeconds(1_796_016_000);
        var option = Contracts.OptionContract(
            "SPX261218C00100000",
            new Venue("CBOE"),
            underlying,
            Currency.USD,
            tick: 0.05m,
            lot: 1m,
            multiplier: 100m,
            strike: new Price(100m, Currency.USD),
            expiry,
            OptionRight.Call,
            ExerciseStyle.European,
            exercisePolicy: OptionExercisePolicy.CashSettledAtExpiry);
        AddContract(runtime, option);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new OptionLifecycleApplied(
            strategy,
            VariantId: 0,
            option.Instrument,
            (OptionLifecycleKind)99,
            new Qty(1m),
            Money.Zero(Currency.USD),
            expiry,
            UnderlyingMark: new Price(105m, Currency.USD),
            SettlementPrice: new Price(105m, Currency.USD),
            ReferenceSource: OptionLifecycleReferenceSource.MarketMark));

        Assert.Contains("Unknown option lifecycle kind", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Projectors_UnhandledEvent_DoNotThrow()
    {
        using var runtime = CreateRuntime(variantCount: 1, out _);
        var marketProjector = new SimulationMarketProjector();
        var portfolioProjector = new SimulationPortfolioProjector();
        var accepted = new OrderAccepted(OrderId.New(), new StrategyId(1), 0);

        var marketTransition = marketProjector.Apply(accepted, runtime);
        var portfolioTransition = portfolioProjector.Apply(accepted, runtime);

        Assert.False(marketTransition.RequiresAdjustment);
        Assert.False(portfolioTransition.RequiresAdjustment);
        Assert.Equal(PositionTransitionKind.None, portfolioTransition.PositionTransition.Kind);
    }

    private static RhodiumRuntime CreateRuntime(int variantCount, out Instrument instrument)
    {
        var runtime = new RhodiumRuntime();
        var contract = Contracts.Equity("SPY", Venue.NYSE, Currency.USD);
        instrument = contract.Instrument;
        runtime.BatchMap.AddInstrument(instrument, variantCount);
        for (var i = 0; i < variantCount; i++)
        {
            runtime.Tensors.Grow();
            runtime.SetContract(i, contract);
        }

        return runtime;
    }

    private static void AddContract(RhodiumRuntime runtime, InstrumentContract contract)
    {
        runtime.BatchMap.AddInstrument(contract.Instrument, 1);
        runtime.Tensors.Grow();
        runtime.SetContract(runtime.BatchMap.GetInstrumentRange(contract.Instrument).Start, contract);
    }
}
