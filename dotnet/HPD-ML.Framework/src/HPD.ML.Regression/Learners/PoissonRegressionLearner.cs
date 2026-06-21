namespace HPD.ML.Regression;

using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;

public sealed record PoissonRegressionOptions
{
    public float L1Regularization { get; init; } = 0f;
    public float L2Regularization { get; init; } = 1f;
    public int MemorySize { get; init; } = 20;
    public double OptimizationTolerance { get; init; } = 1e-7;
    public int MaxIterations { get; init; } = 100;
}

/// <summary>
/// Poisson regression via L-BFGS with analytic gradients.
/// Model: E[y] = exp(w·x + b), trained by minimizing Poisson negative log-likelihood.
/// Useful for count data (insurance claims, event rates, healthcare).
/// </summary>
public sealed class PoissonRegressionLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly PoissonRegressionOptions _options;
    private readonly ProgressSubject _progress = new();

    public PoissonRegressionLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        PoissonRegressionOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new PoissonRegressionOptions();
    }

    public IObservable<ProgressEvent> Progress => _progress;

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column("Score", FieldType.Scalar<float>()));
        return new Schema(columns, inputSchema.Level);
    }

    public IModel Fit(LearnerInput input)
    {
        var (features, labels, featureCount) = RegressionDataLoader.Load(
            input.TrainData, _featureColumn, _labelColumn);
        int n = features.Count;
        int d = featureCount;

        // Validate: Poisson requires non-negative labels
        for (int i = 0; i < n; i++)
        {
            if (labels[i] < 0)
                throw new ArgumentException(
                    $"Poisson regression requires non-negative labels. Found {labels[i]} at row {i}.");
        }

        (double Loss, double[] Gradient) Objective(double[] parameters)
        {
            var totalLoss = 0.0;
            var gradient = new double[d + 1];

            for (int i = 0; i < n; i++)
            {
                var linearScore = parameters[d];
                for (int j = 0; j < d; j++)
                    linearScore += parameters[j] * features[i][j];

                var rate = Math.Exp(Math.Clamp(linearScore, -50.0, 50.0));
                totalLoss += rate - labels[i] * linearScore;
                var error = rate - labels[i];
                for (int j = 0; j < d; j++)
                    gradient[j] += error * features[i][j];
                gradient[d] += error;
            }

            for (int i = 0; i < gradient.Length; i++)
                gradient[i] /= n;

            return (totalLoss / n, gradient);
        }

        var optimizer = new LbfgsOptimizer(
            memorySize: _options.MemorySize,
            tolerance: _options.OptimizationTolerance,
            maxIterations: _options.MaxIterations,
            l1Regularization: _options.L1Regularization,
            l2Regularization: _options.L2Regularization);

        var initial = new double[d + 1];
        var optimized = optimizer.Minimize(Objective, initial, _progress);

        var weights = optimized[..d];
        var bias = optimized[d];

        var parameters = new LinearModelParameters(weights, bias);
        // Poisson: scoring applies exp() to the linear score
        var transform = new RegressionScoringTransform(parameters, _featureColumn, applyExp: true);

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
