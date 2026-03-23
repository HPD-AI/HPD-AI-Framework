using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class SchemaBuilderTests
{
    [Fact]
    public void AddColumn_Generic_CreatesScalarColumn()
    {
        var schema = new SchemaBuilder()
            .AddColumn<float>("x")
            .Build();

        var col = schema.FindByName("x");
        Assert.NotNull(col);
        Assert.Equal(typeof(float), col.Type.ClrType);
        Assert.False(col.Type.IsVector);
    }

    [Fact]
    public void AddColumn_WithRole_AddsRoleAnnotation()
    {
        var schema = new SchemaBuilder()
            .AddColumn<float>("label", "Label")
            .Build();

        var col = schema.FindByName("label");
        Assert.NotNull(col);
        Assert.True(col.Annotations.TryGetValue<bool>("role:Label", out var v));
        Assert.True(v);
    }

    [Fact]
    public void AddVectorColumn_CreatesDimensionedColumn()
    {
        var schema = new SchemaBuilder()
            .AddVectorColumn<float>("embed", 128)
            .Build();

        var col = schema.FindByName("embed");
        Assert.NotNull(col);
        Assert.True(col.Type.IsVector);
        Assert.Equal([128], col.Type.Dimensions);
    }

    [Fact]
    public void AddColumn_ExplicitFieldType()
    {
        var ft = new FieldType(typeof(double));
        var schema = new SchemaBuilder()
            .AddColumn("x", ft)
            .Build();

        Assert.Equal(typeof(double), schema.FindByName("x")!.Type.ClrType);
    }

    [Fact]
    public void WithLevel_SetsRefinementLevel()
    {
        var schema = new SchemaBuilder()
            .AddColumn<int>("x")
            .WithLevel(RefinementLevel.Approximate)
            .Build();

        Assert.Equal(RefinementLevel.Approximate, schema.Level);
    }

    [Fact]
    public void Build_ReturnsSchemaWithAllColumns()
    {
        var schema = new SchemaBuilder()
            .AddColumn<int>("a")
            .AddColumn<float>("b")
            .AddColumn<double>("c")
            .Build();

        Assert.Equal(3, schema.Columns.Count);
        Assert.Equal("a", schema.Columns[0].Name);
        Assert.Equal("b", schema.Columns[1].Name);
        Assert.Equal("c", schema.Columns[2].Name);
    }

    [Fact]
    public void Build_DefaultLevel_IsExact()
    {
        var schema = new SchemaBuilder()
            .AddColumn<int>("x")
            .Build();

        Assert.Equal(RefinementLevel.Exact, schema.Level);
    }
}
