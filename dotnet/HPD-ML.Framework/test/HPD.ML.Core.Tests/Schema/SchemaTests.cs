using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class SchemaTests
{
    private static Schema TwoColumnSchema(string a = "A", string b = "B")
        => new([new Column(a, FieldType.Scalar<int>()), new Column(b, FieldType.Scalar<float>())]);

    [Fact]
    public void Constructor_IndexesNonHiddenColumns()
    {
        var schema = new Schema([
            new Column("Visible", FieldType.Scalar<int>()),
            new Column("Hidden", FieldType.Scalar<int>(), AnnotationSet.Empty, IsHidden: true),
            new Column("Also Visible", FieldType.Scalar<float>()),
        ]);

        Assert.NotNull(schema.FindByName("Visible"));
        Assert.Null(schema.FindByName("Hidden"));
        Assert.NotNull(schema.FindByName("Also Visible"));
    }

    [Fact]
    public void Constructor_LastWriterWins_DuplicateNames()
    {
        var schema = new Schema([
            new Column("x", FieldType.Scalar<int>()),
            new Column("x", FieldType.Scalar<float>()),
        ]);

        var col = schema.FindByName("x");
        Assert.NotNull(col);
        Assert.Equal(typeof(float), col.Type.ClrType);
    }

    [Fact]
    public void FindByName_ExistingColumn_ReturnsColumn()
    {
        var schema = TwoColumnSchema();
        Assert.NotNull(schema.FindByName("A"));
    }

    [Fact]
    public void FindByName_MissingColumn_ReturnsNull()
    {
        var schema = TwoColumnSchema();
        Assert.Null(schema.FindByName("Missing"));
    }

    [Fact]
    public void FindByQualifiedName_MatchesNameColonType()
    {
        var schema = TwoColumnSchema();
        var col = schema.FindByQualifiedName("A:Int32");
        Assert.NotNull(col);
        Assert.Equal("A", col.Name);
    }

    [Fact]
    public void FindByQualifiedName_NoMatch_ReturnsNull()
    {
        var schema = TwoColumnSchema();
        Assert.Null(schema.FindByQualifiedName("Bogus:String"));
    }

    [Fact]
    public void MergeHorizontal_DisjointSchemas_ConcatenatesColumns()
    {
        var left = TwoColumnSchema("A", "B");
        var right = TwoColumnSchema("C", "D");

        var merged = left.MergeHorizontal(right, ConflictPolicy.ErrorOnConflict);

        Assert.Equal(4, merged.Columns.Count);
        Assert.Equal("A", merged.Columns[0].Name);
        Assert.Equal("D", merged.Columns[3].Name);
    }

    [Fact]
    public void MergeHorizontal_LastWriterWins_ReplacesAndAudits()
    {
        var left = new Schema([new Column("X", FieldType.Scalar<int>())]);
        var right = new Schema([new Column("X", FieldType.Scalar<float>())]);

        var merged = left.MergeHorizontal(right, ConflictPolicy.LastWriterWins);

        var col = merged.FindByName("X");
        Assert.NotNull(col);
        Assert.Equal(typeof(float), col.Type.ClrType);
        Assert.True(col.Annotations.TryGetValue<string>("schema:shadowed-type", out var shadowed));
        Assert.Equal("Int32", shadowed);
    }

    [Fact]
    public void MergeHorizontal_ErrorOnConflict_Throws()
    {
        var left = new Schema([new Column("X", FieldType.Scalar<int>())]);
        var right = new Schema([new Column("X", FieldType.Scalar<float>())]);

        Assert.Throws<InvalidOperationException>(
            () => left.MergeHorizontal(right, ConflictPolicy.ErrorOnConflict));
    }

    [Fact]
    public void MergeHorizontal_LevelIsMinOfBoth()
    {
        var left = new Schema([new Column("A", FieldType.Scalar<int>())], RefinementLevel.Exact);
        var right = new Schema([new Column("B", FieldType.Scalar<int>())], RefinementLevel.Approximate);

        var merged = left.MergeHorizontal(right, ConflictPolicy.ErrorOnConflict);
        Assert.Equal(RefinementLevel.Approximate, merged.Level);
    }

    [Fact]
    public void MergeVertical_CompatibleSchemas_ReturnsSelf()
    {
        var left = TwoColumnSchema();
        var right = TwoColumnSchema();

        var result = left.MergeVertical(right);
        Assert.Same(left, result);
    }

    [Fact]
    public void MergeVertical_DifferentColumnCount_Throws()
    {
        var left = TwoColumnSchema();
        var right = new Schema([new Column("A", FieldType.Scalar<int>())]);

        Assert.Throws<InvalidOperationException>(() => left.MergeVertical(right));
    }

    [Fact]
    public void MergeVertical_NameMismatch_Throws()
    {
        var left = TwoColumnSchema("A", "B");
        var right = TwoColumnSchema("A", "C");

        Assert.Throws<InvalidOperationException>(() => left.MergeVertical(right));
    }

    [Fact]
    public void MergeVertical_TypeMismatch_Throws()
    {
        var left = new Schema([new Column("A", FieldType.Scalar<int>()), new Column("B", FieldType.Scalar<float>())]);
        var right = new Schema([new Column("A", FieldType.Scalar<int>()), new Column("B", FieldType.Scalar<double>())]);

        Assert.Throws<InvalidOperationException>(() => left.MergeVertical(right));
    }

    [Fact]
    public void IsRefinementOf_HigherLevel_SupersetColumns_ReturnsTrue()
    {
        var exact = new Schema([
            new Column("A", FieldType.Scalar<int>()),
            new Column("B", FieldType.Scalar<float>()),
            new Column("C", FieldType.Scalar<double>()),
        ], RefinementLevel.Exact);

        var approx = new Schema([
            new Column("A", FieldType.Scalar<int>()),
            new Column("B", FieldType.Scalar<float>()),
        ], RefinementLevel.Approximate);

        Assert.True(exact.IsRefinementOf(approx));
    }

    [Fact]
    public void IsRefinementOf_SameLevel_ReturnsFalse()
    {
        var a = new Schema([new Column("A", FieldType.Scalar<int>())], RefinementLevel.Exact);
        var b = new Schema([new Column("A", FieldType.Scalar<int>())], RefinementLevel.Exact);

        Assert.False(a.IsRefinementOf(b));
    }

    [Fact]
    public void IsRefinementOf_MissingColumn_ReturnsFalse()
    {
        var exact = new Schema([new Column("A", FieldType.Scalar<int>())], RefinementLevel.Exact);
        var approx = new Schema([
            new Column("A", FieldType.Scalar<int>()),
            new Column("B", FieldType.Scalar<float>()),
        ], RefinementLevel.Approximate);

        Assert.False(exact.IsRefinementOf(approx));
    }

    [Fact]
    public void Columns_PreservesInsertionOrder()
    {
        var schema = new Schema([
            new Column("C", FieldType.Scalar<int>()),
            new Column("A", FieldType.Scalar<int>()),
            new Column("B", FieldType.Scalar<int>()),
        ]);

        Assert.Equal("C", schema.Columns[0].Name);
        Assert.Equal("A", schema.Columns[1].Name);
        Assert.Equal("B", schema.Columns[2].Name);
    }
}
