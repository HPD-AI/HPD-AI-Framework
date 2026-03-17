namespace HPD.ML.Clustering.Tests;

public class RandomInitTests
{
    [Fact]
    public void Initialize_ReturnsKCentroids()
    {
        var data = TestHelper.MaterializeFeatures(30, k: 3);
        var centroids = RandomInit.Initialize(data, 5, 2, new Random(42));
        Assert.Equal(5 * 2, centroids.Length);
    }

    [Fact]
    public void Initialize_CentroidsAreDataPoints()
    {
        var data = TestHelper.MaterializeFeatures(20, k: 2);
        var centroids = RandomInit.Initialize(data, 3, 2, new Random(42));

        for (int c = 0; c < 3; c++)
        {
            float cx = centroids[c * 2];
            float cy = centroids[c * 2 + 1];
            bool found = data.Any(p => Math.Abs(p[0] - cx) < 1e-6 && Math.Abs(p[1] - cy) < 1e-6);
            Assert.True(found, $"Centroid {c} ({cx},{cy}) not found in data");
        }
    }

    [Fact]
    public void Initialize_DistinctCentroids()
    {
        var data = TestHelper.MaterializeFeatures(20, k: 3);
        var centroids = RandomInit.Initialize(data, 5, 2, new Random(42));

        var set = new HashSet<(float, float)>();
        for (int c = 0; c < 5; c++)
            set.Add((centroids[c * 2], centroids[c * 2 + 1]));
        Assert.Equal(5, set.Count);
    }

    [Fact]
    public void Initialize_Deterministic()
    {
        var data = TestHelper.MaterializeFeatures(30, k: 3);
        var c1 = RandomInit.Initialize(data, 5, 2, new Random(42));
        var c2 = RandomInit.Initialize(data, 5, 2, new Random(42));
        Assert.Equal(c1, c2);
    }
}
