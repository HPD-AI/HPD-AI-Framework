using System.Runtime.CompilerServices;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Extensions;

[Flags]
public enum ExecutionPolicy : byte
{
    Raw = 0,
    Idempotent = 1 << 0,
    RiskCheck = 1 << 1,
    Safe = Idempotent | RiskCheck
}

public static class TradeExtensions
{
    extension(ref PortfolioContext portfolio)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Buy(AssetId id, Qty quantity, in MarketKernel market, ExecutionPolicy policy = ExecutionPolicy.Safe)
            => portfolio.Buy(id, quantity, in market);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Sell(AssetId id, Qty quantity, in MarketKernel market, ExecutionPolicy policy = ExecutionPolicy.Safe)
            => portfolio.Sell(id, quantity, in market);

        public void SetPosition(AssetId id, Qty targetQty, in MarketKernel market, ExecutionPolicy policy = ExecutionPolicy.Safe)
        {
            var current = portfolio.GetPositionQty(id);
            var delta = targetQty.Value - current;
            if (delta == 0m) return;

            if (delta > 0m)
                portfolio.Buy(id, new Qty(delta), in market);
            else
                portfolio.Sell(id, new Qty(Math.Abs(delta)), in market);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Flatten(AssetId id, in MarketKernel market)
            => portfolio.Flatten(id, in market);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CancelAll(AssetId id)
        {
            // Working-order state is part of PortfolioContext; concrete cancellation
            // routing lands with event/connector integration.
        }
    }
}
