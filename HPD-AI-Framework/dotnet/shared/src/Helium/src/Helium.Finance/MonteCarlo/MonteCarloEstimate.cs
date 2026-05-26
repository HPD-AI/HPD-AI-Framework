namespace Helium.Finance.MonteCarlo;

public readonly record struct MonteCarloEstimate
{
    public MonteCarloEstimate(double value, double standardError, double confidenceRadius, int samples)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Monte Carlo estimate value must be finite.");

        if (!double.IsFinite(standardError) || standardError < 0.0)
            throw new ArgumentOutOfRangeException(nameof(standardError), "Monte Carlo standard error must be finite and nonnegative.");

        if (!double.IsFinite(confidenceRadius) || confidenceRadius < 0.0)
            throw new ArgumentOutOfRangeException(nameof(confidenceRadius), "Monte Carlo confidence radius must be finite and nonnegative.");

        if (standardError == 0.0 && confidenceRadius != 0.0)
            throw new ArgumentOutOfRangeException(nameof(confidenceRadius), "Monte Carlo confidence radius must be zero when standard error is zero.");

        if (samples <= 0)
            throw new ArgumentOutOfRangeException(nameof(samples), "Monte Carlo sample count must be positive.");

        if (!double.IsFinite(value - confidenceRadius) || !double.IsFinite(value + confidenceRadius))
            throw new ArgumentOutOfRangeException(nameof(confidenceRadius), "Monte Carlo confidence bounds must be finite.");

        Value = value;
        StandardError = standardError;
        ConfidenceRadius = confidenceRadius;
        Samples = samples;
    }

    public double Value { get; }

    public double StandardError { get; }

    public double ConfidenceRadius { get; }

    public int Samples { get; }

    public double LowerBound => Value - ConfidenceRadius;

    public double UpperBound => Value + ConfidenceRadius;

    public void Deconstruct(
        out double value,
        out double standardError,
        out double confidenceRadius,
        out int samples)
    {
        value = Value;
        standardError = StandardError;
        confidenceRadius = ConfidenceRadius;
        samples = Samples;
    }
}
