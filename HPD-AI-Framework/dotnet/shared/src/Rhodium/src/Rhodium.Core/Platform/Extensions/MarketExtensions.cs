using System.Runtime.CompilerServices;
using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Extensions;

public static class MarketExtensions
{
    extension(in MarketKernel market)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetBid(AssetId id, out long ticks)
        {
            var value = market.GetBestBidTick(id);
            ticks = value.GetValueOrDefault();
            return value.HasValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAsk(AssetId id, out long ticks)
        {
            var value = market.GetBestAskTick(id);
            ticks = value.GetValueOrDefault();
            return value.HasValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public decimal GetBidDepth(AssetId id)
        {
            var best = market.GetBestBidTick(id);
            return best.HasValue ? market.GetQtyAtTick(id, Side.Buy, best.Value) : 0m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public decimal GetAskDepth(AssetId id)
        {
            var best = market.GetBestAskTick(id);
            return best.HasValue ? market.GetQtyAtTick(id, Side.Sell, best.Value) : 0m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CopyBidLevels(AssetId id, Span<DepthLevel> destination)
            => market.CopyDepthLevels(id, Side.Buy, destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CopyAskLevels(AssetId id, Span<DepthLevel> destination)
            => market.CopyDepthLevels(id, Side.Sell, destination);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long GetSpreadTicks(AssetId id)
        {
            var bid = market.GetBestBidTick(id);
            var ask = market.GetBestAskTick(id);
            return bid.HasValue && ask.HasValue ? ask.Value - bid.Value : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long? GetMidTick(AssetId id)
        {
            var bid = market.GetBestBidTick(id);
            var ask = market.GetBestAskTick(id);
            return bid.HasValue && ask.HasValue ? (bid.Value + ask.Value) / 2 : null;
        }
    }
}
