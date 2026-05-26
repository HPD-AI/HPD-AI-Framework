using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

public class TradeExtensionsTests
{
    [Fact]
    public void Buy_SubmitsMarketOrderIntent()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        Span<OrderIntent> intents = new OrderIntent[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands, orderIntents: intents);

        portfolio.Buy(new AssetId(0), new Qty(10m), Execution.Market());

        var intent = Assert.Single(portfolio.DrainOrderIntents().ToArray());
        Assert.Equal(Side.Buy, intent.Side);
        Assert.Equal(new Qty(10m), intent.Quantity);
        Assert.Equal(OrderType.Market, intent.Execution.OrderType);
    }

    [Fact]
    public void Sell_SubmitsMarketOrderIntent()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        Span<OrderIntent> intents = new OrderIntent[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands, orderIntents: intents);

        portfolio.Sell(new AssetId(0), new Qty(5m), Execution.Market());

        var intent = Assert.Single(portfolio.DrainOrderIntents().ToArray());
        Assert.Equal(Side.Sell, intent.Side);
        Assert.Equal(new Qty(5m), intent.Quantity);
        Assert.Equal(OrderType.Market, intent.Execution.OrderType);
    }

    [Fact]
    public void SetPosition_SubmitsDeltaIntentFromProjectedPosition()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        Span<OrderIntent> intents = new OrderIntent[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands, orderIntents: intents);

        ApplyTestFill(runtime, new StrategyId(1), new AssetId(0), Side.Buy, new Qty(12m), in market);
        portfolio.SetPosition(new AssetId(0), new Qty(3m), Execution.Market());

        var intent = Assert.Single(portfolio.DrainOrderIntents().ToArray());
        Assert.Equal(Side.Sell, intent.Side);
        Assert.Equal(new Qty(9m), intent.Quantity);
        Assert.Equal(12m, portfolio.GetPositionQty(new AssetId(0)));
    }

    [Fact]
    public void Flatten_SubmitsClosingIntent()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        Span<OrderIntent> intents = new OrderIntent[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands, orderIntents: intents);

        ApplyTestFill(runtime, new StrategyId(1), new AssetId(0), Side.Buy, new Qty(10m), in market);
        portfolio.Flatten(new AssetId(0), Execution.Market());

        var intent = Assert.Single(portfolio.DrainOrderIntents().ToArray());
        Assert.Equal(Side.Sell, intent.Side);
        Assert.Equal(new Qty(10m), intent.Quantity);
        Assert.Equal(10m, portfolio.GetPositionQty(new AssetId(0)));
    }

    private static RhodiumRuntime CreateRuntimeWithClose(double close)
    {
        var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        runtime.BatchMap.AddInstrument(inst);
        runtime.Tensors.Grow();
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(close);
        runtime.SetContract(0, Contracts.Equity("TEST", Venue.NASDAQ, Currency.USD));
        return runtime;
    }

    private static int[] CreateCounters() => new int[PortfolioContext.CounterCount];

    private static void ApplyTestFill(
        RhodiumRuntime runtime,
        StrategyId strategyId,
        AssetId assetId,
        Side side,
        Qty quantity,
        in MarketKernel market)
    {
        var price = new Price((decimal)market.GetScalar(Field.Close, assetId), market.GetQuoteCurrency(assetId));
        runtime.WorldState.PositionAt(strategyId, assetId.VirtualIndex)
            .ApplyFill(market.GetContract(assetId), side, quantity, price, Money.Zero(price.Currency));
    }
}
