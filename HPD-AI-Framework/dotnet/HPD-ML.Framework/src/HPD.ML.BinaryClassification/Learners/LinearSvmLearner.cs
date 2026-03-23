namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

/// <summary>
/// Linear SVM via PEGASOS (Primal Estimated sub-GrAdient SOlver for SVM).
///
/// Minimizes: (λ/2)||w||² + (1/n)Σmax(0, 1 - y(w·x + b))
///
/// PEGASOS alternates between SGD steps on the hinge loss
/// and projection to the L2 ball of radius 1/√λ.
/// </summary>
public sealed class LinearSvmLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly LinearSvmOptions _options;
    private readonly ProgressSubject _progress = new();

    public LinearSvmLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        LinearSvmOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new LinearSvmOptions();
    }

    public IObservable<ProgressEvent> Progress => _progress;

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new LinearScoringTransform(
                new LinearModelParameters(Vector<Double>.Zero(1), new Double(0)),
                _featureColumn)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        var (features, labels, featureCount) = TrainingDataLoader.Load(
            input.TrainData, _featureColumn, _labelColumn);
        int n = features.Count;

        var w = new double[featureCount];
        var bias = 0.0;
        double lambda = _options.Lambda;

        var rng = _options.Seed.HasValue ? new Random(_options.Seed.Value) : Random.Shared;
        int t = 0;

        for (int epoch = 0; epoch < _options.NumberOfIterations; epoch++)
        {
            var order = Enumerable.Range(0, n).ToArray();
            rng.Shuffle(order);

            double epochLoss = 0;

            foreach (int i in order)
            {
                t++;
                double eta = 1.0 / (lambda * t); // learning rate = 1/(λt)
                double y = labels[i] ? 1.0 : -1.0;

                // Compute w·x + b
                double wx = bias;
                for (int j = 0; j < featureCount; j++)
                    wx += w[j] * (double)features[i][j];

                double margin = y * wx;

                // Hinge loss subgradient
                if (margin < 1.0)
                {
                    for (int j = 0; j < featureCount; j++)
                        w[j] = (1.0 - eta * lambda) * w[j] + eta * y * (double)features[i][j];
                    if (!_options.NoBias)
                        bias += eta * y;
                    epochLoss += 1.0 - margin;
                }
                else
                {
                    // Only regularization update
                    for (int j = 0; j < featureCount; j++)
                        w[j] *= (1.0 - eta * lambda);
                }

                // PEGASOS projection: ||w|| ≤ 1/√λ
                if (_options.PerformProjection)
                {
                    double norm = 0;
                    for (int j = 0; j < featureCount; j++)
                        norm += w[j] * w[j];
                    norm = Math.Sqrt(norm);

                    double maxNorm = 1.0 / Math.Sqrt(lambda);
                    if (norm > maxNorm)
                    {
                        double scale = maxNorm / norm;
                        for (int j = 0; j < featureCount; j++)
                            w[j] *= scale;
                    }
                }
            }

            _progress.OnNext(new ProgressEvent
            {
                Epoch = epoch,
                MetricValue = epochLoss / n,
                MetricName = "HingeLoss"
            });
        }

        var weights = new Double[featureCount];
        for (int i = 0; i < featureCount; i++)
            weights[i] = new Double(w[i]);

        var parameters = new LinearModelParameters(
            Vector<Double>.FromArray(weights), new Double(bias));

        // SVM outputs uncalibrated scores — add Platt scaling if validation data available
        ITransform transform = new LinearScoringTransform(parameters, _featureColumn);

        if (input.ValidationData is not null)
        {
            var scored = transform.Apply(input.ValidationData);
            var calibrator = CalibratorTransform.Fit(scored, _labelColumn);
            transform = TransformComposer.Compose(transform, calibrator);
        }

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}

public sealed record LinearSvmOptions
{
    public double Lambda { get; init; } = 0.001;
    public int NumberOfIterations { get; init; } = 10;
    public bool PerformProjection { get; init; } = true;
    public bool NoBias { get; init; } = false;
    public int? Seed { get; init; }
}
