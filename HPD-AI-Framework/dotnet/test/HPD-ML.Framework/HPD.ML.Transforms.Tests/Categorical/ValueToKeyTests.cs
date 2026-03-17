namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class ValueToKeyTests
{
    private static readonly Dictionary<string, int> Mapping = new()
    {
        ["A"] = 0, ["B"] = 1, ["C"] = 2
    };

    [Fact]
    public void V2K_MapsStringToInt()
    {
        var data = TestHelper.Data(("V", new string[] { "A", "B", "C" }));
        var transform = new ValueToKeyTransform("V", Mapping);
        var result = transform.Apply(data);
        var values = TestHelper.CollectInt(result, "V");
        Assert.Equal([0, 1, 2], values);
    }

    [Fact]
    public void V2K_UnknownValue_ReturnsNeg1()
    {
        var data = TestHelper.Data(("V", new string[] { "X" }));
        var transform = new ValueToKeyTransform("V", Mapping);
        var result = transform.Apply(data);
        var values = TestHelper.CollectInt(result, "V");
        Assert.Equal([-1], values);
    }

    [Fact]
    public void V2K_InPlace_ChangesType()
    {
        var schema = new SchemaBuilder().AddColumn("V", new FieldType(typeof(string))).Build();
        var transform = new ValueToKeyTransform("V", Mapping);
        var outSchema = transform.GetOutputSchema(schema);
        Assert.Equal(typeof(int), outSchema.FindByName("V")!.Type.ClrType);
    }

    [Fact]
    public void V2K_OutputColumn_AddsNew()
    {
        var schema = new SchemaBuilder().AddColumn("V", new FieldType(typeof(string))).Build();
        var transform = new ValueToKeyTransform("V", Mapping, outputColumnName: "Key");
        var outSchema = transform.GetOutputSchema(schema);
        Assert.NotNull(outSchema.FindByName("V"));
        Assert.NotNull(outSchema.FindByName("Key"));
        Assert.Equal(typeof(int), outSchema.FindByName("Key")!.Type.ClrType);
    }

    [Fact]
    public void V2K_PreservesRowCount()
    {
        var transform = new ValueToKeyTransform("V", Mapping);
        Assert.True(transform.Properties.PreservesRowCount);
    }
}
