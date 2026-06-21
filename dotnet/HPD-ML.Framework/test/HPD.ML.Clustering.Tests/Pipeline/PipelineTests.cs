namespace HPD.ML.Clustering.Tests;

using HPD.ML.Abstractions;

public class PipelineTests
{
    [Fact]
    public void KMeans_TrainAndPredict()
    {
        var trainData = TestHelper.BlobData(30, k: 3, radius: 15f, seed: 42);
        var testData = TestHelper.BlobData(10, k: 3, radius: 15f, seed: 99);

        var learner = new KMeansLearner(options: new KMeansOptions
        {
            NumberOfClusters = 3, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(trainData));
        var predictions = model.Transform.Apply(testData);

        Assert.Equal(30, TestHelper.CountRows(predictions));
        var labels = TestHelper.CollectUInt(predictions, "PredictedLabel");
        Assert.All(labels, l => Assert.True(l >= 1u && l <= 3u));

        var scores = TestHelper.CollectFloatArray(predictions, "Score");
        Assert.All(scores, s =>
        {
            Assert.Equal(3, s.Length);
            Assert.All(s, v => Assert.True(float.IsFinite(v) && v >= 0));
        });
    }

    [Fact]
    public void MiniBatch_TrainAndPredict()
    {
        var trainData = TestHelper.BlobData(30, k: 3, radius: 15f, seed: 42);
        var testData = TestHelper.BlobData(10, k: 3, radius: 15f, seed: 99);

        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 3, BatchSize = 30, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(trainData));
        var predictions = model.Transform.Apply(testData);

        Assert.Equal(30, TestHelper.CountRows(predictions));
        var labels = TestHelper.CollectUInt(predictions, "PredictedLabel");
        Assert.All(labels, l => Assert.True(l >= 1u && l <= 3u));
    }

    [Fact]
    public void KMeans_SingleFeature_Works()
    {
        var data = TestHelper.Scalar1DData(40);
        var learner = new KMeansLearner(options: new KMeansOptions
        {
            NumberOfClusters = 2, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);

        Assert.Equal(40, TestHelper.CountRows(predictions));
        var labels = TestHelper.CollectUInt(predictions, "PredictedLabel");
        Assert.All(labels, l => Assert.True(l >= 1u && l <= 2u));
    }
}
