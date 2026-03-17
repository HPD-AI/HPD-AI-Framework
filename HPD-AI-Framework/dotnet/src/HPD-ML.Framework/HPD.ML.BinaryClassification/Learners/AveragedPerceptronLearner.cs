namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

/// <summary>
/// Online averaged perceptron.
///
/// Updates weights when a prediction is wrong, averages weights across all iterations.
/// The averaged weights generalize better than the final weights (voted perceptron theory).
///
/// Supports online learning via InitialModel — resume training on new data.
/// </summary>
public sealed class AveragedPerceptronLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly AveragedPerceptronOptions _options;
    private readonly ProgressSubject _progress = new();

    public AveragedPerceptronLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        AveragedPerceptronOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new AveragedPerceptronOptions();
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

        // Initialize from InitialModel if provided (online learning)
        double[] w, avgW;
        double bias, avgBias;
        int t = 0;

        if (input.InitialModel?.Parameters is LinearModelParameters initial)
        {
            w = new double[featureCount];
            for (int i = 0; i < featureCount; i++)
                w[i] = (double)initial.Weights[i];
            bias = (double)initial.Bias;
        }
        else
        {
            w = new double[featureCount];
            bias = 0;
        }

        avgW = new double[featureCount];
        Array.Copy(w, avgW, featureCount);
        avgBias = bias;

        for (int epoch = 0; epoch < _options.NumberOfIterations; epoch++)
        {
            int errors = 0;

            for (int i = 0; i < n; i++)
            {
                t++;
                double y = labels[i] ? 1.0 : -1.0;

                // Predict
                double wx = bias;
                for (int j = 0; j < featureCount; j++)
                    wx += w[j] * (double)features[i][j];

                double prediction = wx >= 0 ? 1.0 : -1.0;

                // Update on error (or margin violation for hinge loss)
                double lr = _options.DecreaseLearningRate
                    ? _options.LearningRate / (1.0 + t * 0.001)
                    : _options.LearningRate;

                if (y * wx < 1.0) // hinge loss margin
                {
                    for (int j = 0; j < featureCount; j++)
                        w[j] += lr * y * (double)features[i][j];
                    bias += lr * y;

                    if (prediction != y) errors++;
                }

                // L2 regularization
                if (_options.L2Regularization > 0)
                {
                    double decay = 1.0 - lr * _options.L2Regularization;
                    for (int j = 0; j < featureCount; j++)
                        w[j] *= decay;
                }

                // Running average
                double alpha = 1.0 / t;
                for (int j = 0; j < featureCount; j++)
                    avgW[j] = avgW[j] * (1 - alpha) + w[j] * alpha;
                avgBias = avgBias * (1 - alpha) + bias * alpha;
            }

            double errorRate = (double)errors / n;
            _progress.OnNext(new ProgressEvent
            {
                Epoch = epoch,
                MetricValue = errorRate,
                MetricName = "ErrorRate"
            });
        }

        // Use averaged weights for final model
        var weights = new Double[featureCount];
        for (int i = 0; i < featureCount; i++)
            weights[i] = new Double(avgW[i]);

        var parameters = new LinearModelParameters(
            Vector<Double>.FromArray(weights), new Double(avgBias));

        // Perceptron outputs uncalibrated scores — add Platt scaling if validation data available
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

public sealed record AveragedPerceptronOptions
{
    public double LearningRate { get; init; } = 1.0;
    public bool DecreaseLearningRate { get; init; } = false;
    public double L2Regularization { get; init; } = 0;
    public int NumberOfIterations { get; init; } = 10;
}
