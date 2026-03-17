using Rhodium.Primitives;

namespace Rhodium.Risk;

/// <summary>
/// Volatility/measure model for risk calculations.
/// </summary>
public interface ISigmaModel
{
    /// <summary>
    /// Estimates volatility (sigma) for the given instrument.
    /// </summary>
    double Estimate(Instrument instrument);
}
