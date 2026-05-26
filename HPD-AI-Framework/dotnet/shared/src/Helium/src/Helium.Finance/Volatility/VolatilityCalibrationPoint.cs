using Helium.Finance.Options;

namespace Helium.Finance.Volatility;

public readonly record struct VolatilityCalibrationPoint
{
    public VolatilityCalibrationPoint(
        double TimeToExpiry,
        double Strike,
        double MarketPrice,
        ImpliedVolatilityResult ImpliedVolatility)
    {
        if (ImpliedVolatility.Converged != (ImpliedVolatility.Status == ImpliedVolatilityStatus.Converged))
            throw new ArgumentOutOfRangeException(nameof(ImpliedVolatility), "Implied-volatility convergence flag and status must agree.");

        if (ImpliedVolatility.Converged)
        {
            if (!double.IsFinite(TimeToExpiry) || TimeToExpiry < 0.0)
                throw new ArgumentOutOfRangeException(nameof(TimeToExpiry), "Converged calibration time must be finite and nonnegative.");

            if (!double.IsFinite(Strike))
                throw new ArgumentOutOfRangeException(nameof(Strike), "Converged calibration strike must be finite.");

            if (!double.IsFinite(MarketPrice))
                throw new ArgumentOutOfRangeException(nameof(MarketPrice), "Converged calibration market price must be finite.");

            if (!double.IsFinite(ImpliedVolatility.Volatility) || ImpliedVolatility.Volatility < 0.0)
                throw new ArgumentOutOfRangeException(nameof(ImpliedVolatility), "Converged calibration volatility must be finite and nonnegative.");
        }

        this.TimeToExpiry = TimeToExpiry;
        this.Strike = Strike;
        this.MarketPrice = MarketPrice;
        this.ImpliedVolatility = ImpliedVolatility;
    }

    public double TimeToExpiry { get; }

    public double Strike { get; }

    public double MarketPrice { get; }

    public ImpliedVolatilityResult ImpliedVolatility { get; }

    public bool Converged => ImpliedVolatility.Converged;

    public double Volatility => ImpliedVolatility.Volatility;

    public void Deconstruct(
        out double TimeToExpiry,
        out double Strike,
        out double MarketPrice,
        out ImpliedVolatilityResult ImpliedVolatility)
    {
        TimeToExpiry = this.TimeToExpiry;
        Strike = this.Strike;
        MarketPrice = this.MarketPrice;
        ImpliedVolatility = this.ImpliedVolatility;
    }
}
