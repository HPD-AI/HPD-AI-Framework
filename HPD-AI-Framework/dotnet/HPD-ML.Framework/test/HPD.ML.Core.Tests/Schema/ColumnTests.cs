using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class ColumnTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var annotations = AnnotationSet.Empty.With("key", "value");
        var col = new Column("Name", FieldType.Scalar<int>(), annotations, IsHidden: true);

        Assert.Equal("Name", col.Name);
        Assert.Equal(typeof(int), col.Type.ClrType);
        Assert.True(col.IsHidden);
        Assert.True(col.Annotations.TryGetValue<string>("key", out _));
    }

    [Fact]
    public void ShortConstructor_DefaultsAnnotationsAndHidden()
    {
        var col = new Column("Name", FieldType.Scalar<int>());

        Assert.Same(AnnotationSet.Empty, col.Annotations);
        Assert.False(col.IsHidden);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var a = new Column("X", FieldType.Scalar<int>(), AnnotationSet.Empty, false);
        var b = new Column("X", FieldType.Scalar<int>(), AnnotationSet.Empty, false);

        Assert.Equal(a, b);
    }
}
