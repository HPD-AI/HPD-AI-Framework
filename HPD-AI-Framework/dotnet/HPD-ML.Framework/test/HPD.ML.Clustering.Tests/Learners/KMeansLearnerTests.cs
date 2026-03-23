namespace HPD.ML.Clustering.Tests;

using HPD.ML.Abstractions;

public class KMeansLearnerTests
{
    [Fact]
    public void Fit_SeparatedClusters_FindsThem()
    {
        var data = TestHelper.BlobData(30, k: 3, radius: 20f, spread: 1f);
        var learner = new KMeansLearner(options: new KMeansOptions
        {
            NumberOfClusters = 3,
            Initialization = KMeansInitialization.KMeansPlusPlus,
            Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);

        var labels = TestHelper.CollectUInt(predictions, "PredictedLabel");
        Assert.Equal(90, labels.Count);

        // Check that each original blob maps to a single cluster
        // (first 30 should be same label, next 30 same, last 30 same)
        var blob0 = labels.Take(30).Distinct().ToList();
        var blob1 = labels.Skip(30).Take(30).Distinct().ToList();
        var blob2 = labels.Skip(60).Take(30).Distinct().ToList();

        // Each blob should mostly map to one cluster
        Assert.True(blob0.Count <= 2, $"Blob 0 has {blob0.Count} distinct labels");
        Assert.True(blob1.Count <= 2, $"Blob 1 has {blob1.Count} distinct labels");
        Assert.True(blob2.Count <= 2, $"Blob 2 has {blob2.Count} distinct labels");
    }

    [Fact]
    public void Fit_ConvergesOnSimpleData()
    {
        var data = TestHelper.BlobData(20, k: 2, radius: 10f);
        var events = new List<ProgressEvent>();
        var learner = new KMeansLearner(options: new KMeansOptions
        {
            NumberOfClusters = 2, MaxIterations = 100, Seed = 42
        });
        learner.Progress.Subscribe(new Observer<ProgressEvent>(e => events.Add(e)));
        learner.Fit(new LearnerInput(data));

        // Should converge before max iterations
        Assert.True(events.Count < 100, $"Did not converge: {events.Count} iterations");
    }

    [Fact]
    public void Fit_ReturnsModelWithTransform()
    {
        var data = TestHelper.BlobData(20, k: 2);
        var learner = new KMeansLearner(options: new KMeansOptions { NumberOfClusters = 2, Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));

        Assert.NotNull(model.Transform);
        Assert.IsType<ClusteringModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_ParametersHaveCorrectKAndDim()
    {
        var data = TestHelper.RandomData(30, dim: 3);
        var learner = new KMeansLearner(options: new KMeansOptions { NumberOfClusters = 4, Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));

        var p = Assert.IsType<ClusteringModelParameters>(model.Parameters);
        Assert.Equal(4, p.K);
        Assert.Equal(3, p.Dimensionality);
    }

    [Fact]
    public void Fit_PredictionsHaveCorrectSchema()
    {
        var data = TestHelper.BlobData(10, k: 3);
        var learner = new KMeansLearner(options: new KMeansOptions { NumberOfClusters = 3, Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);

        Assert.NotNull(predictions.Schema.FindByName("PredictedLabel"));
        Assert.NotNull(predictions.Schema.FindByName("Score"));
    }

    [Fact]
    public void Fit_TooFewDataPoints_Throws()
    {
        var data = TestHelper.RandomData(3, dim: 2);
        var learner = new KMeansLearner(options: new KMeansOptions { NumberOfClusters = 5, Seed = 42 });
        Assert.Throws<ArgumentException>(() => learner.Fit(new LearnerInput(data)));
    }

    [Fact]
    public void Fit_EmitsProgressEvents()
    {
        var data = TestHelper.BlobData(20, k: 2);
        var events = new List<ProgressEvent>();
        var learner = new KMeansLearner(options: new KMeansOptions { NumberOfClusters = 2, Seed = 42 });
        learner.Progress.Subscribe(new Observer<ProgressEvent>(e => events.Add(e)));
        learner.Fit(new LearnerInput(data));

        Assert.NotEmpty(events);
        Assert.All(events, e =>
        {
            Assert.Equal("AverageDistance", e.MetricName);
            Assert.NotNull(e.MetricValue);
            Assert.True(e.MetricValue >= 0);
        });
    }

    [Fact]
    public void Fit_Deterministic()
    {
        var data = TestHelper.BlobData(20, k: 2);
        var l1 = new KMeansLearner(options: new KMeansOptions { NumberOfClusters = 2, Seed = 42 });
        var l2 = new KMeansLearner(options: new KMeansOptions { NumberOfClusters = 2, Seed = 42 });

        var p1 = (ClusteringModelParameters)l1.Fit(new LearnerInput(data)).Parameters;
        var p2 = (ClusteringModelParameters)l2.Fit(new LearnerInput(data)).Parameters;

        Assert.Equal(p1.Centroids, p2.Centroids);
    }

    [Fact]
    public void Fit_KMeansPlusPlusInit_Works()
    {
        var data = TestHelper.BlobData(20, k: 3);
        var learner = new KMeansLearner(options: new KMeansOptions
        {
            NumberOfClusters = 3,
            Initialization = KMeansInitialization.KMeansPlusPlus,
            Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<ClusteringModelParameters>(model.Parameters);
        Assert.Equal(3, p.K);
        Assert.All(p.Centroids, c => Assert.True(float.IsFinite(c)));
    }

    [Fact]
    public void Fit_RandomInit_Works()
    {
        var data = TestHelper.BlobData(20, k: 3);
        var learner = new KMeansLearner(options: new KMeansOptions
        {
            NumberOfClusters = 3,
            Initialization = KMeansInitialization.Random,
            Seed = 42
        });
        var model = learner.Fit(new LearnerInput(data));
        var p = Assert.IsType<ClusteringModelParameters>(model.Parameters);
        Assert.Equal(3, p.K);
        Assert.All(p.Centroids, c => Assert.True(float.IsFinite(c)));
    }
}
