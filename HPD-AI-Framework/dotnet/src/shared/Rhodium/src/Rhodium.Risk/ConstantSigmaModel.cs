using Rhodium.Primitives;

namespace Rhodium.Risk;

/// <summary>
/// Simple constant volatility model for testing.
/// Returns the same volatility estimate for all instruments.
/// </summary>
public sealed class ConstantSigmaModel : ISigmaModel
{
    private readonly double _sigma;

    /// <summary>
    /// Creates a constant volatility model.
    /// </summary>
    /// <param name="sigma">Annualized volatility (e.g., 0.20 for 20% vol)</param>
    public ConstantSigmaModel(double sigma)
    {
        if (sigma <= 0)
            throw new ArgumentException("Volatility must be positive", nameof(sigma));

        _sigma = sigma;
    }

    public double Estimate(Instrument instrument) => _sigma;

    /// <summary>
    /// Creates a low volatility model (10% annualized).
    /// </summary>
    public static ConstantSigmaModel LowVol() => new(0.10);

    /// <summary>
    /// Creates a medium volatility model (20% annualized).
    /// </summary>
    public static ConstantSigmaModel MediumVol() => new(0.20);

    /// <summary>
    /// Creates a high volatility model (40% annualized).
    /// </summary>
    public static ConstantSigmaModel HighVol() => new(0.40);
}
