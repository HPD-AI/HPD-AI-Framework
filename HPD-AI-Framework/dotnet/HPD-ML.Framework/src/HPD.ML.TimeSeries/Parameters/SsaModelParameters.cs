namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;

/// <summary>
/// Learned SSA model parameters: SVD decomposition of the trajectory matrix
/// and derived auto-regressive coefficients. Immutable after training.
/// </summary>
public sealed class SsaModelParameters : ILearnedParameters
{
    /// <summary>Window size (L): rows of the trajectory matrix.</summary>
    public int WindowSize { get; }

    /// <summary>Series length used for training (N).</summary>
    public int SeriesLength { get; }

    /// <summary>Rank: number of SVD components retained.</summary>
    public int Rank { get; }

    /// <summary>
    /// Auto-regressive coefficients derived from SVD.
    /// forecast[t] = Σ alpha[i] * series[t - L + i] for i in 0..L-2
    /// </summary>
    public double[] AutoRegressiveCoefficients { get; }

    /// <summary>SVD eigenvectors (Rank × L matrix, column-major: evecs[k * L + i]).</summary>
    public double[] Eigenvectors { get; }

    /// <summary>SVD singular values (length = Rank).</summary>
    public double[] SingularValues { get; }

    /// <summary>Initial SSA state vector (length = Rank). Seed for inference.</summary>
    public double[] InitialStateVector { get; }

    public SsaModelParameters(
        int windowSize, int seriesLength, int rank,
        double[] autoRegressiveCoefficients,
        double[] eigenvectors, double[] singularValues,
        double[] initialStateVector)
    {
        WindowSize = windowSize;
        SeriesLength = seriesLength;
        Rank = rank;
        AutoRegressiveCoefficients = autoRegressiveCoefficients;
        Eigenvectors = eigenvectors;
        SingularValues = singularValues;
        InitialStateVector = initialStateVector;
    }
}
