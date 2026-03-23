namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

/// <summary>
/// Logistic regression via L-BFGS optimization.
///
/// Loss function: -Σ[y·log(σ(w·x+b)) + (1-y)·log(1-σ(w·x+b))] / n
///
/// Helium's autodiff computes the gradient of this loss automatically.
/// L-BFGS uses gradient history to approximate the inverse Hessian.
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
                new LinearModelParameters(Vector<Double>.Zero(1), new Double(0)),
                _featureColumn)
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        var (features, labels, featureCount) = TrainingDataLoader.Load(
            input.TrainData, _featureColumn, _labelColumn);
        int n = features.Count;

        // Define loss function: negative log-likelihood
        // Parameters layout: [w0, w1, ..., w_{d-1}, bias]
        Func<Vector<Var<Double>>, Var<Double>> loss = parameters =>
        {
            int d = featureCount;
            var totalLoss = Var<Double>.Constant(new Double(0));

            for (int i = 0; i < n; i++)
            {
                // w·x + b
                var logit = parameters[d]; // bias
                for (int j = 0; j < d; j++)
                    logit = logit + parameters[j] * Var<Double>.Constant(features[i][j]);

                // Numerically stable log-loss via log(1 + exp(±z))
                var y = labels[i];
                if (y)
                    totalLoss = totalLoss + LogOnePlusExp(-logit);
                else
                    totalLoss = totalLoss + LogOnePlusExp(logit);
            }

            return totalLoss / Var<Double>.Constant(new Double(n));
        };

        // Initialize parameters to zero
        var initial = Vector<Double>.Zero(featureCount + 1); // weights + bias

        // Optimize via L-BFGS
        var optimizer = new LbfgsOptimizer(
            memorySize: _options.MemorySize,
            tolerance: _options.OptimizationTolerance,
            maxIterations: _options.MaxIterations,
            l1Regularization: _options.L1Regularization,
            l2Regularization: _options.L2Regularization);

        var optimized = optimizer.Minimize(loss, initial, _progress);

        // Extract weights and bias
        var weights = new Double[featureCount];
        for (int i = 0; i < featureCount; i++)
            weights[i] = optimized[i];
        var bias = optimized[featureCount];

        var parameters = new LinearModelParameters(
            Vector<Double>.FromArray(weights), bias);

        var transform = new LinearScoringTransform(parameters, _featureColumn);
        _progress.OnCompleted();

        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);

    /// <summary>Numerically stable log(1 + exp(x))</summary>
    private static Var<Double> LogOnePlusExp(Var<Double> x)
    {
        var expX = VarMath.Exp(x);
        var onePlusExpX = Var<Double>.Constant(new Double(1)) + expX;
        return VarMath.Log(onePlusExpX);
    }
}

public sealed record LogisticRegressionOptions
{
    public float L1Regularization { get; init; } = 0f;
    public float L2Regularization { get; init; } = 1f;
    public int MemorySize { get; init; } = 20;
    public double OptimizationTolerance { get; init; } = 1e-7;
    public int MaxIterations { get; init; } = 100;
}
