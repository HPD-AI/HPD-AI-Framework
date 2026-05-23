using Rhodium.Kernel;
using Rhodium.Events;
using Rhodium.Platform.Attributes;
using Rhodium.Platform.Examples;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

public class StrategyTests
{
    [Fact]
    public void Initialize_CallsOnInitializeAndRegistersUniverse()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new TestStrategy();

        strategy.Initialize(runtime);

        Assert.True(strategy.OnInitializeCalled);
        Assert.Equal(10, runtime.BatchMap.TotalSize);
        Assert.Equal(0, strategy.Spy.VirtualIndex);
        Assert.Equal(1, strategy.SpyVariant.VirtualIndex);
    }

    [Fact]
    public void RunTickGuarded_CallsGeneratedTickHook()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new TestStrategy();
        strategy.Initialize(runtime);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(123);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);

        strategy.RunTickGuarded(in market, ref portfolio);

        Assert.True(strategy.OnTickCalled);
        Assert.Equal(123, strategy.CloseRead);
    }

    [Fact]
    public void RunTickGuarded_ThrowsWhenUniverseVersionChanges()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new TestStrategy();
        strategy.Initialize(runtime);
        runtime.BatchMap.AddInstrument(new Instrument(new Asset("NEW", AssetClass.Equity), Venue.NASDAQ));
        runtime.Tensors.Grow();

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);

        UniverseTopologyChangedException? ex = null;
        try
        {
            strategy.RunTickGuarded(in market, ref portfolio);
        }
        catch (UniverseTopologyChangedException caught)
        {
            ex = caught;
        }

        Assert.NotNull(ex);
        Assert.True(ex.ActualVersion > ex.ExpectedVersion);
    }

    [Fact]
    public void RunTickGuarded_ThrowsWhenStrategyAllocatesOnHotPath()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new AllocatingStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);

#if DEBUG
        HotPathAllocationException? ex = null;
        try
        {
            strategy.RunTickGuarded(in market, ref portfolio);
            strategy.RunTickGuarded(in market, ref portfolio);
        }
        catch (HotPathAllocationException caught)
        {
            ex = caught;
        }

        Assert.NotNull(ex);
#else
        strategy.RunTickGuarded(in market, ref portfolio);
#endif
    }

    [Fact]
    public void RunExecutionGuarded_ThrowsWhenStrategyAllocatesOnHotPath()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new AllocatingExecutionStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);
        var accepted = new OrderAccepted(new OrderId(1), strategy.Id, VariantId: 0);

#if DEBUG
        HotPathAllocationException? ex = null;
        try
        {
            strategy.RunExecutionGuarded(in market, ref portfolio, accepted, default);
            strategy.RunExecutionGuarded(in market, ref portfolio, accepted, default);
        }
        catch (HotPathAllocationException caught)
        {
            ex = caught;
        }

        Assert.NotNull(ex);
#else
        strategy.RunExecutionGuarded(in market, ref portfolio, accepted, default);
#endif
    }

    [Fact]
    public void RunLifecycleGuarded_ThrowsWhenStrategyAllocatesOnHotPath()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new AllocatingLifecycleStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);
        var started = new SessionStarted();

#if DEBUG
        HotPathAllocationException? ex = null;
        try
        {
            strategy.RunLifecycleGuarded(in market, ref portfolio, started);
            strategy.RunLifecycleGuarded(in market, ref portfolio, started);
        }
        catch (HotPathAllocationException caught)
        {
            ex = caught;
        }

        Assert.NotNull(ex);
#else
        strategy.RunLifecycleGuarded(in market, ref portfolio, started);
#endif
    }

    [Fact]
    public void GeneratedIndicatorRegistration_DuringInitialize_AllocatesColumn()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new IndicatorRegistrationStrategy();

        strategy.Initialize(runtime);
        runtime.Tensors.GetScalar(Fields.RSI_14, 0) = new FactorF64(50);

        Assert.Equal(50, runtime.CreateMarketKernel().GetRsi14(new AssetId(0)));
    }

    [Fact]
    public void GeneratedBarContext_OnBarPath_UpdatesIndicatorAndTrades()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new RsiMeanReversion();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);

        for (var i = 0; i < 20; i++)
        {
            runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100 - i);
            strategy.RunBarGuarded(in market, ref portfolio);
        }

        Assert.Equal(0.5m, portfolio.GetPositionQty(new AssetId(0)));
    }

    [Fact]
    public void GeneratedIndicator_WritesValueToStrategyPrivateTensorStore()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new SimpleRsiStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);

        for (var i = 0; i < 20; i++)
        {
            runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100 + i);
            strategy.RunBarGuarded(in market, ref portfolio);
        }

        var id = new AssetId(0);
        var persisted = portfolio.GetScalar(Fields.RSI_14, id).Value;

        Assert.True(persisted > 70d);
        Assert.Equal(persisted, strategy.GetRsi(id, ref portfolio));
    }

    [Fact]
    public void GeneratedWritableBarField_WritesToStrategyPrivateStore()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new WritableSignalStrategy();
        strategy.Initialize(runtime);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);

        strategy.RunBarGuarded(in market, ref portfolio);

        Assert.Equal(42d, portfolio.GetScalar(WritableSignalStrategy.SignalField, new AssetId(0)).Value);
    }

    [Fact]
    public void GeneratedBarContext_VisitsOnlyAssetsRegisteredByStrategy()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new RegisteredAssetCountingStrategy();
        strategy.Initialize(runtime);

        Assert.Equal(20, runtime.BatchMap.TotalSize);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands);

        strategy.RunBarGuarded(in market, ref portfolio);

        Assert.Equal(2, strategy.VisitCount);
    }

    [Fact]
    public void GeneratedBarContext_ExecutionSpecOrderIntentSurvivesDispatch()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new GeneratedOrderIntentStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[8];
        var portfolio = runtime.WorldState.BuildContext(
            strategy.Id,
            null,
            default,
            CreateCounters(),
            commands,
            orderIntents: orderIntents);

        strategy.RunBarGuarded(in market, ref portfolio);

        var drained = portfolio.DrainOrderIntents();
        Assert.Equal(1, drained.Length);
        Assert.Equal(strategy.Id, drained[0].StrategyId);
        Assert.Equal(0, drained[0].AssetId.VirtualIndex);
        Assert.Equal(Side.Buy, drained[0].Side);
        Assert.Equal(OrderType.Limit, drained[0].Execution.OrderType);
        Assert.True(drained[0].Execution.PostOnly);
    }

    [Fact]
    public void GeneratedBarContext_OrderIntentAndGroupCommandSurviveSameDispatch()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new GeneratedOrderAndGroupStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[8];
        Span<StrategyId> childIds = stackalloc StrategyId[] { new(777) };
        var portfolio = runtime.WorldState.BuildContext(
            strategy.Id,
            null,
            childIds,
            CreateCounters(),
            commands,
            orderIntents: orderIntents);

        strategy.RunBarGuarded(in market, ref portfolio);

        var drainedIntents = portfolio.DrainOrderIntents();
        var drainedCommands = portfolio.DrainCommands();

        Assert.Equal(1, drainedIntents.Length);
        Assert.Equal(1, drainedCommands.Length);
        Assert.Equal(childIds[0], drainedCommands[0].TargetStrategy);
        Assert.True(drainedCommands[0].HasAllocationWeight);
        Assert.Equal(0.5m, drainedCommands[0].AllocationWeight);
    }

    [Fact]
    public void GeneratedBarContext_OrderIntentSurvivesWorldStateCommit()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new GeneratedOrderIntentStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[8];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands, orderIntents: orderIntents);

        strategy.RunBarGuarded(in market, ref portfolio);
        runtime.WorldState.CommitContext(strategy.Id, ref portfolio);

        var drained = new OrderIntent[8];
        var count = runtime.WorldState.DrainOrderIntents(strategy.Id, drained);
        Assert.Equal(1, count);
        Assert.Equal(strategy.Id, drained[0].StrategyId);
        Assert.Equal(0, drained[0].AssetId.VirtualIndex);
        Assert.Equal(Side.Buy, drained[0].Side);
        Assert.Equal(OrderType.Limit, drained[0].Execution.OrderType);
        Assert.True(drained[0].Execution.PostOnly);
    }

    [Fact]
    public void GeneratedTickContext_OrderIntentSurvivesWorldStateCommit()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new GeneratedTickOrderIntentStrategy();
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[8];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands, orderIntents: orderIntents);

        strategy.RunTickGuarded(in market, ref portfolio);
        runtime.WorldState.CommitContext(strategy.Id, ref portfolio);

        AssertCommittedOrderIntent(runtime, strategy.Id, Side.Buy);
    }

    [Fact]
    public void GeneratedQuoteContext_OrderIntentSurvivesWorldStateCommit()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new GeneratedQuoteOrderIntentStrategy();
        strategy.Initialize(runtime);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var quote = new QuoteReceived(
            instrument,
            new Quote(
                new Price(100m, Currency.USD),
                new Price(100.01m, Currency.USD),
                new Qty(200m),
                new Qty(100m),
                default));
        var (start, length) = runtime.BatchMap.GetInstrumentRange(instrument);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[8];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands, orderIntents: orderIntents);

        strategy.RunQuoteGuarded(in market, ref portfolio, in quote, start, length);
        runtime.WorldState.CommitContext(strategy.Id, ref portfolio);

        AssertCommittedOrderIntent(runtime, strategy.Id, Side.Buy);
    }

    [Fact]
    public void GeneratedTradeContext_OrderIntentSurvivesWorldStateCommit()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new GeneratedTradeOrderIntentStrategy();
        strategy.Initialize(runtime);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var trade = new TradeOccurred(
            instrument,
            new Trade(new Price(101m, Currency.USD), new Qty(5m), Side.Buy, default));
        var (start, length) = runtime.BatchMap.GetInstrumentRange(instrument);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[8];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands, orderIntents: orderIntents);

        strategy.RunTradeGuarded(in market, ref portfolio, in trade, start, length);
        runtime.WorldState.CommitContext(strategy.Id, ref portfolio);

        AssertCommittedOrderIntent(runtime, strategy.Id, Side.Sell);
    }

    [Fact]
    public void GeneratedBookContext_OrderIntentSurvivesWorldStateCommit()
    {
        using var runtime = new RhodiumRuntime();
        var strategy = new GeneratedBookOrderIntentStrategy();
        strategy.Initialize(runtime);

        var instrument = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ);
        var book = new BookUpdated(
            instrument,
            new Book
            {
                Instrument = instrument,
                Time = default,
                Bids = [new Level(new Price(100m, Currency.USD), new Qty(20m))],
                Asks = [new Level(new Price(100.01m, Currency.USD), new Qty(10m))]
            });
        var (start, length) = runtime.BatchMap.GetInstrumentRange(instrument);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var orderIntents = new OrderIntent[8];
        var portfolio = runtime.WorldState.BuildContext(strategy.Id, null, default, CreateCounters(), commands, orderIntents: orderIntents);

        strategy.RunBookGuarded(in market, ref portfolio, in book, start, length);
        runtime.WorldState.CommitContext(strategy.Id, ref portfolio);

        AssertCommittedOrderIntent(runtime, strategy.Id, Side.Buy);
    }

    private static void AssertCommittedOrderIntent(RhodiumRuntime runtime, StrategyId strategyId, Side side)
    {
        var drained = new OrderIntent[8];
        var count = runtime.WorldState.DrainOrderIntents(strategyId, drained);
        Assert.Equal(1, count);
        Assert.Equal(strategyId, drained[0].StrategyId);
        Assert.Equal(0, drained[0].AssetId.VirtualIndex);
        Assert.Equal(side, drained[0].Side);
        Assert.Equal(OrderType.Market, drained[0].Execution.OrderType);
    }

    private static int[] CreateCounters() => new int[PortfolioContext.CounterCount];

    private sealed class TestStrategy : Strategy
    {
        public bool OnInitializeCalled { get; private set; }
        public bool OnTickCalled { get; private set; }
        public AssetId Spy { get; private set; }
        public AssetId SpyVariant { get; private set; }
        public double CloseRead { get; private set; }

        protected override void OnInitialize(in SetupContext setup)
        {
            OnInitializeCalled = true;
            Spy = setup.AddEquity("SPY");
            SpyVariant = setup.AddEquity("SPY", 1);
        }

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
            OnTickCalled = true;
            CloseRead = market.GetClose(Spy);
        }
    }

    private sealed class AllocatingStrategy : Strategy
    {
        private static object? s_sink;

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
            s_sink = new byte[1];
        }
    }

    private sealed class AllocatingExecutionStrategy : Strategy
    {
        private static object? s_sink;

        protected override void OnOrderAccepted(ref OrderContext order)
        {
            s_sink = new byte[1];
        }
    }

    private sealed class AllocatingLifecycleStrategy : Strategy
    {
        private static object? s_sink;

        protected override void OnStart(ref LifecycleContext lifecycle)
        {
            s_sink = new byte[1];
        }
    }

}

internal sealed partial class WritableSignalStrategy : Strategy
{
    public static readonly VectorField<FactorF64> SignalField = new("Signal");

    [BarField]
    public partial double Signal { get; set; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnBar(ref BarContext bar)
        => bar.Signal = 42d;
}

internal sealed partial class RegisteredAssetCountingStrategy : Strategy
{
    public int VisitCount { get; private set; }

    [BarField]
    public partial double Signal { get; set; }

    protected override void OnInitialize(in SetupContext setup)
    {
        setup.AddEquity("SPY");
        setup.AddEquity("MSFT");
    }

    partial void OnBar(ref BarContext bar)
    {
        VisitCount++;
        bar.Signal = VisitCount;
    }
}

internal sealed partial class IndicatorRegistrationStrategy : Strategy
{
    [BarField(Name = "RSI_14", ReadOnly = true)]
    public partial double Rsi { get; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnBar(ref BarContext bar)
    {
    }
}

internal sealed partial class GeneratedOrderIntentStrategy : Strategy
{
    [BarField]
    public partial double Signal { get; set; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnBar(ref BarContext bar)
        => bar.Buy(new Qty(1m), Execution.Limit().AtBid().WithPostOnly());
}

internal sealed partial class GeneratedOrderAndGroupStrategy : Strategy
{
    [BarField]
    public partial double Signal { get; set; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnBar(ref BarContext bar)
        => bar.Buy(new Qty(1m), Execution.Limit().AtBid());

    protected override void OnGroup(ref GroupContext group)
        => group.SetAllocation(group.ChildIds[0], 0.5m);
}

internal sealed partial class GeneratedTickOrderIntentStrategy : Strategy
{
    [TickField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnTick(ref TickContext tick)
        => tick.Buy(new Qty(1m), Execution.Market());
}

internal sealed partial class GeneratedQuoteOrderIntentStrategy : Strategy
{
    [QuoteField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnQuote(ref QuoteContext quote)
        => quote.Buy(new Qty(1m), Execution.Market());
}

internal sealed partial class GeneratedTradeOrderIntentStrategy : Strategy
{
    [TradeField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnTrade(ref TradeContext trade)
        => trade.Sell(new Qty(1m), Execution.Market());
}

internal sealed partial class GeneratedBookOrderIntentStrategy : Strategy
{
    [BookField(ReadOnly = true)]
    public partial double Close { get; }

    protected override void OnInitialize(in SetupContext setup)
        => setup.AddEquity("SPY");

    partial void OnBook(ref BookContext book)
        => book.Buy(new Qty(1m), Execution.Market());
}
