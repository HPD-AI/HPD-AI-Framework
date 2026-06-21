using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class TransformedDataHandleTests
{
    [Fact]
    public void Schema_ComputedEagerlyFromTransform()
    {
        var source = TestHelpers.CreateThreeColumnHandle(); // A, B, C
        var transform = ColumnSelectTransform.Keep("A", "C");
        var transformed = new TransformedDataHandle(source, transform);

        Assert.Equal(2, transformed.Schema.Columns.Count);
        Assert.NotNull(transformed.Schema.FindByName("A"));
        Assert.Null(transformed.Schema.FindByName("B"));
        Assert.NotNull(transformed.Schema.FindByName("C"));
    }

    [Fact]
    public void RowCount_PreservesRowCount_WhenTransformPreserves()
    {
        var source = TestHelpers.CreateSimpleHandle(5);
        var transform = ColumnSelectTransform.Keep("Id");
        var transformed = new TransformedDataHandle(source, transform);

        Assert.Equal(5L, transformed.RowCount);
    }

    [Fact]
    public void GetCursor_AppliesTransform()
    {
        var source = TestHelpers.CreateThreeColumnHandle(3); // A: [0,1,2]
        var transform = ColumnSelectTransform.Keep("A");
        var transformed = new TransformedDataHandle(source, transform);

        var values = TestHelpers.CollectIntColumn(transformed, "A");
        Assert.Equal([0, 1, 2], values);
    }
}
