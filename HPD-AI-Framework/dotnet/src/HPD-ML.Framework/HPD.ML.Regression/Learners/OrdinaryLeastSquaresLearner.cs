namespace HPD.ML.Regression;

using Helium.Algebra;
using Helium.Algorithms;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public sealed record OlsOptions
{
    public float L1Regularization { get; init; } = 0f;
    public float L2Regularization { get; init; } = 1f;
    public int MemorySize { get; init; } = 20;
    public double OptimizationTolerance { get; init; } = 1e-7;
    public int MaxIterations { get; init; } = 100;
}

/// <summary>
/// Ordinary Least Squares regression via L-BFGS with Helium autodiff.
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

        // Loss: (1/2n) Σ (w·x + b - y)²
        // Parameters layout: [w0, w1, ..., w_{d-1}, bias]
        Func<Vector<Var<Double>>, Var<Double>> loss = parameters =>
        {
            var totalLoss = Var<Double>.Constant(new Double(0));

            for (int i = 0; i < n; i++)
            {
                var score = parameters[d]; // bias
                for (int j = 0; j < d; j++)
                    score = score + parameters[j] * Var<Double>.Constant(features[i][j]);

                var diff = score - Var<Double>.Constant(new Double(labels[i]));
                totalLoss = totalLoss + diff * diff;
            }

            return totalLoss / Var<Double>.Constant(new Double(2.0 * n));
        };

        var optimizer = new LbfgsOptimizer(
            memorySize: _options.MemorySize,
            tolerance: _options.OptimizationTolerance,
            maxIterations: _options.MaxIterations,
            l1Regularization: _options.L1Regularization,
            l2Regularization: _options.L2Regularization);

        var initial = Vector<Double>.Zero(d + 1);
        var optimized = optimizer.Minimize(loss, initial, _progress);

        // Extract weights and bias
        var weights = new Double[d];
        for (int i = 0; i < d; i++)
            weights[i] = optimized[i];
        var bias = optimized[d];

        var parameters = new LinearModelParameters(
            Vector<Double>.FromArray(weights), bias);
        var transform = new RegressionScoringTransform(parameters, _featureColumn);

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
