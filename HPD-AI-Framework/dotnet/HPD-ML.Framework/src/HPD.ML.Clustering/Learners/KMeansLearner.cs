namespace HPD.ML.Clustering;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>Initialization strategy for K-Means.</summary>
public enum KMeansInitialization
{
    /// <summary>K-Means++ (sequential D² sampling).</summary>
    KMeansPlusPlus,
    /// <summary>K-Means|| (parallel oversampling). Default.</summary>
    KMeansParallel,
    /// <summary>Uniform random sampling.</summary>
    Random
}

/// <summary>Options for KMeansLearner.</summary>
public sealed record KMeansOptions
{
    public int NumberOfClusters { get; init; } = 5;
    public int MaxIterations { get; init; } = 1000;
    public float ConvergenceTolerance { get; init; } = 1e-7f;
    public KMeansInitialization Initialization { get; init; } = KMeansInitialization.KMeansParallel;
    public int? Seed { get; init; }
}

/// <summary>
/// Batch K-Means clustering via Lloyd's algorithm.
/// </summary>
public sealed class KMeansLearner : ILearner
{
    private readonly string _featureColumn;
    private readonly KMeansOptions _options;
    private readonly ProgressSubject _progress = new();

    public KMeansLearner(string featureColumn = "Features", KMeansOptions? options = null)
    {
        _featureColumn = featureColumn;
        _options = options ?? new KMeansOptions();
    }

    public IObservable<ProgressEvent> Progress => _progress;

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new ClusteringScoringTransform(
                new ClusteringModelParameters(_options.NumberOfClusters, 1, new float[_options.NumberOfClusters]),
                _featureColumn)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        var rng = SeededRandom.Create(_options.Seed);
        var (data, dim) = ClusteringDataLoader.Load(input.TrainData, _featureColumn);
        int n = data.Length;
        int k = _options.NumberOfClusters;

        if (n < k)
            throw new ArgumentException($"Need at least {k} data points for {k} clusters, got {n}.");

        // Initialize centroids
        float[] centroids = _options.Initialization switch
        {
            KMeansInitialization.KMeansPlusPlus => KMeansPlusPlusInit.Initialize(data, k, dim, rng),
            KMeansInitialization.Random => RandomInit.Initialize(data, k, dim, rng),
            _ => KMeansParallelInit.Initialize(data, k, dim, rng)
        };

        // Lloyd's iterations
        var assignments = new int[n];
        float prevAvgDist = float.MaxValue;

        for (int iter = 0; iter < _options.MaxIterations; iter++)
        {
            // Assignment step
            float totalDist = 0;
            int changed = 0;

            for (int i = 0; i < n; i++)
            {
                int bestK = 0;
                float bestDist = float.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    float dist = DistanceHelper.DistanceSquared(data[i], centroids, c * dim, dim);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestK = c;
                    }
                }

                if (assignments[i] != bestK) changed++;
                assignments[i] = bestK;
                totalDist += bestDist;
            }

            float avgDist = totalDist / n;

            _progress.OnNext(new ProgressEvent
            {
                Epoch = iter,
                MetricValue = avgDist,
                MetricName = "AverageDistance"
            });

            // Convergence check
            if (changed == 0)
                break;
            if (Math.Abs(prevAvgDist - avgDist) < _options.ConvergenceTolerance * Math.Abs(prevAvgDist))
                break;

            prevAvgDist = avgDist;

            // Update step
            var sums = new float[k * dim];
            var counts = new int[k];

            for (int i = 0; i < n; i++)
            {
                int c = assignments[i];
                counts[c]++;
                int offset = c * dim;
                for (int d = 0; d < dim; d++)
                    sums[offset + d] += data[i][d];
            }

            for (int c = 0; c < k; c++)
            {
                if (counts[c] == 0) continue; // Keep previous centroid for empty clusters
                int offset = c * dim;
                for (int d = 0; d < dim; d++)
                    centroids[offset + d] = sums[offset + d] / counts[c];
            }
        }

        var parameters = new ClusteringModelParameters(k, dim, centroids);
        var transform = new ClusteringScoringTransform(parameters, _featureColumn);

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
