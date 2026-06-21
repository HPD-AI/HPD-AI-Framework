namespace HPD.ML.BinaryClassification;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Logistic regression via L-BFGS optimization.
///
/// Loss function: -Σ[y·log(σ(w·x+b)) + (1-y)·log(1-σ(w·x+b))] / n
///
/// L-BFGS uses analytic gradients and gradient history to approximate the inverse Hessian.
/// </summary>
public sealed class LogisticRegressionLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly LogisticRegressionOptions _options;
    private readonly ProgressSubject _progress = new();

    public LogisticRegressionLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        LogisticRegressionOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new LogisticRegressionOptions();
    }

    public IObservable<ProgressEvent> Progress => _progress;

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new LinearScoringTransform(
                new LinearModelParameters([0.0], 0.0),
                _featureColumn)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        var (features, labels, featureCount) = TrainingDataLoader.Load(
            input.TrainData, _featureColumn, _labelColumn);
        int n = features.Count;

        (double Loss, double[] Gradient) Objective(double[] parameters)
        {
            int d = featureCount;
            var totalLoss = 0.0;
            var gradient = new double[d + 1];

            for (int i = 0; i < n; i++)
            {
                var logit = parameters[d];
                for (int j = 0; j < d; j++)
                    logit += parameters[j] * features[i][j];

                var y = labels[i];
                totalLoss += y ? LogOnePlusExp(-logit) : LogOnePlusExp(logit);

                var probability = Sigmoid(logit);
                var error = probability - (y ? 1.0 : 0.0);
                for (int j = 0; j < d; j++)
                    gradient[j] += error * features[i][j];
                gradient[d] += error;
            }

            for (int i = 0; i < gradient.Length; i++)
                gradient[i] /= n;

            return (totalLoss / n, gradient);
        }

        var initial = new double[featureCount + 1];

        var optimizer = new LbfgsOptimizer(
            memorySize: _options.MemorySize,
            tolerance: _options.OptimizationTolerance,
            maxIterations: _options.MaxIterations,
            l1Regularization: _options.L1Regularization,
            l2Regularization: _options.L2Regularization);

        var optimized = optimizer.Minimize(Objective, initial, _progress);
        var weights = optimized[..featureCount];
        var bias = optimized[featureCount];

        var parameters = new LinearModelParameters(weights, bias);
        var transform = new LinearScoringTransform(parameters, _featureColumn);
        _progress.OnCompleted();

        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);

    private static double LogOnePlusExp(double x) =>
        x > 0 ? x + Math.Log(1.0 + Math.Exp(-x)) : Math.Log(1.0 + Math.Exp(x));

    private static double Sigmoid(double x) =>
        x >= 0 ? 1.0 / (1.0 + Math.Exp(-x)) : Math.Exp(x) / (1.0 + Math.Exp(x));
}

public sealed record LogisticRegressionOptions
{
    public float L1Regularization { get; init; } = 0f;
    public float L2Regularization { get; init; } = 1f;
    public int MemorySize { get; init; } = 20;
    public double OptimizationTolerance { get; init; } = 1e-7;
    public int MaxIterations { get; init; } = 100;
}
