namespace HPD.ML.Clustering;

/// <summary>
/// K-Means|| (parallel) initialization (Bahmani et al., VLDB 2012).
/// O(log N) passes, samples O(K) points per pass, then reduces via K-Means++.
/// </summary>
internal static class KMeansParallelInit
{
    internal static float[] Initialize(float[][] data, int k, int dim, Random rng)
    {
        int n = data.Length;
        int oversampleFactor = 2 * k;
        int rounds = Math.Max(5, (int)Math.Ceiling(Math.Log(n)));

        // Start with one random centroid
        var candidates = new List<float[]>();
        int firstIdx = rng.Next(n);
        candidates.Add((float[])data[firstIdx].Clone());

        var minDistSq = new float[n];
        Array.Fill(minDistSq, float.MaxValue);

        for (int round = 0; round < rounds; round++)
        {
            // Update min distances to ALL candidates (not just new ones)
            for (int i = 0; i < n; i++)
            {
                foreach (var candidate in candidates)
                {
                    float dist = DistanceHelper.DistanceSquared(data[i], candidate, dim);
                    if (dist < minDistSq[i])
                        minDistSq[i] = dist;
                }
            }

            double totalDist = 0;
            for (int i = 0; i < n; i++)
                totalDist += minDistSq[i];

            if (totalDist < 1e-10) break;

            // Sample O(K) new candidates with probability proportional to D²
            int prevCount = candidates.Count;
            for (int i = 0; i < n; i++)
            {
                double prob = oversampleFactor * minDistSq[i] / totalDist;
                if (rng.NextDouble() < prob)
                    candidates.Add((float[])data[i].Clone());
            }

            // If no new candidates were added, stop early
            if (candidates.Count == prevCount) break;
        }

        // Reduce candidates to K centroids via K-Means++
        if (candidates.Count <= k)
        {
            while (candidates.Count < k)
                candidates.Add((float[])data[rng.Next(n)].Clone());
        }

        return KMeansPlusPlusInit.Initialize(candidates.ToArray(), k, dim, rng);
    }
}
