using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;

namespace Rhodium.Platform.Tests;

public class MarketExtensionsTests
{
    [Fact]
    public void MarketExtensions_ReadTopOfBook()
    {
        using var runtime = new RhodiumRuntime();
        var depth = new HashMapDepth(0.01m, 1m);
        depth.Update(Side.Buy, 1000, 500m, Instant.Now);
        depth.Update(Side.Sell, 1005, 750m, Instant.Now);
        runtime.SetDepth(0, depth);

        var market = runtime.CreateMarketKernel();
        var id = new AssetId(0);

        Assert.True(market.TryGetBid(id, out var bid));
        Assert.True(market.TryGetAsk(id, out var ask));
        Assert.Equal(1000, bid);
        Assert.Equal(1005, ask);
        Assert.Equal(500m, market.GetBidDepth(id));
        Assert.Equal(750m, market.GetAskDepth(id));
        Assert.Equal(5, market.GetSpreadTicks(id));
        Assert.Equal(1002, market.GetMidTick(id));
    }

    [Fact]
    public void MarketExtensions_CopyDepthLevels()
    {
        using var runtime = new RhodiumRuntime();
        var depth = new HashMapDepth(0.01m, 1m);
        depth.Update(Side.Buy, 1000, 500m, Instant.Now);
        depth.Update(Side.Buy, 999, 250m, Instant.Now);
        depth.Update(Side.Sell, 1005, 750m, Instant.Now);
        depth.Update(Side.Sell, 1006, 125m, Instant.Now);
        runtime.SetDepth(0, depth);

        var market = runtime.CreateMarketKernel();
        var id = new AssetId(0);
        Span<DepthLevel> bids = stackalloc DepthLevel[4];
        Span<DepthLevel> asks = stackalloc DepthLevel[4];

        var bidCount = market.CopyBidLevels(id, bids);
        var askCount = market.CopyAskLevels(id, asks);

        Assert.Equal(2, bidCount);
        Assert.Equal(new DepthLevel(1000, 500m), bids[0]);
        Assert.Equal(new DepthLevel(999, 250m), bids[1]);
        Assert.Equal(2, askCount);
        Assert.Equal(new DepthLevel(1005, 750m), asks[0]);
        Assert.Equal(new DepthLevel(1006, 125m), asks[1]);
    }

    [Fact]
    public void MarketExtensions_ReturnDefaultsWhenDepthMissing()
    {
        using var runtime = new RhodiumRuntime();
        var market = runtime.CreateMarketKernel();
        var id = new AssetId(0);

        Assert.False(market.TryGetBid(id, out var bid));
        Assert.False(market.TryGetAsk(id, out var ask));
        Assert.Equal(0, bid);
        Assert.Equal(0, ask);
        Assert.Equal(0m, market.GetBidDepth(id));
        Assert.Equal(0m, market.GetAskDepth(id));
        Assert.Equal(0, market.GetSpreadTicks(id));
        Assert.Null(market.GetMidTick(id));

        Span<DepthLevel> levels = stackalloc DepthLevel[2];
        Assert.Equal(0, market.CopyBidLevels(id, levels));
        Assert.Equal(0, market.CopyAskLevels(id, levels));
    }
}
