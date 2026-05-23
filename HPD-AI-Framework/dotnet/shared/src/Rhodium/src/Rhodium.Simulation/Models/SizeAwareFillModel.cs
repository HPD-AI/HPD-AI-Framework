using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Size-aware fill model - larger orders experience more slippage.
/// </summary>
public sealed class SizeAwareFillModel : IFillModel
{
    private readonly decimal _baseQty;  // Orders <= this size get best price
    private readonly decimal _slippagePerExtraLot;

    public SizeAwareFillModel(decimal baseQty = 10m, decimal slippageBpsPerLot = 0.5m)
    {
        _baseQty = baseQty;
        _slippagePerExtraLot = slippageBpsPerLot;
    }

    public bool ShouldFillLimit(ref FillModelContext ctx)
    {
        // Use default logic
        var defaultModel = new DefaultFillModel();
        return defaultModel.ShouldFillLimit(ref ctx);
    }

    public Price AdjustFillPrice(ref FillModelContext ctx)
    {
        if (ctx.OrderQty.Value <= _baseQty)
            return ctx.NominalFillPrice;

        // Extra slippage for large orders
        var extraLots = ctx.OrderQty.Value - _baseQty;
        var slippageBps = extraLots * _slippagePerExtraLot;
        var slippageAmount = ctx.NominalFillPrice.Value * slippageBps / 10000m;

        var adjustedPrice = ctx.OrderSide == Side.Buy
            ? ctx.NominalFillPrice.Value + slippageAmount  // Buyers pay more
            : ctx.NominalFillPrice.Value - slippageAmount; // Sellers receive less

        return new Price(Math.Max(0, adjustedPrice), ctx.NominalFillPrice.Currency);
    }
}
