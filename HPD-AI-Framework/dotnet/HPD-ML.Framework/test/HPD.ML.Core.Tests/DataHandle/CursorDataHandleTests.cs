using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class CursorDataHandleTests
{
    private static CursorDataHandle CreateFromInMemory(int rowCount = 3)
    {
        var inner = TestHelpers.CreateSimpleHandle(rowCount);
        return new CursorDataHandle(
            inner.Schema,
            columns => inner.GetCursor(columns),
            rowCount);
    }

    [Fact]
    public void Properties_CursorOnly()
    {
        var handle = CreateFromInMemory();

        Assert.Equal(MaterializationCapabilities.CursorOnly, handle.Capabilities);
        Assert.Equal(OrderingPolicy.Ordered, handle.Ordering);
    }

    [Fact]
    public void Materialize_ConvertsToDenseInMemory()
    {
        var handle = CreateFromInMemory(3);
        var materialized = handle.Materialize();

        Assert.IsType<InMemoryDataHandle>(materialized);
        Assert.Equal(3L, materialized.RowCount);
    }

    [Fact]
    public void GetCursor_DelegatesToFactory()
    {
        int callCount = 0;
        var inner = TestHelpers.CreateSimpleHandle(2);
        var handle = new CursorDataHandle(
            inner.Schema,
            columns => { callCount++; return inner.GetCursor(columns); },
            2);

        using var cursor = handle.GetCursor(["Id"]);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task StreamRows_YieldsAllRows()
    {
        var handle = CreateFromInMemory(3);
        var count = 0;
        await foreach (var _ in handle.StreamRows())
            count++;
        Assert.Equal(3, count);
    }

    [Fact]
    public void TryGetColumnBatch_AlwaysFalse()
    {
        var handle = CreateFromInMemory();
        Assert.False(handle.TryGetColumnBatch<float>("Value", 0, 1, out _));
    }
}
