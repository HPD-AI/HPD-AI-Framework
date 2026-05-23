using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public sealed class QueueExecutionModel : SimulationExecutionModelBase
{
    public override void OnMarketEvent(FinanceEvent evt, in MarketKernel market)
        => FillPendingTouchedOrders(evt);

    public override void Submit(in OrderIntent intent, in MarketKernel market)
    {
        var order = CreateOrder(in intent);
        if (RejectIfMarketNotOpen(in order))
            return;

        Accept(in order);

        Price? limitPrice = null;
        if (intent.Execution.OrderType != OrderType.Market && TryResolveLimitPrice(in order, in market, out var resolved))
            limitPrice = resolved;

        AddPending(in order, limitPrice);
    }
}
