using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ColumnSelectTransformTests
{
    [Fact]
    public void Keep_RetainsOnlyNamedColumns()
    {
        var source = TestHelpers.CreateThreeColumnHandle();
        var transform = ColumnSelectTransform.Keep("A", "C");
        var schema = transform.GetOutputSchema(source.Schema);

        Assert.Equal(2, schema.Columns.Count);
        Assert.NotNull(schema.FindByName("A"));
        Assert.NotNull(schema.FindByName("C"));
        Assert.Null(schema.FindByName("B"));
    }

    [Fact]
    public void Drop_RemovesNamedColumns()
    {
        var source = TestHelpers.CreateThreeColumnHandle();
        var transform = ColumnSelectTransform.Drop("B");
        var schema = transform.GetOutputSchema(source.Schema);

        Assert.Equal(2, schema.Columns.Count);
        Assert.Null(schema.FindByName("B"));
    }

    [Fact]
    public void Keep_PreservesColumnOrder()
    {
        var source = TestHelpers.CreateThreeColumnHandle();
        var transform = ColumnSelectTransform.Keep("C", "A"); // request reversed
        var schema = transform.GetOutputSchema(source.Schema);

        // Should follow input schema order: A, C
        Assert.Equal("A", schema.Columns[0].Name);
        Assert.Equal("C", schema.Columns[1].Name);
    }

    [Fact]
    public void Apply_CursorOnlyHasSelectedColumns()
    {
        var source = TestHelpers.CreateThreeColumnHandle(3);
        var transform = ColumnSelectTransform.Keep("A");
        var result = transform.Apply(source);

        Assert.Single(result.Schema.Columns);
        var values = TestHelpers.CollectIntColumn(result, "A");
        Assert.Equal([0, 1, 2], values);
    }
}
