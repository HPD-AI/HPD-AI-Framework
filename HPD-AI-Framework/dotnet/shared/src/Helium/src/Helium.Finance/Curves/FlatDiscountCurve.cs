using Helium.Finance.Conventions;

namespace Helium.Finance.Curves;

public readonly record struct FlatDiscountCurve(InterestRate Rate)
{
    public double DiscountFactor(double time) => Rate.DiscountFactor(time);

    public double ForwardDiscountFactor(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0.0 || end < start)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward-discount interval must satisfy 0 <= start <= end.");

        var forwardDiscountFactor = DiscountFactor(end) / DiscountFactor(start);
        if (!double.IsFinite(forwardDiscountFactor) || forwardDiscountFactor <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(end), "Forward discount factor must be finite and positive.");

        return forwardDiscountFactor;
    }
}
