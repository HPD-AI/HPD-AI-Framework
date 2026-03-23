namespace HPD.ML.Clustering.Tests;

public class KMeansPlusPlusInitTests
{
    [Fact]
    public void Initialize_ReturnsKCentroids()
    {
        var data = TestHelper.MaterializeFeatures(30, k: 3);
        var centroids = KMeansPlusPlusInit.Initialize(data, 5, 2, new Random(42));
        Assert.Equal(5 * 2, centroids.Length);
    }

    [Fact]
    public void Initialize_FirstCentroidIsDataPoint()
    {
        var data = TestHelper.MaterializeFeatures(20, k: 2);
        var centroids = KMeansPlusPlusInit.Initialize(data, 3, 2, new Random(42));

        float cx = centroids[0], cy = centroids[1];
        bool found = data.Any(p => Math.Abs(p[0] - cx) < 1e-6 && Math.Abs(p[1] - cy) < 1e-6);
        Assert.True(found, "First centroid should be a data point");
    }

    [Fact]
    public void Initialize_SpreadOut()
    {
        // 3 tight clusters far apart
        var data = TestHelper.MaterializeFeatures(30, k: 3, radius: 20f, spread: 0.5f);
        var centroids = KMeansPlusPlusInit.Initialize(data, 3, 2, new Random(42));

        // All 3 centroids should be far from each other (at least radius apart)
        for (int i = 0; i < 3; i++)
        for (int j = i + 1; j < 3; j++)
        {
            float dx = centroids[i * 2] - centroids[j * 2];
            float dy = centroids[i * 2 + 1] - centroids[j * 2 + 1];
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            Assert.True(dist > 10f, $"Centroids {i} and {j} too close: {dist}");
        }
    }

    [Fact]
    public void Initialize_Deterministic()
    {
        var data = TestHelper.MaterializeFeatures(30, k: 3);
        var c1 = KMeansPlusPlusInit.Initialize(data, 5, 2, new Random(42));
        var c2 = KMeansPlusPlusInit.Initialize(data, 5, 2, new Random(42));
        Assert.Equal(c1, c2);
    }
}
