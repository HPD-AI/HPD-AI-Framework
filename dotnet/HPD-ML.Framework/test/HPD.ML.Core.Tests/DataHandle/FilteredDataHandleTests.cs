using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class FilteredDataHandleTests
{
    [Fact]
    public void RowCount_AlwaysNull()
    {
        var filtered = new FilteredDataHandle(TestHelpers.CreateSimpleHandle(), _ => true);
        Assert.Null(filtered.RowCount);
    }

    [Fact]
    public void GetCursor_OnlyYieldsMatchingRows()
    {
        var handle = TestHelpers.CreateSimpleHandle(5); // Ids: 0,1,2,3,4
        var filtered = new FilteredDataHandle(handle, row => row.GetValue<int>("Id") > 2);

        var ids = TestHelpers.CollectIntColumn(filtered, "Id");
        Assert.Equal([3, 4], ids);
    }

    [Fact]
    public void Materialize_OnlyContainsMatchingRows()
    {
        var handle = TestHelpers.CreateSimpleHandle(5);
        var filtered = new FilteredDataHandle(handle, row => row.GetValue<int>("Id") < 3);
        var materialized = filtered.Materialize();

        Assert.Equal(3L, materialized.RowCount);
    }

    [Fact]
    public async Task StreamRows_FiltersAsyncStream()
    {
        var handle = TestHelpers.CreateSimpleHandle(5);
        var filtered = new FilteredDataHandle(handle, row => row.GetValue<int>("Id") % 2 == 0);

        var count = 0;
        await foreach (var _ in filtered.StreamRows())
            count++;
        Assert.Equal(3, count); // 0, 2, 4
    }

    [Fact]
    public void Predicate_MatchesNothing_EmptyResult()
    {
        var handle = TestHelpers.CreateSimpleHandle(5);
        var filtered = new FilteredDataHandle(handle, _ => false);

        var ids = TestHelpers.CollectIntColumn(filtered, "Id");
        Assert.Empty(ids);
    }

    [Fact]
    public void Predicate_MatchesEverything_AllRows()
    {
        var handle = TestHelpers.CreateSimpleHandle(5);
        var filtered = new FilteredDataHandle(handle, _ => true);

        var ids = TestHelpers.CollectIntColumn(filtered, "Id");
        Assert.Equal(5, ids.Count);
    }
}
