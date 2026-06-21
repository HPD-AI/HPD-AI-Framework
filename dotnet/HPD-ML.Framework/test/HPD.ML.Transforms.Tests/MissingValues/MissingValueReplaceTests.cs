namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class MissingValueReplaceTests
{
    [Fact]
    public void Replace_NaN_WithConstant()
    {
        var data = TestHelper.Data(("V", new float[] { 1, float.NaN, 3 }));
        var transform = new MissingValueReplaceTransform("V", ReplacementValue.Constant(0f));
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(1f, values[0], 0.01f);
        Assert.Equal(0f, values[1], 0.01f);
        Assert.Equal(3f, values[2], 0.01f);
    }

    [Fact]
    public void Replace_Null_WithConstant()
    {
        // Use object array with null
        var schema = new SchemaBuilder().AddColumn<float>("V").Build();
        var columns = new Dictionary<string, Array> { ["V"] = new object[] { 1f, null!, 3f } };
        var data = new InMemoryDataHandle(schema, columns);
        var transform = new MissingValueReplaceTransform("V", ReplacementValue.Constant(-1f));
        var result = transform.Apply(data);
        var values = TestHelper.CollectObject(result, "V");
        Assert.Equal(-1f, values[1]);
    }

    [Fact]
    public void Replace_Whitespace_WithConstant()
    {
        var data = TestHelper.Data(("V", new string[] { "hello", "  ", "world" }));
        var transform = new MissingValueReplaceTransform("V", ReplacementValue.Constant("N/A"));
        var result = transform.Apply(data);
        var values = TestHelper.CollectObject(result, "V");
        Assert.Equal("hello", values[0]);
        Assert.Equal("N/A", values[1]);
        Assert.Equal("world", values[2]);
    }

    [Fact]
    public void Replace_NonMissing_Unchanged()
    {
        var data = TestHelper.Data(("V", new float[] { 42f }));
        var transform = new MissingValueReplaceTransform("V", ReplacementValue.Zero);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(42f, values[0], 0.01f);
    }

    [Fact]
    public void Replace_DoubleNaN_Detected()
    {
        var data = TestHelper.Data(("V", new double[] { 1.0, double.NaN, 3.0 }));
        var transform = new MissingValueReplaceTransform("V", ReplacementValue.Constant(0.0));
        var result = transform.Apply(data);
        var values = TestHelper.CollectObject(result, "V");
        Assert.Equal(0.0, values[1]);
    }

    [Fact]
    public void Replace_PreservesSchema()
    {
        var schema = new SchemaBuilder().AddColumn<float>("V").Build();
        var transform = new MissingValueReplaceTransform("V", ReplacementValue.Zero);
        var outSchema = transform.GetOutputSchema(schema);
        Assert.Equal(schema.Columns.Count, outSchema.Columns.Count);
        Assert.Equal("V", outSchema.Columns[0].Name);
    }
}

public class MissingValueReplaceLearnerTests
{
    [Fact]
    public void ReplaceLearner_Mean_ComputesCorrectly()
    {
        var data = TestHelper.Data(("V", new float[] { 1, float.NaN, 3, 5 }));
        var learner = new MissingValueReplaceLearner("V", ReplacementStrategy.Mean);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MissingValueParameters>(model.Parameters);
        Assert.Equal(3f, p.Replacement, 0.01f); // (1+3+5)/3 = 3
    }

    [Fact]
    public void ReplaceLearner_Median_ComputesCorrectly()
    {
        var data = TestHelper.Data(("V", new float[] { 1, float.NaN, 3, 5, 7 }));
        var learner = new MissingValueReplaceLearner("V", ReplacementStrategy.Median);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MissingValueParameters>(model.Parameters);
        // valid=[1,3,5,7], median of even count = (3+5)/2 = 4
        Assert.Equal(4f, p.Replacement, 0.01f);
    }

    [Fact]
    public void ReplaceLearner_Median_EvenCount()
    {
        var data = TestHelper.Data(("V", new float[] { 1, float.NaN, 3 }));
        var learner = new MissingValueReplaceLearner("V", ReplacementStrategy.Median);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MissingValueParameters>(model.Parameters);
        // valid=[1,3], median = (1+3)/2 = 2
        Assert.Equal(2f, p.Replacement, 0.01f);
    }

    [Fact]
    public void ReplaceLearner_Mode_ComputesCorrectly()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 1, float.NaN, 3 }));
        var learner = new MissingValueReplaceLearner("V", ReplacementStrategy.Mode);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MissingValueParameters>(model.Parameters);
        Assert.Equal(1f, p.Replacement, 0.01f);
    }

    [Fact]
    public void ReplaceLearner_FitTransform_ReplacesNaN()
    {
        var data = TestHelper.Data(("V", new float[] { 1, float.NaN, 5 }));
        var learner = new MissingValueReplaceLearner("V", ReplacementStrategy.Mean);
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(1f, values[0], 0.01f);
        Assert.Equal(3f, values[1], 0.01f); // mean=(1+5)/2=3
        Assert.Equal(5f, values[2], 0.01f);
    }
}
