namespace Helium.Finance.Options;

public readonly record struct ImpliedVolatilityOptions(
    double LowerVolatility = 0.0,
    double UpperVolatility = 5.0,
    double PriceTolerance = 1e-12,
    int MaxIterations = 100,
    int MaxBracketExpansions = 100)
{
    public ImpliedVolatilityOptions()
        : this(0.0, 5.0, 1e-12, 100, 100)
    {
    }

    public static ImpliedVolatilityOptions Default { get; } = new(0.0, 5.0, 1e-12, 100, 100);

    public ImpliedVolatilityOptions Normalize(double defaultUpperVolatility = 5.0)
    {
        if (!TryNormalize(out var normalized, defaultUpperVolatility))
            throw new ArgumentOutOfRangeException(nameof(ImpliedVolatilityOptions), "Implied-volatility solver settings are invalid.");

        return normalized;
    }

    public bool TryNormalize(out ImpliedVolatilityOptions normalized, double defaultUpperVolatility = 5.0)
    {
        normalized = default;
        if (!double.IsFinite(defaultUpperVolatility) || defaultUpperVolatility <= 0.0)
            return false;

        if (this == default)
        {
            normalized = new ImpliedVolatilityOptions(
                LowerVolatility: 0.0,
                UpperVolatility: defaultUpperVolatility,
                PriceTolerance: 1e-12,
                MaxIterations: 100,
                MaxBracketExpansions: 100);
            return true;
        }

        if (!double.IsFinite(LowerVolatility) || LowerVolatility < 0.0)
            return false;

        if (!double.IsFinite(UpperVolatility) || UpperVolatility <= LowerVolatility)
            return false;

        if (!double.IsFinite(PriceTolerance) || PriceTolerance <= 0.0)
            return false;

        if (MaxIterations <= 0)
            return false;

        if (MaxBracketExpansions <= 0)
            return false;

        normalized = this;
        return true;
    }
}
