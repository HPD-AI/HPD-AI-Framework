namespace HPD.ML.BinaryClassification;

using HPD.ML.Abstractions;

/// <summary>
/// Learned parameters for any linear binary classifier.
/// Weights + bias, optionally with per-feature statistics.
/// </summary>
public sealed class LinearModelParameters : ILearnedParameters
{
    private readonly double[] _weights;

    public IReadOnlyList<double> Weights => _weights;
    public double Bias { get; }

    /// <summary>Optional: feature names for interpretability.</summary>
    public IReadOnlyList<string>? FeatureNames { get; init; }

    /// <summary>Optional: per-weight statistics (z-score, p-value) for logistic regression.</summary>
    public IReadOnlyList<WeightStatistics>? Statistics { get; init; }

    public LinearModelParameters(ReadOnlySpan<double> weights, double bias)
    {
        _weights = weights.ToArray();
        Bias = bias;
    }

    /// <summary>Number of features.</summary>
    public int FeatureCount => _weights.Length;
}

public sealed record WeightStatistics(
    double Weight,
    double StandardError,
    double ZScore,
    double PValue);
