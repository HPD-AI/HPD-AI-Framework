namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class MinMaxNormalizeTests
{
    [Fact]
    public void MinMax_ScalesTo01_Default()
    {
        var data = TestHelper.Data(("V", new float[] { 0, 50, 100 }));
        var transform = new MinMaxNormalizeTransform("V", dataMin: 0, dataMax: 100);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(0f, values[0], 0.001f);
        Assert.Equal(0.5f, values[1], 0.001f);
        Assert.Equal(1f, values[2], 0.001f);
    }

    [Fact]
    public void MinMax_CustomRange_ScalesCorrectly()
    {
        var data = TestHelper.Data(("V", new float[] { 0, 50, 100 }));
        var transform = new MinMaxNormalizeTransform("V", 0, 100, scaleMin: -1, scaleMax: 1);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(-1f, values[0], 0.001f);
        Assert.Equal(0f, values[1], 0.001f);
        Assert.Equal(1f, values[2], 0.001f);
    }

    [Fact]
    public void MinMax_OutputColumn_AddsNewColumn()
    {
        var data = TestHelper.Data(("V", new float[] { 0, 100 }));
        var transform = new MinMaxNormalizeTransform("V", 0, 100, outputColumnName: "Norm");
        var result = transform.Apply(data);
        Assert.NotNull(result.Schema.FindByName("V"));
        Assert.NotNull(result.Schema.FindByName("Norm"));
    }

    [Fact]
    public void MinMax_InPlace_OverwritesColumn()
    {
        var data = TestHelper.Data(("V", new float[] { 0, 100 }));
        var transform = new MinMaxNormalizeTransform("V", 0, 100);
        var result = transform.Apply(data);
        Assert.Equal(1, result.Schema.Columns.Count);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(0f, values[0], 0.001f);
        Assert.Equal(1f, values[1], 0.001f);
    }

    [Fact]
    public void MinMax_ZeroRange_NoDiv0()
    {
        var data = TestHelper.Data(("V", new float[] { 5, 5, 5 }));
        var transform = new MinMaxNormalizeTransform("V", 5, 5);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.All(values, v => Assert.Equal(0f, v, 0.001f));
    }

    [Fact]
    public void MinMax_MissingColumn_Throws()
    {
        var data = TestHelper.Data(("V", new float[] { 1 }));
        var transform = new MinMaxNormalizeTransform("Missing", 0, 1);
        Assert.Throws<InvalidOperationException>(() => transform.GetOutputSchema(data.Schema));
    }

    [Fact]
    public void MinMax_PreservesRowCount()
    {
        var transform = new MinMaxNormalizeTransform("V", 0, 1);
        Assert.True(transform.Properties.PreservesRowCount);
    }
}

public class MinMaxNormalizeLearnerTests
{
    [Fact]
    public void MinMaxLearner_FitsMinMax_FromData()
    {
        var data = TestHelper.Data(("V", new float[] { 10, 20, 30 }));
        var learner = new MinMaxNormalizeLearner("V");
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<NormalizationParameters>(model.Parameters);
        Assert.Equal(10f, p.Min);
        Assert.Equal(30f, p.Max);
    }

    [Fact]
    public void MinMaxLearner_Transform_AppliesCorrectly()
    {
        var data = TestHelper.Data(("V", new float[] { 10, 20, 30 }));
        var learner = new MinMaxNormalizeLearner("V");
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(0f, values[0], 0.001f);
        Assert.Equal(0.5f, values[1], 0.001f);
        Assert.Equal(1f, values[2], 0.001f);
    }

    [Fact]
    public void MinMaxLearner_CustomScale_Propagated()
    {
        var data = TestHelper.Data(("V", new float[] { 10, 30 }));
        var learner = new MinMaxNormalizeLearner("V", scaleMin: -1, scaleMax: 1);
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(-1f, values[0], 0.001f);
        Assert.Equal(1f, values[1], 0.001f);
    }

    [Fact]
    public void MinMaxLearner_OutputSchema_MatchesTransform()
    {
        var schema = new SchemaBuilder().AddColumn<float>("V").Build();
        var learner = new MinMaxNormalizeLearner("V");
        var outSchema = learner.GetOutputSchema(schema);
        Assert.NotNull(outSchema.FindByName("V"));
    }

    [Fact]
    public async Task MinMaxLearner_FitAsync_SameResult()
    {
        var data = TestHelper.Data(("V", new float[] { 10, 20, 30 }));
        var learner = new MinMaxNormalizeLearner("V");
        var syncModel = learner.Fit(new LearnerInput(data));
        var asyncModel = await learner.FitAsync(new LearnerInput(data));
        var syncP = (NormalizationParameters)syncModel.Parameters;
        var asyncP = (NormalizationParameters)asyncModel.Parameters;
        Assert.Equal(syncP.Min, asyncP.Min);
        Assert.Equal(syncP.Max, asyncP.Max);
    }
}
