using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;

namespace Rhodium.Platform;

public ref struct TimerContext
{
    private readonly MarketKernel _market;
    private PortfolioContextFrame _portfolio;

    internal TimerContext(in MarketKernel market, ref PortfolioContext portfolio, Scheduled scheduled)
    {
        _market = market;
        _portfolio = portfolio.AsFrame();
        Name = scheduled.Name;
    }

    public string Name { get; }

    public decimal GetPositionQty(AssetId id) => _portfolio.GetPositionQty(id);

    public void Buy(AssetId id, Qty quantity, ExecutionSpec execution)
        => _portfolio.Buy(id, quantity, execution);

    public void Sell(AssetId id, Qty quantity, ExecutionSpec execution)
        => _portfolio.Sell(id, quantity, execution);

    public void Flatten(AssetId id, ExecutionPolicy policy = ExecutionPolicy.Safe)
    {
        var market = _market;
        _portfolio.Flatten(id, in market);
    }
}
