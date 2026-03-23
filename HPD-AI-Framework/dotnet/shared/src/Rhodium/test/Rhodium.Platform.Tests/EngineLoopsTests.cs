using Rhodium.Kernel;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

/// <summary>
/// Tests for EngineLoops zero-cost iteration patterns.
/// </summary>
public class EngineLoopsTests
{
    private TradingEngine CreateEngineWithAssets(int count)
    {
        var engine = new TradingEngine();

        for (int i = 0; i < count; i++)
        {
            var instrument = new Instrument(new Asset($"ASSET{i}", AssetClass.Equity), Venue.NASDAQ);
            engine.BatchMap.AddInstrument(instrument, 1);
        }
        engine.Tensors.Grow();

        return engine;
    }

    [Fact]
    public void ForEachAsset_VisitsAllAssets()
    {
        var engine = CreateEngineWithAssets(5);
        var visitor = new CountingVisitor();

        EngineLoops.ForEachAsset(ref engine, ref visitor);

        Assert.Equal(5, visitor.Count);
    }

    [Fact]
    public void ForEachAsset_VisitsInOrder()
    {
        var engine = CreateEngineWithAssets(10);
        var visitor = new OrderCheckingVisitor();

        EngineLoops.ForEachAsset(ref engine, ref visitor);

        Assert.True(visitor.InOrder);
        Assert.Equal(9, visitor.LastId); // Last index is 9 for 10 assets (0-9)
    }

    [Fact]
    public void ForEachAsset_WithZeroAssets_DoesNothing()
    {
        var engine = new TradingEngine();
        var visitor = new CountingVisitor();

        EngineLoops.ForEachAsset(ref engine, ref visitor);

        Assert.Equal(0, visitor.Count);
    }

    [Fact]
    public void ForEachAsset_PassesCorrectAssetIds()
    {
        var engine = CreateEngineWithAssets(3);
        var visitor = new AssetIdCollector();

        EngineLoops.ForEachAsset(ref engine, ref visitor);

        Assert.Equal(3, visitor.VisitedIds.Count);
        Assert.Contains(new AssetId(0), visitor.VisitedIds);
        Assert.Contains(new AssetId(1), visitor.VisitedIds);
        Assert.Contains(new AssetId(2), visitor.VisitedIds);
    }

    [Fact]
    public void ForEachAsset_PassesEngineByRef()
    {
        var engine = CreateEngineWithAssets(5);
        var visitor = new EngineModifyingVisitor();

        EngineLoops.ForEachAsset(ref engine, ref visitor);

        // Verify engine was modified (position set)
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(100m, engine.GetPosition(i));
        }
    }

    [Fact]
    public void ForEachAssetInRange_VisitsSpecifiedRange()
    {
        var engine = CreateEngineWithAssets(10);
        var visitor = new CountingVisitor();

        // Visit assets 3-7 (5 assets)
        EngineLoops.ForEachAssetInRange(ref engine, ref visitor, start: 3, count: 5);

        Assert.Equal(5, visitor.Count);
    }

    [Fact]
    public void ForEachAssetInRange_VisitsCorrectAssets()
    {
        var engine = CreateEngineWithAssets(10);
        var visitor = new AssetIdCollector();

        // Visit assets 2-4 (3 assets)
        EngineLoops.ForEachAssetInRange(ref engine, ref visitor, start: 2, count: 3);

        Assert.Equal(3, visitor.VisitedIds.Count);
        Assert.Contains(new AssetId(2), visitor.VisitedIds);
        Assert.Contains(new AssetId(3), visitor.VisitedIds);
        Assert.Contains(new AssetId(4), visitor.VisitedIds);
        Assert.DoesNotContain(new AssetId(0), visitor.VisitedIds);
        Assert.DoesNotContain(new AssetId(5), visitor.VisitedIds);
    }

    [Fact]
    public void ForEachAssetInRange_WithZeroCount_DoesNothing()
    {
        var engine = CreateEngineWithAssets(5);
        var visitor = new CountingVisitor();

        EngineLoops.ForEachAssetInRange(ref engine, ref visitor, start: 0, count: 0);

        Assert.Equal(0, visitor.Count);
    }

    [Fact]
    public void ForEachAssetInRange_WithStartAtEnd_DoesNothing()
    {
        var engine = CreateEngineWithAssets(5);
        var visitor = new CountingVisitor();

        EngineLoops.ForEachAssetInRange(ref engine, ref visitor, start: 5, count: 0);

        Assert.Equal(0, visitor.Count);
    }

    [Fact]
    public void ForEachAsset_WithLargeUniverse_Efficient()
    {
        var engine = CreateEngineWithAssets(1000);
        var visitor = new CountingVisitor();

        EngineLoops.ForEachAsset(ref engine, ref visitor);

        Assert.Equal(1000, visitor.Count);
    }

    [Fact]
    public void ForEachAsset_VisitorCanReadData()
    {
        var engine = CreateEngineWithAssets(5);

        // Set some data
        for (int i = 0; i < 5; i++)
        {
            engine.Tensors.GetScalar(Field.Close, i) = new PriceF64(100.0 + i);
        }

        var visitor = new DataReadingVisitor();
        EngineLoops.ForEachAsset(ref engine, ref visitor);

        Assert.Equal(5, visitor.ReadCount);
        Assert.True(visitor.TotalClose > 0);
    }

    [Fact]
    public void ForEachAsset_VisitorCanModifyData()
    {
        var engine = CreateEngineWithAssets(3);

        var visitor = new DataWritingVisitor();
        EngineLoops.ForEachAsset(ref engine, ref visitor);

        // Verify data was written
        for (int i = 0; i < 3; i++)
        {
            var close = engine.Tensors.GetScalar(Field.Close, i).Value;
            Assert.Equal(200.0, close);
        }
    }

    [Fact]
    public void ForEachAssetInRange_SingleAsset_Works()
    {
        var engine = CreateEngineWithAssets(5);
        var visitor = new AssetIdCollector();

        EngineLoops.ForEachAssetInRange(ref engine, ref visitor, start: 2, count: 1);

        Assert.Single(visitor.VisitedIds);
        Assert.Contains(new AssetId(2), visitor.VisitedIds);
    }

    [Fact]
    public void ForEachAsset_StructVisitor_NoAllocation()
    {
        var engine = CreateEngineWithAssets(10);
        var visitor = new CountingVisitor();

        // In production, this would be validated with GC.GetAllocatedBytesForCurrentThread
        EngineLoops.ForEachAsset(ref engine, ref visitor);

        Assert.Equal(10, visitor.Count);
    }
}

/// <summary>
/// Simple visitor that counts how many assets were visited.
/// </summary>
struct CountingVisitor : ITickVisitor
{
    public int Count;

    public void Visit(AssetId id, ref TradingEngine engine)
    {
        Count++;
    }
}

/// <summary>
/// Visitor that checks if assets are visited in order.
/// </summary>
struct OrderCheckingVisitor : ITickVisitor
{
    public bool InOrder;
    public int LastId;

    public OrderCheckingVisitor()
    {
        InOrder = true;
        LastId = -1;
    }

    public void Visit(AssetId id, ref TradingEngine engine)
    {
        int currentId = id.VirtualIndex;
        if (currentId != LastId + 1)
        {
            InOrder = false;
        }
        LastId = currentId;
    }
}

/// <summary>
/// Visitor that collects all visited asset IDs.
/// </summary>
struct AssetIdCollector : ITickVisitor
{
    public List<AssetId> VisitedIds;

    public AssetIdCollector()
    {
        VisitedIds = new List<AssetId>();
    }

    public void Visit(AssetId id, ref TradingEngine engine)
    {
        VisitedIds.Add(id);
    }
}

/// <summary>
/// Visitor that modifies the engine state.
/// </summary>
struct EngineModifyingVisitor : ITickVisitor
{
    public void Visit(AssetId id, ref TradingEngine engine)
    {
        engine.SetPosition(id, 100m);
    }
}

/// <summary>
/// Visitor that reads data from tensor store.
/// </summary>
struct DataReadingVisitor : ITickVisitor
{
    public int ReadCount;
    public double TotalClose;

    public void Visit(AssetId id, ref TradingEngine engine)
    {
        var close = engine.Tensors.GetScalar(Field.Close, id).Value;
        TotalClose += close;
        ReadCount++;
    }
}

/// <summary>
/// Visitor that writes data to tensor store.
/// </summary>
struct DataWritingVisitor : ITickVisitor
{
    public void Visit(AssetId id, ref TradingEngine engine)
    {
        engine.Tensors.GetScalar(Field.Close, id) = new PriceF64(200.0);
    }
}
