namespace HPD.ML.Regression;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public sealed record SdcaRegressionOptions
{
    public double L2Regularization { get; init; } = 1e-4;
    public int NumberOfIterations { get; init; } = 20;
    public double ConvergenceTolerance { get; init; } = 1e-4;
    public int? Seed { get; init; }
}

/// <summary>
/// Stochastic Dual Coordinate Ascent for regression with squared loss.
/// Dual update: Δαᵢ = (yᵢ - wᵀxᵢ - αᵢ) / (‖xᵢ‖²/(λn) + 1)
/// </summary>
public sealed class SdcaRegressionLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly SdcaRegressionOptions _options;
    private readonly ProgressSubject _progress = new();

    public SdcaRegressionLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        SdcaRegressionOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new SdcaRegressionOptions();
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
        var (featuresD, labelsD, dim) = RegressionDataLoader.Load(
            input.TrainData, _featureColumn, _labelColumn);
        int n = featuresD.Count;

        // Convert to double[][] for manual math
        var features = new double[n][];
        var labels = new double[n];
        for (int i = 0; i < n; i++)
        {
            features[i] = new double[dim];
            for (int j = 0; j < dim; j++)
                features[i][j] = (double)featuresD[i][j];
            labels[i] = labelsD[i];
        }

        double lambda = _options.L2Regularization;
        var rng = _options.Seed.HasValue ? new Random(_options.Seed.Value) : Random.Shared;

        // Initialize dual variables and primal weights
        double[] alpha = new double[n];
        double[] weights = new double[dim];
        double bias = 0;

        // Precompute ‖xᵢ‖²
        double[] sqNorms = new double[n];
        for (int i = 0; i < n; i++)
        {
            double norm = 0;
            for (int j = 0; j < dim; j++)
                norm += features[i][j] * features[i][j];
            sqNorms[i] = norm;
        }

        double prevLoss = double.MaxValue;
        for (int iter = 0; iter < _options.NumberOfIterations; iter++)
        {
            int[] perm = Enumerable.Range(0, n).ToArray();
            rng.Shuffle(perm);

            for (int t = 0; t < n; t++)
            {
                int i = perm[t];

                double score = bias;
                for (int j = 0; j < dim; j++)
                    score += weights[j] * features[i][j];

                // Dual update
                double denom = sqNorms[i] / (lambda * n) + 1.0;
                double deltaAlpha = (labels[i] - score - alpha[i]) / denom;

                alpha[i] += deltaAlpha;

                // Primal update
                double scale = deltaAlpha / (lambda * n);
                for (int j = 0; j < dim; j++)
                    weights[j] += scale * features[i][j];
                bias += scale;
            }

            // Compute primal loss
            double loss = 0;
            for (int i = 0; i < n; i++)
            {
                double score = bias;
                for (int j = 0; j < dim; j++)
                    score += weights[j] * features[i][j];
                double diff = score - labels[i];
                loss += diff * diff;
            }
            loss = loss / (2 * n) + lambda / 2 * weights.Sum(w => w * w);

            _progress.OnNext(new ProgressEvent
            {
                Epoch = iter,
                MetricValue = loss,
                MetricName = "SquaredLoss"
            });

            if (Math.Abs(prevLoss - loss) < _options.ConvergenceTolerance * Math.Abs(prevLoss))
                break;
            prevLoss = loss;
        }

        var wArr = new Double[dim];
        for (int j = 0; j < dim; j++)
            wArr[j] = new Double(weights[j]);

        var parameters = new LinearModelParameters(
            Vector<Double>.FromArray(wArr), new Double(bias));
        var transform = new RegressionScoringTransform(parameters, _featureColumn);

        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);
}
