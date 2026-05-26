namespace Helium.Finance.Curves;

internal static class CurvePolicyValidation
{
    public static void ValidateInterpolation(InterpolationPolicy policy)
    {
        if (policy is not InterpolationPolicy.Linear)
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported interpolation policy.");
    }

    public static void ValidateExtrapolation(ExtrapolationPolicy policy)
    {
        if (policy is not (ExtrapolationPolicy.Disabled or ExtrapolationPolicy.Flat or ExtrapolationPolicy.Linear))
            throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported extrapolation policy.");
    }
}
