namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

/// <summary>
/// Stochastic Dual Coordinate Ascent for binary classification.
///
/// SDCA works on the dual formulation of the regularized loss.
/// For logistic loss: updates dual variables α_i and primal weights w simultaneously.
///
/// Unlike L-BFGS, SDCA doesn't need to hold the full gradient in memory —
/// it updates one dual variable at a time, making it efficient for large datasets.
/// </summary>
public sealed class SdcaLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly SdcaOptions _options;
    private readonly ProgressSubject _progress = new();

    public SdcaLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        SdcaOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new SdcaOptions();
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
        double lambda = _options.L2Regularization;

        // Initialize primal (w) and dual (alpha) variables
        var w = new double[featureCount];
        var bias = 0.0;
        var alpha = new double[n]; // dual variables

        var rng = _options.Seed.HasValue ? new Random(_options.Seed.Value) : Random.Shared;

        for (int epoch = 0; epoch < _options.NumberOfIterations; epoch++)
        {
            // Shuffle sample order
            var order = Enumerable.Range(0, n).ToArray();
            rng.Shuffle(order);

            double epochLoss = 0;

            foreach (int i in order)
            {
                var x = features[i];
                double y = labels[i] ? 1.0 : -1.0;

                // Compute w·x + b
                double wx = bias;
                for (int j = 0; j < featureCount; j++)
                    wx += w[j] * (double)x[j];

                // Compute optimal dual update for log-loss
                double p = 1.0 / (1.0 + Math.Exp(-wx));
                double dualUpdate = (y > 0 ? p - 1.0 : p) - alpha[i];

                // Scale by step size
                double scaling = 1.0 / (lambda * n);
                dualUpdate *= scaling;

                // Update dual
                alpha[i] += dualUpdate;

                // Update primal: w += dualUpdate * x
                for (int j = 0; j < featureCount; j++)
                    w[j] += dualUpdate * (double)x[j];
                bias += dualUpdate;

                epochLoss += Math.Log(1.0 + Math.Exp(-y * wx));
            }

            _progress.OnNext(new ProgressEvent
            {
                Epoch = epoch,
                MetricValue = epochLoss / n,
                MetricName = "LogLoss"
            });

            // Check convergence
            if (epoch > 0 && epochLoss / n < _options.ConvergenceTolerance)
                break;
        }

        var weights = new Double[featureCount];
        for (int i = 0; i < featureCount; i++)
            weights[i] = new Double(w[i]);

        var parameters = new LinearModelParameters(
            Vector<Double>.FromArray(weights), new Double(bias));
        var transform = new LinearScoringTransform(parameters, _featureColumn);
        _progress.OnCompleted();

        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}

public sealed record SdcaOptions
{
    public double L2Regularization { get; init; } = 1e-4;
    public int NumberOfIterations { get; init; } = 20;
    public double ConvergenceTolerance { get; init; } = 1e-4;
    public int? Seed { get; init; }
}
