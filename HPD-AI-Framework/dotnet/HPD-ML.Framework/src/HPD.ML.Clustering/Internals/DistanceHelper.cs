namespace HPD.ML.Clustering;

/// <summary>
/// Shared distance computation used by initialization strategies and learners.
/// </summary>
internal static class DistanceHelper
{
    /// <summary>Squared Euclidean distance between a point and a centroid in a flat array.</summary>
    internal static float DistanceSquared(float[] point, float[] centroids, int offset, int dim)
    {
        float sum = 0;
        for (int d = 0; d < dim; d++)
        {
            float diff = point[d] - centroids[offset + d];
            sum += diff * diff;
        }
        return sum;
    }

    /// <summary>Squared Euclidean distance between two points.</summary>
    internal static float DistanceSquared(float[] a, float[] b, int dim)
    {
        float sum = 0;
        for (int d = 0; d < dim; d++)
        {
            float diff = a[d] - b[d];
            sum += diff * diff;
        }
        return sum;
    }
}
