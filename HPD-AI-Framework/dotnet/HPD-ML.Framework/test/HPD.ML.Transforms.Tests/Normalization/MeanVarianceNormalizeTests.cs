namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class MeanVarianceNormalizeTests
{
    [Fact]
    public void MeanVar_NormalizesCorrectly()
    {
        // mean=4, stddev=sqrt((4+0+4)/3) = sqrt(8/3) ≈ 1.633
        var data = TestHelper.Data(("V", new float[] { 2, 4, 6 }));
        double mean = 4.0, stdDev = Math.Sqrt(8.0 / 3.0);
        var transform = new MeanVarianceNormalizeTransform("V", mean, stdDev);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal((float)((2 - mean) / stdDev), values[0], 0.01f);
        Assert.Equal(0f, values[1], 0.01f);
        Assert.Equal((float)((6 - mean) / stdDev), values[2], 0.01f);
    }

    [Fact]
    public void MeanVar_OutputColumn_AddsNew()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 2 }));
        var transform = new MeanVarianceNormalizeTransform("V", 1.5, 0.5, outputColumnName: "Z");
        var result = transform.Apply(data);
        Assert.NotNull(result.Schema.FindByName("V"));
        Assert.NotNull(result.Schema.FindByName("Z"));
    }

    [Fact]
    public void MeanVar_ZeroStdDev_TreatsAs1()
    {
        var data = TestHelper.Data(("V", new float[] { 5, 5 }));
        var transform = new MeanVarianceNormalizeTransform("V", mean: 5, stdDev: 0);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.All(values, v => Assert.Equal(0f, v, 0.01f));
    }

    [Fact]
    public void MeanVar_PreservesOtherColumns()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 2 }), ("Id", new int[] { 10, 20 }));
        var transform = new MeanVarianceNormalizeTransform("V", 1.5, 0.5);
        var result = transform.Apply(data);
        var ids = TestHelper.CollectInt(result, "Id");
        Assert.Equal([10, 20], ids);
    }

    [Fact]
    public void MeanVar_PreservesRowCount()
    {
        var transform = new MeanVarianceNormalizeTransform("V", 0, 1);
        Assert.True(transform.Properties.PreservesRowCount);
    }
}

public class MeanVarianceNormalizeLearnerTests
{
    [Fact]
    public void MeanVarLearner_ComputesMeanStdDev()
    {
        var data = TestHelper.Data(("V", new float[] { 2, 4, 6 }));
        var learner = new MeanVarianceNormalizeLearner("V");
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MeanVarianceParameters>(model.Parameters);
        Assert.Equal(4.0, p.Mean, 0.01);
        Assert.Equal(Math.Sqrt(8.0 / 3.0), p.StdDev, 0.01);
    }

    [Fact]
    public void MeanVarLearner_Transform_AppliesZScore()
    {
        var data = TestHelper.Data(("V", new float[] { 2, 4, 6 }));
        var learner = new MeanVarianceNormalizeLearner("V");
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        // Middle value (4) should be ~0
        Assert.Equal(0f, values[1], 0.01f);
    }

    [Fact]
    public void MeanVarLearner_SingleValue_ZeroStdDev()
    {
        var data = TestHelper.Data(("V", new float[] { 5 }));
        var learner = new MeanVarianceNormalizeLearner("V");
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<MeanVarianceParameters>(model.Parameters);
        Assert.Equal(5.0, p.Mean, 0.01);
        // stdDev=0, but transform should handle this (uses 1)
        var result = model.Transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.Equal(0f, values[0], 0.01f);
    }

    [Fact]
    public async Task MeanVarLearner_FitAsync_SameResult()
    {
        var data = TestHelper.Data(("V", new float[] { 2, 4, 6 }));
        var learner = new MeanVarianceNormalizeLearner("V");
        var syncModel = learner.Fit(new LearnerInput(data));
        var asyncModel = await learner.FitAsync(new LearnerInput(data));
        var syncP = (MeanVarianceParameters)syncModel.Parameters;
        var asyncP = (MeanVarianceParameters)asyncModel.Parameters;
        Assert.Equal(syncP.Mean, asyncP.Mean, 0.01);
        Assert.Equal(syncP.StdDev, asyncP.StdDev, 0.01);
    }
}
