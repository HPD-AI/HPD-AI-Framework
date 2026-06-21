namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class MissingValueIndicateTests
{
    [Fact]
    public void Indicate_NaN_TrueIndicator()
    {
        var data = TestHelper.Data(("V", new float[] { float.NaN }));
        var transform = new MissingValueIndicateTransform("V");
        var result = transform.Apply(data);
        var indicators = TestHelper.CollectBool(result, "V_Missing");
        Assert.True(indicators[0]);
    }

    [Fact]
    public void Indicate_Valid_FalseIndicator()
    {
        var data = TestHelper.Data(("V", new float[] { 42f }));
        var transform = new MissingValueIndicateTransform("V");
        var result = transform.Apply(data);
        var indicators = TestHelper.CollectBool(result, "V_Missing");
        Assert.False(indicators[0]);
    }

    [Fact]
    public void Indicate_CustomColumnName()
    {
        var schema = new SchemaBuilder().AddColumn<float>("V").Build();
        var transform = new MissingValueIndicateTransform("V", indicatorColumnName: "HasMissing");
        var outSchema = transform.GetOutputSchema(schema);
        Assert.NotNull(outSchema.FindByName("HasMissing"));
    }

    [Fact]
    public void Indicate_DefaultColumnName()
    {
        var schema = new SchemaBuilder().AddColumn<float>("Score").Build();
        var transform = new MissingValueIndicateTransform("Score");
        var outSchema = transform.GetOutputSchema(schema);
        Assert.NotNull(outSchema.FindByName("Score_Missing"));
    }
}
