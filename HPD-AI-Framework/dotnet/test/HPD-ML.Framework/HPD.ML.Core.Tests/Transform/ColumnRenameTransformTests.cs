using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ColumnRenameTransformTests
{
    [Fact]
    public void GetOutputSchema_RenamesColumn()
    {
        var source = TestHelpers.CreateSimpleHandle();
        var transform = new ColumnRenameTransform("Id", "Identifier");
        var schema = transform.GetOutputSchema(source.Schema);

        Assert.NotNull(schema.FindByName("Identifier"));
        Assert.Null(schema.FindByName("Id"));
    }

    [Fact]
    public void Apply_DataAccessibleByNewName()
    {
        var source = InMemoryDataHandle.FromColumns(("Age", new int[] { 25, 30 }));
        var transform = new ColumnRenameTransform("Age", "Years");
        var result = transform.Apply(source);

        var values = TestHelpers.CollectIntColumn(result, "Years");
        Assert.Equal([25, 30], values);
    }

    [Fact]
    public void Apply_OldNameNotInSchema()
    {
        var source = InMemoryDataHandle.FromColumns(("Age", new int[] { 25 }));
        var transform = new ColumnRenameTransform("Age", "Years");
        var result = transform.Apply(source);

        // The schema no longer contains the old name
        Assert.Null(result.Schema.FindByName("Age"));
        Assert.NotNull(result.Schema.FindByName("Years"));
    }

    [Fact]
    public void MultipleRenames()
    {
        var source = TestHelpers.CreateThreeColumnHandle();
        var renames = new Dictionary<string, string> { ["A"] = "X", ["B"] = "Y" };
        var transform = new ColumnRenameTransform(renames);
        var schema = transform.GetOutputSchema(source.Schema);

        Assert.NotNull(schema.FindByName("X"));
        Assert.NotNull(schema.FindByName("Y"));
        Assert.NotNull(schema.FindByName("C")); // unchanged
        Assert.Null(schema.FindByName("A"));
        Assert.Null(schema.FindByName("B"));
    }

    [Fact]
    public void SingleArgConstructor()
    {
        var transform = new ColumnRenameTransform("old", "new_name");
        var source = InMemoryDataHandle.FromColumns(("old", new int[] { 1 }));
        var schema = transform.GetOutputSchema(source.Schema);

        Assert.NotNull(schema.FindByName("new_name"));
    }
}
