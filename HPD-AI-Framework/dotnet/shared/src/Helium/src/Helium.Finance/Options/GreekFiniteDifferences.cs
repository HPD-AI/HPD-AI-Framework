namespace Helium.Finance.Options;

public static class GreekFiniteDifferences
{
    public static FiniteDifferenceGreekEstimate EstimateBlack76(
        Black76Input input,
        FiniteDifferenceBumps? bumps = null)
    {
        var settings = (bumps ?? FiniteDifferenceBumps.Default).Normalize();
        var price = Black76.Price(input);
        var delta = NonnegativeFirstDerivative(
            input.Forward,
            forward => Black76.Price(input with { Forward = forward }),
            settings.Underlying);
        var gamma = NonnegativeSecondDerivative(
            input.Forward,
            forward => Black76.Price(input with { Forward = forward }),
            settings.Underlying);
        var vega = VolatilityDerivative(
            input.Volatility,
            volatility => Black76.Price(input with { Volatility = volatility }),
            settings.Volatility);

        return new FiniteDifferenceGreekEstimate(price, delta, gamma, vega);
    }

    public static FiniteDifferenceGreekEstimate EstimateBlackScholes(
        BlackScholesInput input,
        FiniteDifferenceBumps? bumps = null)
    {
        var settings = (bumps ?? FiniteDifferenceBumps.Default).Normalize();
        var price = BlackScholes.Price(input);
        var delta = NonnegativeFirstDerivative(
            input.Spot,
            spot => BlackScholes.Price(input with { Spot = spot }),
            settings.Underlying);
        var gamma = NonnegativeSecondDerivative(
            input.Spot,
            spot => BlackScholes.Price(input with { Spot = spot }),
            settings.Underlying);
        var vega = VolatilityDerivative(
            input.Volatility,
            volatility => BlackScholes.Price(input with { Volatility = volatility }),
            settings.Volatility);

        return new FiniteDifferenceGreekEstimate(price, delta, gamma, vega);
    }

    public static FiniteDifferenceGreekEstimate EstimateBachelier(
        BachelierInput input,
        FiniteDifferenceBumps? bumps = null)
    {
        var settings = (bumps ?? FiniteDifferenceBumps.Default).Normalize();
        var price = Bachelier.Price(input);
        var delta = Central(
            input.Forward,
            forward => Bachelier.Price(input with { Forward = forward }),
            settings.Underlying);
        var gamma = SecondCentral(
            input.Forward,
            forward => Bachelier.Price(input with { Forward = forward }),
            settings.Underlying);
        var vega = VolatilityDerivative(
            input.NormalVolatility,
            normalVolatility => Bachelier.Price(input with { NormalVolatility = normalVolatility }),
            settings.Volatility);

        return new FiniteDifferenceGreekEstimate(price, delta, gamma, vega);
    }

    private static double Central(double x, Func<double, double> f, double bump)
    {
        var result = (f(x + bump) - f(x - bump)) / (2.0 * bump);
        return EnsureFinite(result, "Finite-difference first derivative must be finite.");
    }

    private static double SecondCentral(double x, Func<double, double> f, double bump)
    {
        var result = (f(x + bump) - 2.0 * f(x) + f(x - bump)) / (bump * bump);
        return EnsureFinite(result, "Finite-difference second derivative must be finite.");
    }

    private static double NonnegativeFirstDerivative(double x, Func<double, double> f, double bump)
    {
        if (x > bump)
            return Central(x, f, bump);

        var result = (f(x + bump) - f(x)) / bump;
        return EnsureFinite(result, "Finite-difference first derivative must be finite.");
    }

    private static double NonnegativeSecondDerivative(double x, Func<double, double> f, double bump)
    {
        if (x > bump)
            return SecondCentral(x, f, bump);

        var result = (f(x + 2.0 * bump) - 2.0 * f(x + bump) + f(x)) / (bump * bump);
        return EnsureFinite(result, "Finite-difference second derivative must be finite.");
    }

    private static double VolatilityDerivative(double volatility, Func<double, double> f, double bump)
    {
        if (volatility > bump)
            return Central(volatility, f, bump);

        var result = (f(volatility + bump) - f(volatility)) / bump;
        return EnsureFinite(result, "Finite-difference volatility derivative must be finite.");
    }

    private static double EnsureFinite(double value, string message)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), message);

        return value;
    }
}
