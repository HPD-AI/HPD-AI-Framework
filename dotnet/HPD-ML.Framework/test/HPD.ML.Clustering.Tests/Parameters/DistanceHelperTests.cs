namespace HPD.ML.Clustering.Tests;

public class DistanceHelperTests
{
    [Fact]
    public void DistanceSquared_FlatArray_Correct()
    {
        float[] point = [1f, 2f];
        float[] centroids = [0f, 0f, 3f, 4f]; // centroid 1 at offset 2
        float dist = DistanceHelper.DistanceSquared(point, centroids, 2, 2);
        Assert.Equal(8f, dist, 1e-5f); // (1-3)²+(2-4)²=4+4=8
    }

    [Fact]
    public void DistanceSquared_TwoPoints_Correct()
    {
        float dist = DistanceHelper.DistanceSquared([0f, 0f], [3f, 4f], 2);
        Assert.Equal(25f, dist, 1e-5f);
    }

    [Fact]
    public void DistanceSquared_SamePoint_Zero()
    {
        float dist = DistanceHelper.DistanceSquared([5f, 5f], [5f, 5f], 2);
        Assert.Equal(0f, dist, 1e-5f);
    }
}
