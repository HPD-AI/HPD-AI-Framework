namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class TypeConvertTests
{
    [Fact]
    public void TypeConvert_IntToFloat()
    {
        var data = TestHelper.Data(("V", new int[] { 42 }));
        var transform = new TypeConvertTransform("V", typeof(float));
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(42f, values[0], 0.01f);
    }

    [Fact]
    public void TypeConvert_FloatToDouble()
    {
        var data = TestHelper.Data(("V", new float[] { 3.14f }));
        var transform = new TypeConvertTransform("V", typeof(double));
        var result = transform.Apply(data);
        using var cursor = result.GetCursor(["V"]);
        cursor.MoveNext();
        var val = cursor.Current.GetValue<double>("V");
        Assert.Equal(3.14, val, 0.01);
    }

    [Fact]
    public void TypeConvert_StringToInt()
    {
        var data = TestHelper.Data(("V", new string[] { "123" }));
        var transform = new TypeConvertTransform("V", typeof(int));
        var result = transform.Apply(data);
        var values = TestHelper.CollectInt(result, "V");
        Assert.Equal(123, values[0]);
    }

    [Fact]
    public void TypeConvert_Schema_UpdatesType()
    {
        var schema = new SchemaBuilder().AddColumn<int>("V").Build();
        var transform = new TypeConvertTransform("V", typeof(double));
        var outSchema = transform.GetOutputSchema(schema);
        Assert.Equal(typeof(double), outSchema.FindByName("V")!.Type.ClrType);
    }

    [Fact]
    public void TypeConvert_GenericFactory()
    {
        var transform = TypeConvertTransform.To<float>("V");
        var schema = new SchemaBuilder().AddColumn<int>("V").Build();
        var outSchema = transform.GetOutputSchema(schema);
        Assert.Equal(typeof(float), outSchema.FindByName("V")!.Type.ClrType);
    }
}
