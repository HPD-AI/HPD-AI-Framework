namespace HPD.ML.Clustering;

using HPD.ML.Abstractions;

/// <summary>
/// Learned K-Means model: K centroids with precomputed norms.
/// Distance computation uses: ||x - c||² = ||x||² - 2·dot(x, c) + ||c||²
/// </summary>
public sealed class ClusteringModelParameters : ILearnedParameters
{
    /// <summary>Number of clusters.</summary>
    public int K { get; }

    /// <summary>Feature dimensionality.</summary>
    public int Dimensionality { get; }

    /// <summary>Centroid coordinates. Row-major: centroids[k * Dimensionality + d].</summary>
    public float[] Centroids { get; }

    /// <summary>Precomputed ||centroid[k]||² for each cluster.</summary>
    public float[] CentroidNormsSquared { get; }

    public ClusteringModelParameters(int k, int dimensionality, float[] centroids)
    {
        K = k;
        Dimensionality = dimensionality;
        Centroids = centroids;

        CentroidNormsSquared = new float[k];
        for (int i = 0; i < k; i++)
        {
            float norm = 0;
            int offset = i * dimensionality;
            for (int d = 0; d < dimensionality; d++)
                norm += centroids[offset + d] * centroids[offset + d];
            CentroidNormsSquared[i] = norm;
        }
    }

    /// <summary>Get centroid k as a span.</summary>
    public ReadOnlySpan<float> GetCentroid(int k)
        => Centroids.AsSpan(k * Dimensionality, Dimensionality);

    /// <summary>Compute squared Euclidean distance from features to centroid k.</summary>
    public float DistanceSquared(ReadOnlySpan<float> features, int k)
    {
        float dot = 0;
        float xNorm = 0;
        int offset = k * Dimensionality;
        for (int d = 0; d < features.Length; d++)
        {
            dot += features[d] * Centroids[offset + d];
            xNorm += features[d] * features[d];
        }
        return Math.Max(0, xNorm - 2 * dot + CentroidNormsSquared[k]);
    }

    /// <summary>Find nearest cluster and return (clusterIndex, distanceSquared).</summary>
    public (int Cluster, float Distance) NearestCluster(ReadOnlySpan<float> features)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int k = 0; k < K; k++)
        {
            float dist = DistanceSquared(features, k);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = k;
            }
        }
        return (best, bestDist);
    }
}
