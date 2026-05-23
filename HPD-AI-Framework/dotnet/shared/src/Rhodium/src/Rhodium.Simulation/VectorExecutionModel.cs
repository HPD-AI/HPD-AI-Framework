using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public sealed class VectorExecutionModel : SimulationExecutionModelBase
{
    private FinanceEvent? _currentEvent;

    public override void OnMarketEvent(FinanceEvent evt, in MarketKernel market)
    {
        _currentEvent = evt;
        FillPendingTouchedOrders(evt);
    }

    public override void Submit(in OrderIntent intent, in MarketKernel market)
    {
        var order = CreateOrder(in intent);
        if (RejectIfMarketNotOpen(in order))
            return;

        Accept(in order);

        Price? limitPrice = null;
        if (intent.Execution.OrderType != OrderType.Market && TryResolveLimitPrice(in order, in market, out var resolved))
            limitPrice = resolved;

        if (_currentEvent is not null
            && ShouldFillOnEvent(
                in order,
                limitPrice,
                stopTriggeredBeforeEvent: false,
                _currentEvent,
                out var fillPrice,
                out _))
        {
            Fill(in order, intent.Quantity, fillPrice, isMaker: intent.Execution.OrderType != OrderType.Market);
            return;
        }

        if (intent.Execution.OrderType == OrderType.Market)
        {
            Reject(in order, "No market price available for vector fill.");
            return;
        }

        AddPending(in order, limitPrice);
    }
}
