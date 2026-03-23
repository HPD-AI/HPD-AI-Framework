namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using Double = Helium.Primitives.Double;

/// <summary>
/// Learned parameters for any linear binary classifier.
/// Weights + bias, optionally with per-feature statistics.
/// </summary>
public sealed class LinearModelParameters : ILearnedParameters
{
    public Vector<Double> Weights { get; }
    public Double Bias { get; }

    /// <summary>Optional: feature names for interpretability.</summary>
    public IReadOnlyList<string>? FeatureNames { get; init; }

    /// <summary>Optional: per-weight statistics (z-score, p-value) for logistic regression.</summary>
    public IReadOnlyList<WeightStatistics>? Statistics { get; init; }

    public LinearModelParameters(Vector<Double> weights, Double bias)
    {
        Weights = weights;
        Bias = bias;
    }

    /// <summary>Number of features.</summary>
    public int FeatureCount => Weights.Length;
}

public sealed record WeightStatistics(
    double Weight,
    double StandardError,
    double ZScore,
    double PValue);
