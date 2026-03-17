namespace HPD.ML.Clustering.Tests;

public class ClusteringModelParametersTests
{
    [Fact]
    public void Constructor_ComputesCentroidNorms()
    {
        // centroids: [1,0] and [0,1]
        var p = new ClusteringModelParameters(2, 2, [1f, 0f, 0f, 1f]);
        Assert.Equal(1f, p.CentroidNormsSquared[0]);
        Assert.Equal(1f, p.CentroidNormsSquared[1]);
    }

    [Fact]
    public void GetCentroid_ReturnsCorrectSlice()
    {
        var p = new ClusteringModelParameters(3, 2, [1f, 2f, 3f, 4f, 5f, 6f]);
        var c1 = p.GetCentroid(1);
        Assert.Equal(3f, c1[0]);
        Assert.Equal(4f, c1[1]);
    }

    [Fact]
    public void DistanceSquared_IdenticalPoint_Zero()
    {
        var p = new ClusteringModelParameters(1, 2, [3f, 4f]);
        float dist = p.DistanceSquared([3f, 4f], 0);
        Assert.Equal(0f, dist, 1e-5f);
    }

    [Fact]
    public void DistanceSquared_KnownValues()
    {
        var p = new ClusteringModelParameters(1, 2, [0f, 0f]);
        float dist = p.DistanceSquared([3f, 4f], 0);
        Assert.Equal(25f, dist, 1e-5f);
    }

    [Fact]
    public void DistanceSquared_NonNegative()
    {
        var p = new ClusteringModelParameters(2, 3, [1f, 2f, 3f, 4f, 5f, 6f]);
        var rng = new Random(42);
        for (int i = 0; i < 20; i++)
        {
            var point = new float[] { (float)rng.NextDouble() * 10, (float)rng.NextDouble() * 10, (float)rng.NextDouble() * 10 };
            Assert.True(p.DistanceSquared(point, 0) >= 0);
            Assert.True(p.DistanceSquared(point, 1) >= 0);
        }
    }

    [Fact]
    public void NearestCluster_ReturnsClosest()
    {
        // centroids: [0,0], [10,10], [20,20]
        var p = new ClusteringModelParameters(3, 2, [0f, 0f, 10f, 10f, 20f, 20f]);
        var (cluster, dist) = p.NearestCluster([9f, 9f]);
        Assert.Equal(1, cluster);
        Assert.True(dist < 5f);
    }

    [Fact]
    public void NearestCluster_TieBreaksToFirst()
    {
        // Two identical centroids at origin
        var p = new ClusteringModelParameters(2, 2, [0f, 0f, 0f, 0f]);
        var (cluster, _) = p.NearestCluster([5f, 5f]);
        Assert.Equal(0, cluster);
    }
}
