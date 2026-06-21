namespace HPD.ML.Clustering.Tests;

public class KMeansParallelInitTests
{
    [Fact]
    public void Initialize_ReturnsKCentroids()
    {
        var data = TestHelper.MaterializeFeatures(50, k: 3);
        var centroids = KMeansParallelInit.Initialize(data, 5, 2, new Random(42));
        Assert.Equal(5 * 2, centroids.Length);
    }

    [Fact]
    public void Initialize_ReducesToKCentroids()
    {
        var data = TestHelper.MaterializeFeatures(100, k: 3);
        var centroids = KMeansParallelInit.Initialize(data, 3, 2, new Random(42));
        // Should be exactly K centroids (3 * 2 = 6 floats)
        Assert.Equal(6, centroids.Length);
    }

    [Fact]
    public void Initialize_Deterministic()
    {
        var data = TestHelper.MaterializeFeatures(50, k: 3);
        var c1 = KMeansParallelInit.Initialize(data, 5, 2, new Random(42));
        var c2 = KMeansParallelInit.Initialize(data, 5, 2, new Random(42));
        Assert.Equal(c1, c2);
    }

    [Fact]
    public void Initialize_SmallData_PadsIfNeeded()
    {
        var data = TestHelper.MaterializeFeatures(2, k: 1, radius: 5f);
        // K=5 but only 2 data points — should pad
        var centroids = KMeansParallelInit.Initialize(data, 5, 2, new Random(42));
        Assert.Equal(5 * 2, centroids.Length);
    }
}
