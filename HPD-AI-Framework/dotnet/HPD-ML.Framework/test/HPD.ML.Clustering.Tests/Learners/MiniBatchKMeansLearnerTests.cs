namespace HPD.ML.Clustering.Tests;

using HPD.ML.Abstractions;

public class MiniBatchKMeansLearnerTests
{
    [Fact]
    public void Fit_SeparatedClusters_FindsThem()
    {
        var data = TestHelper.BlobData(50, k: 3, radius: 20f, spread: 1f);
        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 3, BatchSize = 50, MaxIterations = 100, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var labels = TestHelper.CollectUInt(predictions, "PredictedLabel");
        Assert.Equal(150, labels.Count);
        Assert.True(labels.Distinct().Count() >= 2, "Should assign at least 2 distinct clusters");
    }

    [Fact]
    public void Fit_ReturnsModelWithTransform()
    {
        var data = TestHelper.BlobData(20, k: 2);
        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 2, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        Assert.NotNull(model.Transform);
        Assert.IsType<ClusteringModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_ParametersHaveCorrectKAndDim()
    {
        var data = TestHelper.RandomData(30, dim: 3);
        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 4, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<ClusteringModelParameters>(model.Parameters);
        Assert.Equal(4, p.K);
        Assert.Equal(3, p.Dimensionality);
    }

    [Fact]
    public void Fit_SmallBatchSize_StillConverges()
    {
        var data = TestHelper.BlobData(50, k: 2, radius: 10f);
        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 2, BatchSize = 10, MaxIterations = 200, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<ClusteringModelParameters>(model.Parameters);
        Assert.All(p.Centroids, c => Assert.True(float.IsFinite(c)));
    }

    [Fact]
    public void Fit_BatchSizeLargerThanData_Clamped()
    {
        var data = TestHelper.RandomData(20, dim: 2);
        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 3, BatchSize = 1000, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<ClusteringModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_EmitsProgressEvents()
    {
        var data = TestHelper.BlobData(20, k: 2);
        var events = new List<ProgressEvent>();
        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 2, Seed = 42
        });
        learner.Progress.Subscribe(new Observer<ProgressEvent>(e => events.Add(e)));
        learner.Fit(new LearnerInput(data));

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal("AverageDistance", e.MetricName));
    }

    [Fact]
    public void Fit_TooFewDataPoints_Throws()
    {
        var data = TestHelper.RandomData(3, dim: 2);
        var learner = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions
        {
            NumberOfClusters = 5, Seed = 42
        });
        Assert.Throws<ArgumentException>(() => learner.Fit(new LearnerInput(data)));
    }

    [Fact]
    public void Fit_Deterministic()
    {
        var data = TestHelper.BlobData(20, k: 2);
        var l1 = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions { NumberOfClusters = 2, Seed = 42 });
        var l2 = new MiniBatchKMeansLearner(options: new MiniBatchKMeansOptions { NumberOfClusters = 2, Seed = 42 });

        var p1 = (ClusteringModelParameters)l1.Fit(new LearnerInput(data)).Parameters;
        var p2 = (ClusteringModelParameters)l2.Fit(new LearnerInput(data)).Parameters;

        Assert.Equal(p1.Centroids, p2.Centroids);
    }
}
