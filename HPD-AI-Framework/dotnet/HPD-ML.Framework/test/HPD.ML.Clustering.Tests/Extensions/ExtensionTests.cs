namespace HPD.ML.Clustering.Tests;

using HPD.ML.Abstractions;

public class ExtensionTests
{
    [Fact]
    public void KMeans_ReturnsKMeansLearner()
    {
        ILearner learner = ILearner.KMeans();
        Assert.IsType<KMeansLearner>(learner);
    }

    [Fact]
    public void MiniBatchKMeans_ReturnsMiniBatchLearner()
    {
        ILearner learner = ILearner.MiniBatchKMeans();
        Assert.IsType<MiniBatchKMeansLearner>(learner);
    }
}
