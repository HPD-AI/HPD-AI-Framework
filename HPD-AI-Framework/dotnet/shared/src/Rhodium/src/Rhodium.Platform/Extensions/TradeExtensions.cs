using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Extensions;

public static class TradeExtensions
{
    extension(ref PortfolioContext portfolio)
    {
        public void SetPosition(AssetId id, Qty targetQty, ExecutionSpec execution)
        {
            var current = portfolio.GetPositionQty(id);
            var delta = targetQty.Value - current;
            if (delta == 0m) return;

            if (delta > 0m)
                portfolio.Buy(id, new Qty(delta), execution);
            else
                portfolio.Sell(id, new Qty(Math.Abs(delta)), execution);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flatten(AssetId id, ExecutionSpec execution)
            => portfolio.SetPosition(id, Qty.Zero, execution);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CancelAll(AssetId id)
        {
            // Working-order state is part of PortfolioContext; concrete cancellation
            // routing lands with event/connector integration.
        }
    }
}
