namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class BinNormalizeTests
{
    [Fact]
    public void Bin_AssignsBinsCorrectly()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 5, 9 }));
        var transform = new BinNormalizeTransform("V", [3f, 7f]);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        // edges=[3,7] → 3 bins. 1→bin0=0/2=0, 5→bin1=1/2=0.5, 9→bin2=2/2=1
        Assert.Equal(0f, values[0], 0.01f);
        Assert.Equal(0.5f, values[1], 0.01f);
        Assert.Equal(1f, values[2], 0.01f);
    }

    [Fact]
    public void Bin_OutputColumn_AddsNew()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 2 }));
        var transform = new BinNormalizeTransform("V", [1.5f], outputColumnName: "Binned");
        var result = transform.Apply(data);
        Assert.NotNull(result.Schema.FindByName("V"));
        Assert.NotNull(result.Schema.FindByName("Binned"));
    }

    [Fact]
    public void Bin_SingleBin_ReturnsZero()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 5, 9 }));
        var transform = new BinNormalizeTransform("V", []);
        var result = transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        Assert.All(values, v => Assert.Equal(0f, v, 0.01f));
    }

    [Fact]
    public void Bin_PreservesRowCount()
    {
        var transform = new BinNormalizeTransform("V", []);
        Assert.True(transform.Properties.PreservesRowCount);
    }
}

public class BinNormalizeLearnerTests
{
    [Fact]
    public void BinLearner_ComputesEdges()
    {
        var data = TestHelper.Data(("V", Enumerable.Range(1, 100).Select(i => (float)i).ToArray()));
        var learner = new BinNormalizeLearner("V", numBins: 4);
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<BinParameters>(model.Parameters);
        Assert.Equal(3, p.Edges.Length); // 4 bins → 3 edges
    }

    [Fact]
    public void BinLearner_Transform_AppliesCorrectly()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        var learner = new BinNormalizeLearner("V", numBins: 2);
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);
        var values = TestHelper.CollectFloat(result, "V");
        // All values should be 0 or 1
        Assert.All(values, v => Assert.True(v >= 0f && v <= 1f));
    }

    [Fact]
    public async Task BinLearner_FitAsync_SameResult()
    {
        var data = TestHelper.Data(("V", new float[] { 1, 2, 3, 4, 5 }));
        var learner = new BinNormalizeLearner("V", numBins: 3);
        var syncModel = learner.Fit(new LearnerInput(data));
        var asyncModel = await learner.FitAsync(new LearnerInput(data));
        var syncP = (BinParameters)syncModel.Parameters;
        var asyncP = (BinParameters)asyncModel.Parameters;
        Assert.Equal(syncP.Edges, asyncP.Edges);
    }
}
