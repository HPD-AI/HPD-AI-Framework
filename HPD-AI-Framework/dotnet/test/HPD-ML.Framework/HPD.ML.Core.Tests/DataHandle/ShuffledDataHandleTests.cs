using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ShuffledDataHandleTests
{
    [Fact]
    public void Ordering_IsUnordered()
    {
        var shuffled = new ShuffledDataHandle(TestHelpers.CreateSimpleHandle(), seed: 42);
        Assert.Equal(OrderingPolicy.Unordered, shuffled.Ordering);
    }

    [Fact]
    public void RowCount_PreservedFromSource()
    {
        var shuffled = new ShuffledDataHandle(TestHelpers.CreateSimpleHandle(10), seed: 42);
        Assert.Equal(10L, shuffled.RowCount);
    }

    [Fact]
    public void Seed_ProducesDeterministicOrder()
    {
        var source = TestHelpers.CreateSimpleHandle(20);
        var a = TestHelpers.CollectIntColumn(new ShuffledDataHandle(source, seed: 42), "Id");
        var b = TestHelpers.CollectIntColumn(new ShuffledDataHandle(source, seed: 42), "Id");

        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentOrders()
    {
        var source = TestHelpers.CreateSimpleHandle(100);
        var a = TestHelpers.CollectIntColumn(new ShuffledDataHandle(source, seed: 42), "Id");
        var b = TestHelpers.CollectIntColumn(new ShuffledDataHandle(source, seed: 99), "Id");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AllRowsPresent_NoDuplicates()
    {
        var source = TestHelpers.CreateSimpleHandle(10);
        var shuffled = new ShuffledDataHandle(source, seed: 42);
        var ids = TestHelpers.CollectIntColumn(shuffled, "Id");

        Assert.Equal(10, ids.Count);
        Assert.Equal(10, ids.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 10).ToHashSet(), ids.ToHashSet());
    }
}
