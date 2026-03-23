namespace HPD.ML.Clustering;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>Options for MiniBatchKMeansLearner.</summary>
public sealed record MiniBatchKMeansOptions
{
    public int NumberOfClusters { get; init; } = 5;
    public int MaxIterations { get; init; } = 100;
    public int BatchSize { get; init; } = 1000;
    public float ConvergenceTolerance { get; init; } = 1e-4f;
    public KMeansInitialization Initialization { get; init; } = KMeansInitialization.KMeansParallel;
    public int? Seed { get; init; }
}

/// <summary>
/// Mini-Batch K-Means (Sculley, 2010).
/// Samples a mini-batch per iteration instead of full dataset passes.
/// </summary>
public sealed class MiniBatchKMeansLearner : ILearner
{
    private readonly string _featureColumn;
    private readonly MiniBatchKMeansOptions _options;
    private readonly ProgressSubject _progress = new();

    public MiniBatchKMeansLearner(string featureColumn = "Features", MiniBatchKMeansOptions? options = null)
    {
        _featureColumn = featureColumn;
        _options = options ?? new MiniBatchKMeansOptions();
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
        int batchSize = Math.Min(_options.BatchSize, n);

        if (n < k)
            throw new ArgumentException($"Need at least {k} data points for {k} clusters, got {n}.");

        // Initialize centroids
        float[] centroids = _options.Initialization switch
        {
            KMeansInitialization.KMeansPlusPlus => KMeansPlusPlusInit.Initialize(data, k, dim, rng),
            KMeansInitialization.Random => RandomInit.Initialize(data, k, dim, rng),
            _ => KMeansParallelInit.Initialize(data, k, dim, rng)
        };

        var centroidCounts = new long[k];
        float prevAvgDist = float.MaxValue;

        for (int iter = 0; iter < _options.MaxIterations; iter++)
        {
            // Sample mini-batch
            var batch = new int[batchSize];
            for (int i = 0; i < batchSize; i++)
                batch[i] = rng.Next(n);

            // Assign batch points
            var batchAssignments = new int[batchSize];
            float totalDist = 0;

            for (int b = 0; b < batchSize; b++)
            {
                int idx = batch[b];
                int bestK = 0;
                float bestDist = float.MaxValue;
                for (int c = 0; c < k; c++)
                {
                    float dist = DistanceHelper.DistanceSquared(data[idx], centroids, c * dim, dim);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestK = c;
                    }
                }
                batchAssignments[b] = bestK;
                totalDist += bestDist;
            }

            float avgDist = totalDist / batchSize;

            _progress.OnNext(new ProgressEvent
            {
                Epoch = iter,
                MetricValue = avgDist,
                MetricName = "AverageDistance"
            });

            // Update centroids with per-sample learning rate
            for (int b = 0; b < batchSize; b++)
            {
                int c = batchAssignments[b];
                int idx = batch[b];
                centroidCounts[c]++;

                float eta = 1.0f / centroidCounts[c];
                int offset = c * dim;
                for (int d = 0; d < dim; d++)
                    centroids[offset + d] = (1 - eta) * centroids[offset + d] + eta * data[idx][d];
            }

            // Convergence check
            if (Math.Abs(prevAvgDist - avgDist) < _options.ConvergenceTolerance * Math.Abs(prevAvgDist))
                break;
            prevAvgDist = avgDist;
        }

        var parameters = new ClusteringModelParameters(k, dim, centroids);
        var transform = new ClusteringScoringTransform(parameters, _featureColumn);

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
