using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ColumnCopyTransformTests
{
    [Fact]
    public void GetOutputSchema_AddsNewColumn()
    {
        var source = TestHelpers.CreateSimpleHandle();
        var transform = new ColumnCopyTransform("Id", "Id_copy");
        var schema = transform.GetOutputSchema(source.Schema);

        Assert.NotNull(schema.FindByName("Id"));
        Assert.NotNull(schema.FindByName("Id_copy"));
        Assert.Equal(source.Schema.Columns.Count + 1, schema.Columns.Count);
    }

    [Fact]
    public void Apply_CopiedColumnHasSameValues()
    {
        var source = InMemoryDataHandle.FromColumns(("Id", new int[] { 1, 2, 3 }));
        var transform = new ColumnCopyTransform("Id", "Id_copy");
        var result = transform.Apply(source);

        var originals = TestHelpers.CollectIntColumn(result, "Id");
        var copies = TestHelpers.CollectIntColumn(result, "Id_copy");
        Assert.Equal(originals, copies);
    }

    [Fact]
    public void CopyNonExistentColumn_Throws()
    {
        var source = TestHelpers.CreateSimpleHandle();
        var transform = new ColumnCopyTransform("Bogus", "Copy");

        Assert.Throws<InvalidOperationException>(
            () => transform.GetOutputSchema(source.Schema));
    }

    [Fact]
    public void CopiedColumn_InheritsAnnotations()
    {
        var annotations = AnnotationSet.Empty.With("role:Label", true);
        var schema = new Schema([new Column("Label", FieldType.Scalar<float>(), annotations)]);
        var handle = new InMemoryDataHandle(schema, new Dictionary<string, Array>
        {
            ["Label"] = new float[] { 1f },
        });

        var transform = new ColumnCopyTransform("Label", "Label_copy");
        var outSchema = transform.GetOutputSchema(handle.Schema);

        var copy = outSchema.FindByName("Label_copy");
        Assert.NotNull(copy);
        Assert.True(copy.Annotations.TryGetValue<bool>("role:Label", out var v));
        Assert.True(v);
    }
}
