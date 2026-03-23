namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class HashTransformTests
{
    [Fact]
    public void Hash_ProducesUint()
    {
        var data = TestHelper.Data(("V", new string[] { "hello" }));
        var transform = new HashTransform("V");
        var result = transform.Apply(data);
        using var cursor = result.GetCursor(["V"]);
        Assert.True(cursor.MoveNext());
        var val = cursor.Current.GetValue<uint>("V");
        Assert.IsType<uint>(val);
    }

    [Fact]
    public void Hash_Deterministic()
    {
        var data = TestHelper.Data(("V", new string[] { "hello", "hello" }));
        var transform = new HashTransform("V");
        var result = transform.Apply(data);
        using var cursor = result.GetCursor(["V"]);
        cursor.MoveNext();
        var first = cursor.Current.GetValue<uint>("V");
        cursor.MoveNext();
        var second = cursor.Current.GetValue<uint>("V");
        Assert.Equal(first, second);
    }

    [Fact]
    public void Hash_DifferentInputs_DifferentHash()
    {
        var data = TestHelper.Data(("V", new string[] { "hello", "world" }));
        var transform = new HashTransform("V");
        var result = transform.Apply(data);
        using var cursor = result.GetCursor(["V"]);
        cursor.MoveNext();
        var first = cursor.Current.GetValue<uint>("V");
        cursor.MoveNext();
        var second = cursor.Current.GetValue<uint>("V");
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hash_NumBits_BoundsOutput()
    {
        var values = Enumerable.Range(0, 50).Select(i => $"val_{i}").ToArray();
        var data = TestHelper.Data(("V", values));
        var transform = new HashTransform("V", numBits: 8);
        var result = transform.Apply(data);
        using var cursor = result.GetCursor(["V"]);
        while (cursor.MoveNext())
        {
            var hash = cursor.Current.GetValue<uint>("V");
            Assert.True(hash < 256u);
        }
    }

    [Fact]
    public void Hash_OutputColumn_AddsNew()
    {
        var schema = new SchemaBuilder().AddColumn("V", new FieldType(typeof(string))).Build();
        var transform = new HashTransform("V", outputColumnName: "Hashed");
        var outSchema = transform.GetOutputSchema(schema);
        Assert.NotNull(outSchema.FindByName("V"));
        Assert.NotNull(outSchema.FindByName("Hashed"));
        Assert.Equal(typeof(uint), outSchema.FindByName("Hashed")!.Type.ClrType);
    }

    [Fact]
    public void Hash_InPlace_ChangesType()
    {
        var schema = new SchemaBuilder().AddColumn("V", new FieldType(typeof(string))).Build();
        var transform = new HashTransform("V");
        var outSchema = transform.GetOutputSchema(schema);
        Assert.Equal(typeof(uint), outSchema.FindByName("V")!.Type.ClrType);
    }
}
