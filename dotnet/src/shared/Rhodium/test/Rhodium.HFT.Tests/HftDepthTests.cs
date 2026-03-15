using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.HFT.Tests;

public class HftDepthTests
{
    private static readonly Instant Now = new(1000000000L);
    private const decimal TickSize = 0.01m;
    private const decimal LotSize = 0.001m;

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_InitializeWithCorrectProperties(Type depthType)
    {
        var depth = CreateDepth(depthType);

        Assert.Equal(TickSize, depth.TickSize);
        Assert.Equal(LotSize, depth.LotSize);
        Assert.Null(depth.BestBidTick);
        Assert.Null(depth.BestAskTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_UpdateBidSetsQuantity(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Buy, 10000, 100m, Now);

        Assert.Equal(100m, depth.QtyAtTick(Side.Buy, 10000));
        Assert.Equal(10000, depth.BestBidTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_UpdateAskSetsQuantity(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Sell, 10001, 50m, Now);

        Assert.Equal(50m, depth.QtyAtTick(Side.Sell, 10001));
        Assert.Equal(10001, depth.BestAskTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_ZeroQtyRemovesLevel(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Buy, 10000, 100m, Now);
        depth.Update(Side.Buy, 10000, 0m, Now);

        Assert.Equal(0m, depth.QtyAtTick(Side.Buy, 10000));
        Assert.Null(depth.BestBidTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_BestBidIsHighestPrice(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Buy, 9998, 10m, Now);
        depth.Update(Side.Buy, 10000, 20m, Now);
        depth.Update(Side.Buy, 9999, 15m, Now);

        Assert.Equal(10000, depth.BestBidTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_BestAskIsLowestPrice(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Sell, 10002, 10m, Now);
        depth.Update(Side.Sell, 10001, 20m, Now);
        depth.Update(Side.Sell, 10003, 15m, Now);

        Assert.Equal(10001, depth.BestAskTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_ClearBidsSide(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Buy, 10000, 100m, Now);
        depth.Update(Side.Sell, 10001, 50m, Now);
        depth.Clear(Side.Buy);

        Assert.Null(depth.BestBidTick);
        Assert.Equal(0m, depth.QtyAtTick(Side.Buy, 10000));
        Assert.Equal(10001, depth.BestAskTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_ClearAsksSide(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Buy, 10000, 100m, Now);
        depth.Update(Side.Sell, 10001, 50m, Now);
        depth.Clear(Side.Sell);

        Assert.Null(depth.BestAskTick);
        Assert.Equal(0m, depth.QtyAtTick(Side.Sell, 10001));
        Assert.Equal(10000, depth.BestBidTick);
    }

    [Theory]
    [InlineData(typeof(HashMapDepth))]
    [InlineData(typeof(BTreeDepth))]
    [InlineData(typeof(FusedDepth))]
    public void AllImplementations_ClearBothSides(Type depthType)
    {
        var depth = CreateDepth(depthType);

        depth.Update(Side.Buy, 10000, 100m, Now);
        depth.Update(Side.Sell, 10001, 50m, Now);
        depth.Clear();

        Assert.Null(depth.BestBidTick);
        Assert.Null(depth.BestAskTick);
    }

    [Fact]
    public void FusedDepth_RejectsStaleUpdates()
    {
        var depth = new FusedDepth(TickSize, LotSize);
        var laterTime = new Instant(2000000000L);

        depth.Update(Side.Buy, 10000, 100m, laterTime);
        depth.Update(Side.Buy, 10000, 50m, Now); // Stale - should be rejected

        Assert.Equal(100m, depth.QtyAtTick(Side.Buy, 10000));
    }

    [Fact]
    public void FusedDepth_AcceptsNewerUpdates()
    {
        var depth = new FusedDepth(TickSize, LotSize);
        var laterTime = new Instant(2000000000L);

        depth.Update(Side.Buy, 10000, 100m, Now);
        depth.Update(Side.Buy, 10000, 50m, laterTime); // Newer - should be accepted

        Assert.Equal(50m, depth.QtyAtTick(Side.Buy, 10000));
    }

    [Fact]
    public void RoiVectorDepth_HandlesRangeCorrectly()
    {
        var depth = new RoiVectorDepth(TickSize, LotSize, lowerBound: 10000, rangeSize: 100);

        depth.Update(Side.Buy, 10050, 100m, Now);
        Assert.Equal(100m, depth.QtyAtTick(Side.Buy, 10050));

        // Out of range - should be ignored
        depth.Update(Side.Buy, 5000, 50m, Now);
        Assert.Equal(0m, depth.QtyAtTick(Side.Buy, 5000));

        depth.Update(Side.Buy, 20000, 50m, Now);
        Assert.Equal(0m, depth.QtyAtTick(Side.Buy, 20000));
    }

    [Fact]
    public void RoiVectorDepth_BestBidScanBackward()
    {
        var depth = new RoiVectorDepth(TickSize, LotSize, lowerBound: 10000, rangeSize: 100);

        depth.Update(Side.Buy, 10010, 10m, Now);
        depth.Update(Side.Buy, 10050, 20m, Now);
        depth.Update(Side.Buy, 10030, 15m, Now);

        Assert.Equal(10050, depth.BestBidTick);
    }

    [Fact]
    public void RoiVectorDepth_BestAskScanForward()
    {
        var depth = new RoiVectorDepth(TickSize, LotSize, lowerBound: 10000, rangeSize: 100);

        depth.Update(Side.Sell, 10070, 10m, Now);
        depth.Update(Side.Sell, 10050, 20m, Now);
        depth.Update(Side.Sell, 10060, 15m, Now);

        Assert.Equal(10050, depth.BestAskTick);
    }

    [Fact]
    public void HashMapDepth_HandlesMultipleLevels()
    {
        var depth = new HashMapDepth(TickSize, LotSize);

        depth.Update(Side.Buy, 10000, 100m, Now);
        depth.Update(Side.Buy, 9999, 90m, Now);
        depth.Update(Side.Buy, 9998, 80m, Now);

        Assert.Equal(100m, depth.QtyAtTick(Side.Buy, 10000));
        Assert.Equal(90m, depth.QtyAtTick(Side.Buy, 9999));
        Assert.Equal(80m, depth.QtyAtTick(Side.Buy, 9998));
        Assert.Equal(10000, depth.BestBidTick);
    }

    [Fact]
    public void BTreeDepth_MaintainsSortedOrder()
    {
        var depth = new BTreeDepth(TickSize, LotSize);

        depth.Update(Side.Buy, 10000, 100m, Now);
        depth.Update(Side.Buy, 9995, 80m, Now);
        depth.Update(Side.Buy, 9998, 90m, Now);

        Assert.Equal(10000, depth.BestBidTick);

        depth.Update(Side.Sell, 10005, 50m, Now);
        depth.Update(Side.Sell, 10001, 30m, Now);
        depth.Update(Side.Sell, 10003, 40m, Now);

        Assert.Equal(10001, depth.BestAskTick);
    }

    private static IHftDepth CreateDepth(Type depthType)
    {
        if (depthType == typeof(HashMapDepth))
            return new HashMapDepth(TickSize, LotSize);
        if (depthType == typeof(BTreeDepth))
            return new BTreeDepth(TickSize, LotSize);
        if (depthType == typeof(FusedDepth))
            return new FusedDepth(TickSize, LotSize);
        if (depthType == typeof(RoiVectorDepth))
            return new RoiVectorDepth(TickSize, LotSize, lowerBound: 5000, rangeSize: 10000);

        throw new ArgumentException($"Unknown depth type: {depthType}");
    }
}
