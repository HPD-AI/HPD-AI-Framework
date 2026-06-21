using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ConcatenatedDataHandleTests
{
    [Fact]
    public void RowCount_SumsAllSources()
    {
        var a = TestHelpers.CreateSimpleHandle(3);
        var b = TestHelpers.CreateSimpleHandle(5);
        var concat = new ConcatenatedDataHandle(a, b);

        Assert.Equal(8L, concat.RowCount);
    }

    [Fact]
    public void GetCursor_YieldsAllRowsInOrder()
    {
        var a = InMemoryDataHandle.FromColumns(("Id", new int[] { 1, 2 }));
        var b = InMemoryDataHandle.FromColumns(("Id", new int[] { 3, 4, 5 }));
        var concat = new ConcatenatedDataHandle(a, b);

        var ids = TestHelpers.CollectIntColumn(concat, "Id");
        Assert.Equal([1, 2, 3, 4, 5], ids);
    }

    [Fact]
    public void Schema_MismatchedColumns_Throws()
    {
        var a = InMemoryDataHandle.FromColumns(("Id", new int[] { 1 }));
        var b = InMemoryDataHandle.FromColumns(("Name", new string[] { "x" }));

        Assert.Throws<InvalidOperationException>(() => new ConcatenatedDataHandle(a, b));
    }

    [Fact]
    public void EmptySources_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConcatenatedDataHandle(ReadOnlySpan<IDataHandle>.Empty));
    }

    [Fact]
    public async Task StreamRows_ConcatenatesAsync()
    {
        var a = InMemoryDataHandle.FromColumns(("Id", new int[] { 1, 2 }));
        var b = InMemoryDataHandle.FromColumns(("Id", new int[] { 3 }));
        var concat = new ConcatenatedDataHandle(a, b);

        var ids = new List<int>();
        await foreach (var row in concat.StreamRows())
            ids.Add(row.GetValue<int>("Id"));
        Assert.Equal([1, 2, 3], ids);
    }

    [Fact]
    public void Materialize_ProducesCorrectInMemory()
    {
        var a = InMemoryDataHandle.FromColumns(("Id", new int[] { 1, 2 }));
        var b = InMemoryDataHandle.FromColumns(("Id", new int[] { 3, 4, 5 }));
        var concat = new ConcatenatedDataHandle(a, b);

        var materialized = concat.Materialize();
        Assert.Equal(5L, materialized.RowCount);
        var ids = TestHelpers.CollectIntColumn(materialized, "Id");
        Assert.Equal([1, 2, 3, 4, 5], ids);
    }
}
