using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class LambdaTransformTests
{
    [Fact]
    public void Apply_MapsEachRow()
    {
        var source = InMemoryDataHandle.FromColumns(("V", new int[] { 1, 2, 3 }));

        // Double every value — produce a new Row with doubled values
        var transform = new LambdaTransform(
            row =>
            {
                var val = row.GetValue<int>("V") * 2;
                var schema = row.Schema;
                var columns = new Dictionary<string, Array> { ["V"] = new int[] { val } };
                return new Row(schema, columns, 0);
            },
            schema => schema);

        var result = transform.Apply(source);
        var values = TestHelpers.CollectIntColumn(result, "V");
        Assert.Equal([2, 4, 6], values);
    }

    [Fact]
    public void GetOutputSchema_DelegatesToSchemaMapper()
    {
        var source = TestHelpers.CreateSimpleHandle();
        var transform = new LambdaTransform(_ => _, schema => schema);

        var output = transform.GetOutputSchema(source.Schema);
        Assert.Equal(source.Schema.Columns.Count, output.Columns.Count);
    }

    [Fact]
    public void Properties_PreservesRowCount()
    {
        var transform = new LambdaTransform(_ => _, s => s);
        Assert.True(transform.Properties.PreservesRowCount);
    }
}
