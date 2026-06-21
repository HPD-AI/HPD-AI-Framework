namespace HPD.ML.Clustering;

/// <summary>
/// K-Means++ initialization (Arthur and Vassilvitskii, 2007).
/// Sequential: O(N·K) distance computations.
/// </summary>
internal static class KMeansPlusPlusInit
{
    internal static float[] Initialize(float[][] data, int k, int dim, Random rng)
    {
        int n = data.Length;
        var centroids = new float[k * dim];

        // Pick first centroid uniformly at random
        int firstIdx = rng.Next(n);
        Array.Copy(data[firstIdx], 0, centroids, 0, dim);

        // D² weighting for subsequent centroids
        var minDistSq = new float[n];
        Array.Fill(minDistSq, float.MaxValue);

        for (int c = 1; c < k; c++)
        {
            // Update min distances to the most recently added centroid
            int prevOffset = (c - 1) * dim;
            double totalWeight = 0;
            for (int i = 0; i < n; i++)
            {
                float dist = DistanceHelper.DistanceSquared(data[i], centroids, prevOffset, dim);
                if (dist < minDistSq[i])
                    minDistSq[i] = dist;
                totalWeight += minDistSq[i];
            }

            // Weighted random selection
            double target = rng.NextDouble() * totalWeight;
            double cumulative = 0;
            int selected = n - 1;
            for (int i = 0; i < n; i++)
            {
                cumulative += minDistSq[i];
                if (cumulative >= target)
                {
                    selected = i;
                    break;
                }
            }

            Array.Copy(data[selected], 0, centroids, c * dim, dim);
        }

        return centroids;
    }
}
