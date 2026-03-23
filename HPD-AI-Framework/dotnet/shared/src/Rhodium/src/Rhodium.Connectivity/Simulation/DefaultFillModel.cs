using Rhodium.Primitives;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Default fill model (matches current §14 logic).
/// </summary>
public sealed class DefaultFillModel : IFillModel
{
    public bool ShouldFillLimit(ref FillContext ctx)
    {
        if (ctx.OrderSide == Side.Buy)
        {
            // Liquidity-taking: order crosses spread
            if (ctx.BestAskTick.HasValue && ctx.OrderPriceTick >= ctx.BestAskTick.Value)
                return true;

            // Passive fill: at best bid, front of queue
            if (ctx.BestBidTick.HasValue && ctx.OrderPriceTick == ctx.BestBidTick.Value)
                return ctx.QueueRelativePosition < 0.01; // Front 1%
        }
        else // Sell
        {
            if (ctx.BestBidTick.HasValue && ctx.OrderPriceTick <= ctx.BestBidTick.Value)
                return true;

            if (ctx.BestAskTick.HasValue && ctx.OrderPriceTick == ctx.BestAskTick.Value)
                return ctx.QueueRelativePosition < 0.01;
        }

        return false;
    }

    public Price AdjustFillPrice(ref FillContext ctx) => ctx.NominalFillPrice;
}
