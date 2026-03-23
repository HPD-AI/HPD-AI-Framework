namespace HPD.ML.Clustering;

/// <summary>
/// Random initialization: pick K data points uniformly at random using Fisher-Yates partial shuffle.
/// </summary>
internal static class RandomInit
{
    internal static float[] Initialize(float[][] data, int k, int dim, Random rng)
    {
        int n = data.Length;
        var centroids = new float[k * dim];

        // Fisher-Yates partial shuffle to select K distinct indices
        var indices = new int[n];
        for (int i = 0; i < n; i++) indices[i] = i;

        int count = Math.Min(k, n);
        for (int i = 0; i < count; i++)
        {
            int j = rng.Next(i, n);
            (indices[i], indices[j]) = (indices[j], indices[i]);
            Array.Copy(data[indices[i]], 0, centroids, i * dim, dim);
        }

        return centroids;
    }
}
