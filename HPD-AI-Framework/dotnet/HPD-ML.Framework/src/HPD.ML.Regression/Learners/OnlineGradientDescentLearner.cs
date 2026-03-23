namespace HPD.ML.Regression;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public sealed record OnlineGradientDescentOptions
{
    public double LearningRate { get; init; } = 0.1;
    public double L2Regularization { get; init; } = 0;
    public int NumberOfIterations { get; init; } = 1;
    public bool AverageWeights { get; init; } = true;
    public bool DecreaseLearningRate { get; init; } = false;
}

/// <summary>
/// Online gradient descent for regression with squared loss.
/// Processes one sample at a time, supports weight averaging and warm-start.
/// </summary>
public sealed class OnlineGradientDescentLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly OnlineGradientDescentOptions _options;
    private readonly ProgressSubject _progress = new();

    public OnlineGradientDescentLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        OnlineGradientDescentOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new OnlineGradientDescentOptions();
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
        var (featuresD, labelsD, featureCount) = RegressionDataLoader.Load(
            input.TrainData, _featureColumn, _labelColumn);
        int n = featuresD.Count;

        // Convert to double[][] for fast math
        var features = new double[n][];
        var labels = new double[n];
        for (int i = 0; i < n; i++)
        {
            features[i] = new double[featureCount];
            for (int j = 0; j < featureCount; j++)
                features[i][j] = (double)featuresD[i][j];
            labels[i] = labelsD[i];
        }

        // Initialize from initial model if provided
        double[] weights = new double[featureCount];
        double bias = 0;
        if (input.InitialModel?.Parameters is LinearModelParameters init)
        {
            for (int j = 0; j < featureCount && j < init.FeatureCount; j++)
                weights[j] = (double)init.Weights[j];
            bias = (double)init.Bias;
        }

        // Averaged weights accumulators
        double[] avgWeights = new double[featureCount];
        double avgBias = 0;
        long totalSamples = 0;

        for (int pass = 0; pass < _options.NumberOfIterations; pass++)
        {
            double passLoss = 0;

            for (int i = 0; i < n; i++)
            {
                // Learning rate with optional decay
                double lr = _options.DecreaseLearningRate
                    ? _options.LearningRate / (1.0 + pass * n + i)
                    : _options.LearningRate;

                // Predict
                double score = bias;
                for (int j = 0; j < featureCount; j++)
                    score += weights[j] * features[i][j];

                // Gradient of squared loss: (score - label)
                double grad = score - labels[i];
                passLoss += 0.5 * grad * grad;

                // Update: w -= lr * (grad * x + λw)
                for (int j = 0; j < featureCount; j++)
                    weights[j] -= lr * (grad * features[i][j] + _options.L2Regularization * weights[j]);
                bias -= lr * grad;

                // Accumulate for averaging
                if (_options.AverageWeights)
                {
                    totalSamples++;
                    for (int j = 0; j < featureCount; j++)
                        avgWeights[j] += weights[j];
                    avgBias += bias;
                }
            }

            _progress.OnNext(new ProgressEvent
            {
                Epoch = pass,
                MetricValue = passLoss / Math.Max(n, 1),
                MetricName = "SquaredLoss"
            });
        }

        // Build model
        double[] finalWeights;
        double finalBias;
        if (_options.AverageWeights && totalSamples > 0)
        {
            finalWeights = avgWeights.Select(w => w / totalSamples).ToArray();
            finalBias = avgBias / totalSamples;
        }
        else
        {
            finalWeights = weights;
            finalBias = bias;
        }

        var wArr = new Double[featureCount];
        for (int j = 0; j < featureCount; j++)
            wArr[j] = new Double(finalWeights[j]);

        var parameters = new LinearModelParameters(
            Vector<Double>.FromArray(wArr), new Double(finalBias));
        var transform = new RegressionScoringTransform(parameters, _featureColumn);

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
