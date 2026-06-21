namespace HPD.ML.Regression;

using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;

public sealed record OlsOptions
{
    public float L1Regularization { get; init; } = 0f;
    public float L2Regularization { get; init; } = 1f;
    public int MemorySize { get; init; } = 20;
    public double OptimizationTolerance { get; init; } = 1e-7;
    public int MaxIterations { get; init; } = 100;
}

/// <summary>
/// Ordinary Least Squares regression via L-BFGS with analytic gradients.
/// Minimizes (1/2n) Σ (w·x + b - y)² + regularization.
/// </summary>
public sealed class OrdinaryLeastSquaresLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly OlsOptions _options;
    private readonly ProgressSubject _progress = new();

    public OrdinaryLeastSquaresLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        OlsOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new OlsOptions();
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

        (double Loss, double[] Gradient) Objective(double[] parameters)
        {
            var totalLoss = 0.0;
            var gradient = new double[d + 1];

            for (int i = 0; i < n; i++)
            {
                var score = parameters[d];
                for (int j = 0; j < d; j++)
                    score += parameters[j] * features[i][j];

                var diff = score - labels[i];
                totalLoss += diff * diff;
                for (int j = 0; j < d; j++)
                    gradient[j] += diff * features[i][j];
                gradient[d] += diff;
            }

            for (int i = 0; i < gradient.Length; i++)
                gradient[i] /= n;

            return (totalLoss / (2.0 * n), gradient);
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
        var transform = new RegressionScoringTransform(parameters, _featureColumn);

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
