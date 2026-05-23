using Rhodium.Events;
using Rhodium.Platform;
using Rhodium.Platform.Attributes;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Tests;

public sealed class SimulationRuntimeTests
{
    [Fact]
    public void SharedHistory_Load_PreservesOrderAndCount()
    {
        var first = CreateBarClosed(101m);
        var second = CreateBarClosed(102m);

        var history = SharedHistory.Load([first, second]);

        Assert.Equal(2, history.Count);
        Assert.Same(first, history.Span[0]);
        Assert.Same(second, history.Span[1]);
    }

    [Fact]
    public void SimulationRuntime_Run_ReplaysBarHistoryDeterministically()
    {
        var first = RunSingleBarSimulation();
        var second = RunSingleBarSimulation();

        Assert.Single(first.Runs);
        Assert.Single(first.OrderIntents);
        Assert.Equal(Side.Buy, first.OrderIntents[0].Side);
        Assert.Equal(new Qty(1m), first.OrderIntents[0].Quantity);
        Assert.Single(second.OrderIntents);
        Assert.Equal(first.OrderIntents[0].Side, second.OrderIntents[0].Side);
        Assert.Equal(first.OrderIntents[0].Quantity, second.OrderIntents[0].Quantity);
    }

    [Fact]
    public void VectorExecutionModel_FillsTouchedLimitInsideEventBoundaryAndDispatchesHooks()
    {
        VectorFillStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(123m)]);

        var result = Rhodium.Simulate<VectorFillStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        Assert.Single(result.OrderIntents);
        Assert.Contains(result.ExecutionEvents, static evt => evt is OrderAccepted);
        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), fill.FilledQty);
        Assert.Equal(new Price(123m, Currency.USD), fill.FillPrice);
        Assert.Equal(1, VectorFillStrategy.FillCount);
        Assert.Equal(new Qty(1m), result.Runs.Single().FinalSnapshot.GetPositions().ToArray().Single().Quantity);
    }

    [Fact]
    public void HookOnlyBarStrategy_RunsWithoutDummyGeneratedFields()
    {
        HookOnlyBarStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(123m)]);

        var result = Rhodium.Simulate<HookOnlyBarStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        Assert.Single(result.OrderIntents);
        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(1, HookOnlyBarStrategy.BarCount);
        Assert.Equal(new Qty(1m), result.Runs.Single().FinalSnapshot.GetPositions().ToArray().Single().Quantity);
    }

    [Fact]
    public void SimulationRuntime_BuildsRunTearSheetsFromExecutionFills()
    {
        var history = SharedHistory.Load(
        [
            CreateBarClosed(100m),
            CreateBarClosed(110m)
        ]);

        var result = Rhodium.Simulate<RoundTripStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .WithInitialCash(Money.USD(1_000m))
            .Run();

        var run = Assert.Single(result.Runs);
        Assert.Equal(2, result.ExecutionEvents.OfType<OrderFilled>().Count());
        Assert.Equal(1, run.TearSheet.TotalTrades);
        Assert.Equal(Money.USD(10m), run.TearSheet.TotalPnL);
        Assert.Equal(0.01m, run.TearSheet.TotalReturn);
        Assert.Equal(0.01d, result.Batch.TotalReturn.Span[0]);
    }

    [Fact]
    public void QueueExecutionModel_DoesNotFillOrderOnSubmissionBar()
    {
        var history = SharedHistory.Load([CreateBarClosed(123m)]);

        var result = Rhodium.Simulate<VectorFillStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Queue)
            .Run();

        Assert.Single(result.OrderIntents);
        Assert.Contains(result.ExecutionEvents, static evt => evt is OrderAccepted);
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderFilled);
        Assert.Empty(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
    }

    [Theory]
    [InlineData(SimulationFidelity.Vector, MarketStatus.PreOpen)]
    [InlineData(SimulationFidelity.Vector, MarketStatus.Closed)]
    [InlineData(SimulationFidelity.Vector, MarketStatus.Halted)]
    [InlineData(SimulationFidelity.Queue, MarketStatus.PreOpen)]
    [InlineData(SimulationFidelity.Queue, MarketStatus.Closed)]
    [InlineData(SimulationFidelity.Queue, MarketStatus.Halted)]
    public void ExecutionModels_RejectOrdersWhenInitialMarketIsNotOpen(
        SimulationFidelity fidelity,
        MarketStatus status)
    {
        RejectedMarketStatusStrategy.Reset();
        var config = (fidelity == SimulationFidelity.Vector
            ? SimulationConfig.Vector()
            : SimulationConfig.Queue()) with
        {
            InitialMarketStatus = status
        };
        var history = SharedHistory.Load([CreateBarClosed(123m), CreateBarClosed(123m)]);

        var result = Rhodium.Simulate<RejectedMarketStatusStrategy>()
            .WithHistory(history)
            .WithConfig(config)
            .Run();

        var rejection = Assert.Single(result.ExecutionEvents.OfType<OrderRejected>());
        Assert.Contains(status.ToString(), rejection.Reason);
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderAccepted);
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderFilled);
        Assert.Equal(1, RejectedMarketStatusStrategy.RejectedCount);
        Assert.Empty(result.Runs.Single().FinalSnapshot.GetPositions().ToArray());
    }

    [Fact]
    public void VectorExecutionModel_FillsStopMarketWhenBarTouchesStop()
    {
        StopMarketStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(close: 123m, high: 125m, low: 121m)]);

        var result = Rhodium.Simulate<StopMarketStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Price(124m, Currency.USD), fill.FillPrice);
        Assert.Equal(1, StopMarketStrategy.FillCount);
    }

    [Fact]
    public void VectorExecutionModel_FillsStopLimitWhenStopAndLimitTouch()
    {
        StopLimitStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(close: 123m, high: 125m, low: 122m)]);

        var result = Rhodium.Simulate<StopLimitStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Price(123m, Currency.USD), fill.FillPrice);
        Assert.Equal(1, StopLimitStrategy.FillCount);
    }

    [Fact]
    public void QueueExecutionModel_FillsStopLimitOnLaterTouchedBar()
    {
        StopLimitStrategy.Reset();
        var history = SharedHistory.Load(
        [
            CreateBarClosed(close: 120m, high: 121m, low: 119m),
            CreateBarClosed(close: 123m, high: 125m, low: 122m)
        ]);

        var result = Rhodium.Simulate<StopLimitStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Queue)
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Price(123m, Currency.USD), fill.FillPrice);
        Assert.Equal(1, StopLimitStrategy.FillCount);
    }

    [Fact]
    public void QueueExecutionModel_FixedBarOrderingDoesNotFillSellStopLimitWhenLimitTouchedBeforeStop()
    {
        SellStopLimitStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(open: 123m, high: 125m, low: 119m, close: 121m)]);

        var result = Rhodium.Simulate<SellStopLimitStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Queue)
            .Run();

        Assert.Contains(result.ExecutionEvents, static evt => evt is OrderAccepted);
        Assert.DoesNotContain(result.ExecutionEvents, static evt => evt is OrderFilled);
        Assert.Equal(0, SellStopLimitStrategy.FillCount);
    }

    [Fact]
    public void VectorExecutionModel_AppliesConfiguredSlippageAndFees()
    {
        VectorFillStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(123m)]);
        var config = SimulationConfig.Vector() with
        {
            Slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 10m),
            Fees = FeeParams.MakerTaker(makerBps: 5m, takerBps: 11m)
        };

        var result = Rhodium.Simulate<VectorFillStrategy>()
            .WithHistory(history)
            .WithConfig(config)
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Price(123.123m, Currency.USD), fill.FillPrice);
        Assert.Equal(Money.USD(0.0615615m), fill.Commission);
    }

    [Fact]
    public void VectorExecutionModel_AppliesConfiguredMakerPriceImprovement()
    {
        VectorFillStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(123m)]);
        var config = SimulationConfig.Vector() with
        {
            PriceImprovement = PriceImprovementParams.FixedBps(takerBps: 0m, makerBps: 10m)
        };

        var result = Rhodium.Simulate<VectorFillStrategy>()
            .WithHistory(history)
            .WithConfig(config)
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Price(122.877m, Currency.USD), fill.FillPrice);
    }

    [Fact]
    public void QueueExecutionModel_PartiallyFillsFromTradeSizes()
    {
        PartialFillStrategy.Reset();
        var history = SharedHistory.Load(
        [
            CreateBarClosed(120m),
            CreateTradeOccurred(price: 123m, size: 0.4m, Side.Sell),
            CreateTradeOccurred(price: 123m, size: 0.6m, Side.Sell)
        ]);
        var config = SimulationConfig.Queue() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade
        };

        var result = Rhodium.Simulate<PartialFillStrategy>()
            .WithHistory(history)
            .WithConfig(config)
            .Run();

        var fills = result.ExecutionEvents.OfType<OrderFilled>().ToArray();
        Assert.Equal([new Qty(0.4m), new Qty(0.6m)], fills.Select(static fill => fill.FilledQty).ToArray());
        Assert.Equal(2, PartialFillStrategy.FillCount);
        Assert.Equal(new Qty(1m), result.Runs.Single().FinalSnapshot.GetPositions().ToArray().Single().Quantity);
    }

    [Fact]
    public void QueueExecutionModel_TailQueueAdvancesBeforeFill()
    {
        PartialFillStrategy.Reset();
        var history = SharedHistory.Load(
        [
            CreateBarClosed(120m),
            CreateTradeOccurred(price: 123m, size: 0.4m, Side.Sell),
            CreateTradeOccurred(price: 123m, size: 0.6m, Side.Sell)
        ]);
        var config = SimulationConfig.Queue() with
        {
            QueueModel = QueueParams.DeterministicTail(initialPosition: 1m, advancementPerUnit: 1m),
            FillBehavior = FillBehavior.NoPartialFill
        };

        var result = Rhodium.Simulate<PartialFillStrategy>()
            .WithHistory(history)
            .WithConfig(config)
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), fill.FilledQty);
        Assert.Equal(1, PartialFillStrategy.FillCount);
    }

    [Fact]
    public void VectorExecutionModel_IgnoresPartialFillBehaviorAndFillsFullQuantity()
    {
        PartialFillStrategy.Reset();
        var history = SharedHistory.Load([CreateBarClosed(120m)]);
        var config = SimulationConfig.Vector() with
        {
            FillBehavior = FillBehavior.PartialFillOnTrade
        };

        var result = Rhodium.Simulate<PartialFillStrategy>()
            .WithHistory(history)
            .WithConfig(config)
            .Run();

        var fill = Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), fill.FilledQty);
        Assert.Equal(1, PartialFillStrategy.FillCount);
    }

    [Fact]
    public void SimulationBuilder_Run_WithGridRegistersVariantsAndPreservesParameters()
    {
        var grid = ParameterGrid.Create()
            .Add(nameof(GridStrategy.Period), 10, 14, 21);
        var history = SharedHistory.Load(Array.Empty<FinanceEvent>());

        var result = Rhodium.Simulate<GridStrategy>()
            .WithHistory(history)
            .WithGrid(grid)
            .Run();

        Assert.Equal(3, result.Runs.Count);
        Assert.Equal([10, 14, 21], result.Runs.Select(static run => run.Parameters.Get<int>(nameof(GridStrategy.Period))).ToArray());
        Assert.Equal(3, result.ToParameterGrid().Count);
    }

    [Fact]
    public void VectorFinalists_ValidateInQueueModeWithSameGeneratedStrategy()
    {
        var grid = ParameterGrid.Create()
            .Add(nameof(FidelityValidationStrategy.Lots), 1, 2);
        var history = SharedHistory.Load(
        [
            CreateBarClosed(open: 100m, high: 101m, low: 99m, close: 100m),
            CreateBarClosed(open: 110m, high: 111m, low: 99m, close: 110m),
            CreateBarClosed(open: 110m, high: 111m, low: 109m, close: 110m)
        ]);

        var vectorResults = Rhodium.Simulate<FidelityValidationStrategy>()
            .WithHistory(history)
            .WithGrid(grid)
            .WithFidelity(SimulationFidelity.Vector)
            .WithInitialCash(Money.USD(100_000m))
            .Run();

        var finalistGrid = vectorResults
            .TopByTotalReturn(1)
            .ToParameterGrid();

        var queueResults = Rhodium.Simulate<FidelityValidationStrategy>()
            .WithHistory(history)
            .WithGrid(finalistGrid)
            .WithFidelity(SimulationFidelity.Queue)
            .WithInitialCash(Money.USD(100_000m))
            .Run();

        var vectorRun = Assert.Single(vectorResults.TopByTotalReturn(1));
        var queueRun = Assert.Single(queueResults.Runs);
        Assert.Equal(2, vectorRun.Parameters.Get<int>(nameof(FidelityValidationStrategy.Lots)));
        Assert.Equal(2, queueRun.Parameters.Get<int>(nameof(FidelityValidationStrategy.Lots)));
        Assert.Equal(1, vectorRun.TearSheet.TotalTrades);
        Assert.Equal(1, queueRun.TearSheet.TotalTrades);
        Assert.Equal(Money.USD(20m), vectorRun.TearSheet.TotalPnL);
        Assert.Equal(Money.USD(20m), queueRun.TearSheet.TotalPnL);
    }

    [Fact]
    public void QueueAndVector_DispatchSameExecutionAndPositionHooksWhenFilled()
    {
        var vectorCounts = RunLifecycleParitySimulation(
            SimulationFidelity.Vector,
            SharedHistory.Load([CreateBarClosed(123m)]));
        var queueCounts = RunLifecycleParitySimulation(
            SimulationFidelity.Queue,
            SharedHistory.Load([CreateBarClosed(123m), CreateBarClosed(123m)]));

        Assert.Equal(vectorCounts, queueCounts);
        Assert.Equal(new ExecutionHookCounts(1, 1, 1), vectorCounts);
    }

    [Fact]
    public void SimulationRuntime_ReplaysLifecycleEventsThroughOperationalHooks()
    {
        OperationalLifecycleStrategy.Reset();
        var history = SharedHistory.Load(
        [
            new SessionStarted(),
            new Scheduled("rebalance"),
            new SessionEnded()
        ]);

        var result = Rhodium.Simulate<OperationalLifecycleStrategy>()
            .WithHistory(history)
            .Run();

        Assert.Single(result.Runs);
        Assert.Equal(new OperationalLifecycleCounts(1, 1, 1), OperationalLifecycleStrategy.Counts);
        Assert.Equal("rebalance", OperationalLifecycleStrategy.LastScheduleName);
    }

    [Fact]
    public void GeneratedWindowedBarField_TracksRollingHistoryAcrossReplay()
    {
        WindowedCloseStrategy.Reset();
        var history = SharedHistory.Load(
        [
            CreateBarClosed(10m),
            CreateBarClosed(20m),
            CreateBarClosed(30m)
        ]);

        var result = Rhodium.Simulate<WindowedCloseStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        Assert.Single(result.Runs);
        Assert.Equal(3, WindowedCloseStrategy.BarCount);
        Assert.Equal(20d, WindowedCloseStrategy.LastMean);
        Assert.Equal(30d, WindowedCloseStrategy.LastClose);
    }

    [Fact]
    public void GeneratedWindowedBarField_ZScoreStrategyCompilesAndRuns()
    {
        ZScoreWindowStrategy.Reset();
        var history = SharedHistory.Load(
        [
            CreateBarClosed(10m),
            CreateBarClosed(20m),
            CreateBarClosed(30m)
        ]);

        var result = Rhodium.Simulate<ZScoreWindowStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        Assert.Single(result.Runs);
        Assert.Equal(3, ZScoreWindowStrategy.BarCount);
        Assert.Equal(Math.Sqrt(1.5), ZScoreWindowStrategy.LastZScore, precision: 12);
    }

    [Fact]
    public void GeneratedWindowedBarField_TwentyPeriodWindowCompilesAndRuns()
    {
        TwentyPeriodWindowStrategy.Reset();
        var history = SharedHistory.Load(
            Enumerable.Range(1, 20).Select(static value => CreateBarClosed(value)));

        var result = Rhodium.Simulate<TwentyPeriodWindowStrategy>()
            .WithHistory(history)
            .WithFidelity(SimulationFidelity.Vector)
            .Run();

        Assert.Single(result.Runs);
        Assert.Equal(20, TwentyPeriodWindowStrategy.BarCount);
        Assert.Equal(10.5d, TwentyPeriodWindowStrategy.LastMean);
    }

    private static SimulationResult RunSingleBarSimulation()
    {
        using var runtime = new SimulationRuntime();
        runtime.RegisterStrategy<SimulationBuyingStrategy>();

        return runtime.Run(SharedHistory.Load([CreateBarClosed(123m)]));
    }

    private static ExecutionHookCounts RunLifecycleParitySimulation(
        SimulationFidelity fidelity,
        SharedHistory history)
    {
        LifecycleParityStrategy.Reset();

        var result = Rhodium.Simulate<LifecycleParityStrategy>()
            .WithHistory(history)
            .WithFidelity(fidelity)
            .Run();

        Assert.Single(result.ExecutionEvents.OfType<OrderFilled>());
        Assert.Equal(new Qty(1m), result.Runs.Single().FinalSnapshot.GetPositions().ToArray().Single().Quantity);
        return LifecycleParityStrategy.Counts;
    }

    private static BarClosed CreateBarClosed(decimal close)
        => CreateBarClosed(close, high: close + 1m, low: close - 1m);

    private static BarClosed CreateBarClosed(decimal close, decimal high, decimal low)
        => CreateBarClosed(open: close, high, low, close);

    private static BarClosed CreateBarClosed(decimal open, decimal high, decimal low, decimal close)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var bar = new Bar(
            new Price(open, Currency.USD),
            new Price(high, Currency.USD),
            new Price(low, Currency.USD),
            new Price(close, Currency.USD),
            new Qty(10_000m),
            default,
            Duration.FromMinutes(1));

        return new BarClosed(instrument, bar);
    }

    private static TradeOccurred CreateTradeOccurred(decimal price, decimal size, Side aggressorSide)
    {
        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var trade = new Trade(
            new Price(price, Currency.USD),
            new Qty(size),
            aggressorSide,
            DualTimestamp.Synchronized(default));

        return new TradeOccurred(instrument, trade);
    }
}

internal readonly record struct ExecutionHookCounts(
    int Accepted,
    int Filled,
    int PositionOpened);

internal readonly record struct OperationalLifecycleCounts(
    int Started,
    int Scheduled,
    int Stopped);

internal sealed partial class SimulationBuyingStrategy : Strategy
{
    private AssetId _spy;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy && bar.Close == 123.0)
            bar.Buy(new Qty(1m), Execution.Limit().AtBid());
    }
}

internal sealed partial class HookOnlyBarStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    public static int BarCount { get; private set; }

    public static void Reset()
    {
        BarCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy)
            return;

        BarCount++;
        if (_submitted)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Limit().At(new Price(123m, Currency.USD)));
    }
}

internal sealed class OperationalLifecycleStrategy : Strategy
{
    public static OperationalLifecycleCounts Counts { get; private set; }
    public static string? LastScheduleName { get; private set; }

    public static void Reset()
    {
        Counts = default;
        LastScheduleName = null;
    }

    protected override void OnStart(ref LifecycleContext lifecycle)
    {
        Counts = Counts with { Started = Counts.Started + 1 };
    }

    protected override void OnScheduled(ref TimerContext timer)
    {
        Counts = Counts with { Scheduled = Counts.Scheduled + 1 };
        LastScheduleName = timer.Name;
    }

    protected override void OnStop(ref LifecycleContext lifecycle)
    {
        Counts = Counts with { Stopped = Counts.Stopped + 1 };
    }
}

internal sealed partial class LifecycleParityStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;
    private static int s_accepted;
    private static int s_filled;
    private static int s_positionOpened;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static ExecutionHookCounts Counts => new(s_accepted, s_filled, s_positionOpened);

    public static void Reset()
    {
        s_accepted = 0;
        s_filled = 0;
        s_positionOpened = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 123.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Limit().At(new Price(123m, Currency.USD)));
    }

    protected override void OnOrderAccepted(ref OrderContext order)
    {
        s_accepted++;
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        s_filled++;
    }

    protected override void OnPositionOpened(ref PositionContext position)
    {
        s_positionOpened++;
    }
}

internal sealed partial class RejectedMarketStatusStrategy : Strategy
{
    private AssetId _spy;
    private bool _submitted;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static int RejectedCount { get; private set; }

    public static void Reset()
    {
        RejectedCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (_submitted || bar.AssetId != _spy || bar.Close != 123.0)
            return;

        _submitted = true;
        bar.Buy(new Qty(1m), Execution.Limit().At(new Price(123m, Currency.USD)));
    }

    protected override void OnOrderRejected(ref OrderContext order)
    {
        RejectedCount++;
    }
}

internal sealed partial class GridStrategy : Strategy
{
    [Param]
    public int Period { get; init; }
}

internal sealed partial class FidelityValidationStrategy : Strategy
{
    private AssetId _spy;
    private bool _entrySubmitted;
    private bool _exitSubmitted;

    [Param]
    public int Lots { get; init; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy)
            return;

        if (!_entrySubmitted && bar.Close == 100.0)
        {
            _entrySubmitted = true;
            bar.Buy(new Qty(Lots), Execution.Limit().At(new Price(100m, Currency.USD)));
            return;
        }

        if (_entrySubmitted && !_exitSubmitted && bar.Close == 110.0)
        {
            _exitSubmitted = true;
            bar.Sell(new Qty(Lots), Execution.Limit().At(new Price(110m, Currency.USD)));
        }
    }
}

internal sealed partial class WindowedCloseStrategy : Strategy
{
    private AssetId _spy;

    public static int BarCount { get; private set; }
    public static double LastMean { get; private set; }
    public static double LastClose { get; private set; }

    [BarField(ReadOnly = true)]
    [Window(3)]
    public partial double Close { get; }

    public static void Reset()
    {
        BarCount = 0;
        LastMean = 0d;
        LastClose = 0d;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy)
            return;

        BarCount++;
        LastClose = bar.Close;
        if (BarCount >= 3)
            LastMean = bar.Close.Window(3).Mean();
    }
}

internal sealed partial class ZScoreWindowStrategy : Strategy
{
    private AssetId _spy;

    public static int BarCount { get; private set; }
    public static double LastZScore { get; private set; }

    [BarField(ReadOnly = true)]
    [Window(3)]
    public partial double Close { get; }

    public static void Reset()
    {
        BarCount = 0;
        LastZScore = 0d;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy)
            return;

        BarCount++;
        if (BarCount < 3)
            return;

        var closes = bar.Close.Window(3);
        LastZScore = (bar.Close - closes.Mean()) / closes.StdDev();
    }
}

internal sealed partial class TwentyPeriodWindowStrategy : Strategy
{
    private AssetId _spy;

    public static int BarCount { get; private set; }
    public static double LastMean { get; private set; }

    [BarField(ReadOnly = true)]
    [Window(20)]
    public partial double Close { get; }

    public static void Reset()
    {
        BarCount = 0;
        LastMean = 0d;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy)
            return;

        BarCount++;
        if (BarCount == 20)
            LastMean = bar.Close.Window(20).Mean();
    }
}

internal sealed partial class VectorFillStrategy : Strategy
{
    private AssetId _spy;

    public static int FillCount { get; private set; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
        FillCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy && bar.Close == 123.0)
            bar.Buy(new Qty(1m), Execution.Limit().At(new Price(123m, Currency.USD)));
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        FillCount++;
    }
}

internal sealed partial class RoundTripStrategy : Strategy
{
    private AssetId _spy;

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId != _spy)
            return;

        if (bar.Close == 100.0)
            bar.Buy(new Qty(1m), Execution.Limit().At(new Price(100m, Currency.USD)));
        else if (bar.Close == 110.0)
            bar.Sell(new Qty(1m), Execution.Limit().At(new Price(110m, Currency.USD)));
    }
}

internal sealed partial class StopMarketStrategy : Strategy
{
    private AssetId _spy;

    public static int FillCount { get; private set; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
        FillCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy)
            bar.Buy(new Qty(1m), Execution.StopMarket(new Price(124m, Currency.USD)));
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        FillCount++;
    }
}

internal sealed partial class StopLimitStrategy : Strategy
{
    private AssetId _spy;

    public static int FillCount { get; private set; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
        FillCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy && bar.Close <= 123.0)
            bar.Buy(new Qty(1m), Execution.StopLimit(new Price(124m, Currency.USD), new Price(123m, Currency.USD)));
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        FillCount++;
    }
}

internal sealed partial class SellStopLimitStrategy : Strategy
{
    private AssetId _spy;

    public static int FillCount { get; private set; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
        FillCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy)
            bar.Sell(new Qty(1m), Execution.StopLimit(new Price(120m, Currency.USD), new Price(124m, Currency.USD)));
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        FillCount++;
    }
}

internal sealed partial class PartialFillStrategy : Strategy
{
    private AssetId _spy;

    public static int FillCount { get; private set; }

    [BarField(ReadOnly = true)]
    public partial double Close { get; }

    public static void Reset()
    {
        FillCount = 0;
    }

    protected override void OnInitialize(in SetupContext setup)
    {
        _spy = setup.AddEquity("SPY");
    }

    partial void OnBar(ref BarContext bar)
    {
        if (bar.AssetId == _spy && bar.Close == 120.0)
            bar.Buy(new Qty(1m), Execution.Limit().At(new Price(123m, Currency.USD)));
    }

    protected override void OnOrderFilled(ref FillContext fill)
    {
        FillCount++;
    }
}
