using Rhodium.HFT;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Kernel for updating queue positions when trades occur.
/// Implements probabilistic queue advancement based on configured queue model.
/// </summary>
public readonly struct QueueAdvancementKernel : IComputeKernel
{
    private readonly QueueParams _params;
    private readonly IHftDepth _depth;
    private readonly Trade _trade;

    public QueueAdvancementKernel(QueueParams queueParams, IHftDepth depth, Trade trade)
    {
        _params = queueParams;
        _depth = depth;
        _trade = trade;
    }

    public void Execute(ITensorStore store, int pageIndex)
    {
        var queueAhead = store.GetPage(SimField.QueueAheadQty, pageIndex);
        var queuePos = store.GetPage(SimField.QueueRelativePosition, pageIndex);
        var buyOrderTicks = store.GetPage(SimField.BuyOrderPriceTick, pageIndex);
        var sellOrderTicks = store.GetPage(SimField.SellOrderPriceTick, pageIndex);

        for (int i = 0; i < queueAhead.Length; i++)
        {
            long orderTick = buyOrderTicks[i].Value > 0
                ? (long)buyOrderTicks[i].Value
                : (long)sellOrderTicks[i].Value;

            if (orderTick == 0) continue;

            // Trade price in ticks
            var tradePriceTick = _trade.PriceTick(_depth.TickSize).Ticks;

            if (tradePriceTick == orderTick)
            {
                // Determine side based on which order tick field is active
                var side = buyOrderTicks[i].Value > 0 ? Side.Buy : Side.Sell;
                long totalQtyAtTick = (long)_depth.QtyAtTick(side, orderTick);
                long tradeQty = (long)_trade.Size.Value;

                double newQtyAhead = AdvanceQueue(
                    queueAhead[i].Value,
                    tradeQty,
                    totalQtyAtTick,
                    queuePos[i].Value,
                    _params);

                queueAhead[i] = new SizeF64(newQtyAhead);
                queuePos[i] = new FactorF64(totalQtyAtTick > 0 ? newQtyAhead / totalQtyAtTick : 0.0);
            }
        }
    }

    private static double AdvanceQueue(
        double qtyAhead,
        long tradeQty,
        long totalQty,
        double relativePos,
        QueueParams @params)
    {
        return @params.Model switch
        {
            QueueModelType.AlwaysFront => 0.0,

            QueueModelType.RiskAverse =>
                Math.Max(0, qtyAhead - tradeQty),

            QueueModelType.PowerProbabilistic =>
                PowerProbabilisticAdvancement(qtyAhead, tradeQty, relativePos, @params.Alpha),

            QueueModelType.PowerProbabilistic2 =>
                PowerProbabilistic2Advancement(qtyAhead, tradeQty, relativePos, @params.Alpha1, @params.Alpha2, @params.Transition),

            QueueModelType.PowerProbabilistic3 =>
                PowerProbabilistic3Advancement(qtyAhead, tradeQty, relativePos, @params.Alpha),

            QueueModelType.LogProbabilistic =>
                LogProbabilisticAdvancement(qtyAhead, tradeQty, relativePos, @params.Scale),

            QueueModelType.LogProbabilistic2 =>
                LogProbabilistic2Advancement(qtyAhead, tradeQty, relativePos, @params.Scale),

            _ => throw new ArgumentException($"Unknown queue model: {@params.Model}")
        };
    }

    private static double PowerProbabilisticAdvancement(double qtyAhead, long tradeQty, double relativePos, double alpha)
    {
        var probBefore = Math.Pow(relativePos, alpha);
        var qtyBefore = tradeQty * probBefore;
        return Math.Max(0, qtyAhead - qtyBefore);
    }

    private static double PowerProbabilistic2Advancement(
        double qtyAhead,
        long tradeQty,
        double relativePos,
        double alpha1,
        double alpha2,
        double transition)
    {
        double probBefore = relativePos < transition
            ? Math.Pow(relativePos / transition, alpha1) * transition
            : transition + Math.Pow((relativePos - transition) / (1 - transition), alpha2) * (1 - transition);

        var qtyBefore = tradeQty * probBefore;
        return Math.Max(0, qtyAhead - qtyBefore);
    }

    private static double PowerProbabilistic3Advancement(double qtyAhead, long tradeQty, double relativePos, double alpha)
    {
        var centered = Math.Abs(relativePos - 0.5) * 2;
        var curve = 1 - Math.Pow(centered, alpha);
        var probBefore = relativePos * curve;
        var qtyBefore = tradeQty * probBefore;
        return Math.Max(0, qtyAhead - qtyBefore);
    }

    private static double LogProbabilisticAdvancement(double qtyAhead, long tradeQty, double relativePos, double scale)
    {
        var probBefore = Math.Log(1 + scale * relativePos) / Math.Log(1 + scale);
        var qtyBefore = tradeQty * probBefore;
        return Math.Max(0, qtyAhead - qtyBefore);
    }

    private static double LogProbabilistic2Advancement(double qtyAhead, long tradeQty, double relativePos, double scale)
    {
        var scale2 = scale * 2;
        var probBefore = Math.Log(1 + scale2 * relativePos) / Math.Log(1 + scale2);
        var qtyBefore = tradeQty * probBefore;
        return Math.Max(0, qtyAhead - qtyBefore);
    }
}
