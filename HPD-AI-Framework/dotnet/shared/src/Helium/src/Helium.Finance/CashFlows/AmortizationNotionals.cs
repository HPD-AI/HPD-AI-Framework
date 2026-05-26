namespace Helium.Finance.CashFlows;

public static class AmortizationNotionals
{
    public static IReadOnlyList<double> LevelPrincipal(double initialNotional, int periodCount)
    {
        Validate(initialNotional, periodCount);

        var notionals = new double[periodCount];
        var principalStep = initialNotional / periodCount;
        for (var i = 0; i < notionals.Length; i++)
            notionals[i] = ValidateProjectedNotional(principalStep * (periodCount - i), nameof(initialNotional));

        return notionals;
    }

    public static IReadOnlyList<double> French(double initialNotional, double annualCouponRate, int paymentsPerYear, int periodCount)
    {
        Validate(initialNotional, periodCount);

        if (!double.IsFinite(annualCouponRate) || annualCouponRate < 0.0)
            throw new ArgumentOutOfRangeException(nameof(annualCouponRate), "Annual coupon rate must be finite and nonnegative.");

        if (paymentsPerYear <= 0)
            throw new ArgumentOutOfRangeException(nameof(paymentsPerYear), "Payments per year must be positive.");

        var periodicRate = annualCouponRate / paymentsPerYear;
        if (!double.IsFinite(periodicRate))
            throw new ArgumentOutOfRangeException(nameof(annualCouponRate), "Periodic coupon rate must be finite.");

        var notionals = new double[periodCount];
        notionals[0] = initialNotional;

        if (periodCount == 1)
            return notionals;

        if (periodicRate < 1e-12)
            return LevelPrincipal(initialNotional, periodCount);

        var denominator = 1.0 - Math.Pow(1.0 + periodicRate, -periodCount);
        if (!double.IsFinite(denominator) || denominator <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(annualCouponRate), "French amortization denominator must be finite and positive.");

        var levelPayment = initialNotional * periodicRate / denominator;
        if (!double.IsFinite(levelPayment) || levelPayment <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialNotional), "French amortization payment must be finite and positive.");

        var outstanding = initialNotional;
        for (var i = 1; i < periodCount; i++)
        {
            var interest = outstanding * periodicRate;
            if (!double.IsFinite(interest))
                throw new ArgumentOutOfRangeException(nameof(initialNotional), "French amortization interest projection must be finite.");

            var principal = levelPayment - interest;
            if (!double.IsFinite(principal))
                throw new ArgumentOutOfRangeException(nameof(initialNotional), "French amortization principal projection must be finite.");

            outstanding = Math.Max(outstanding - principal, 0.0);
            notionals[i] = ValidateProjectedNotional(outstanding, nameof(initialNotional));
        }

        return notionals;
    }

    private static void Validate(double initialNotional, int periodCount)
    {
        if (!double.IsFinite(initialNotional) || initialNotional <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(initialNotional), "Initial notional must be finite and positive.");

        if (periodCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodCount), "Period count must be positive.");
    }

    private static double ValidateProjectedNotional(double notional, string parameterName)
    {
        if (!double.IsFinite(notional) || notional < 0.0)
            throw new ArgumentOutOfRangeException(parameterName, "Projected notional must be finite and nonnegative.");

        return notional;
    }
}
