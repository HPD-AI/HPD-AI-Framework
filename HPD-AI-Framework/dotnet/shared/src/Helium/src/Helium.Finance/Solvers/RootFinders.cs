namespace Helium.Finance.Solvers;

public static class RootFinders
{
    public static RootResult Bisection(
        Func<double, double> function,
        double lower,
        double upper,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100)
    {
        if (function is null)
            return Failure(RootStatus.NonFiniteInput, lower, upper);

        if (!double.IsFinite(lower) || !double.IsFinite(upper) || !IsValidTolerance(absoluteTolerance) || maxIterations <= 0)
            return Failure(RootStatus.NonFiniteInput, lower, upper);

        if (lower > upper)
            (lower, upper) = (upper, lower);

        var fLower = function(lower);
        var fUpper = function(upper);
        var evaluations = 2;

        if (!double.IsFinite(fLower) || !double.IsFinite(fUpper))
            return Failure(RootStatus.NonFiniteFunctionValue, lower, upper, evaluations);

        if (fLower == 0.0)
            return Success(lower, fLower, 0, evaluations, lower, upper);

        if (fUpper == 0.0)
            return Success(upper, fUpper, 0, evaluations, lower, upper);

        if (Math.Sign(fLower) == Math.Sign(fUpper))
            return new RootResult(false, double.NaN, double.NaN, 0, evaluations, lower, upper, RootStatus.NoBracket);

        var root = double.NaN;
        var fRoot = double.NaN;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            root = lower + 0.5 * (upper - lower);
            fRoot = function(root);
            evaluations++;

            if (!double.IsFinite(fRoot))
                return new RootResult(false, root, fRoot, iteration, evaluations, lower, upper, RootStatus.NonFiniteFunctionValue);

            if (Math.Abs(fRoot) <= absoluteTolerance || Math.Abs(upper - lower) <= absoluteTolerance)
                return Success(root, fRoot, iteration, evaluations, lower, upper);

            if (Math.Sign(fRoot) == Math.Sign(fLower))
            {
                lower = root;
                fLower = fRoot;
            }
            else
            {
                upper = root;
                fUpper = fRoot;
            }
        }

        return new RootResult(false, root, fRoot, maxIterations, evaluations, lower, upper, RootStatus.MaxIterations);
    }

    public static RootResult Brent(
        Func<double, double> function,
        double lower,
        double upper,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100)
    {
        if (function is null)
            return Failure(RootStatus.NonFiniteInput, lower, upper);

        if (!double.IsFinite(lower) || !double.IsFinite(upper) || !IsValidTolerance(absoluteTolerance) || maxIterations <= 0)
            return Failure(RootStatus.NonFiniteInput, lower, upper);

        if (lower > upper)
            (lower, upper) = (upper, lower);

        var a = lower;
        var b = upper;
        var fa = function(a);
        var fb = function(b);
        var evaluations = 2;

        if (!double.IsFinite(fa) || !double.IsFinite(fb))
            return Failure(RootStatus.NonFiniteFunctionValue, lower, upper, evaluations);

        if (fa == 0.0)
            return Success(a, fa, 0, evaluations, lower, upper);

        if (fb == 0.0)
            return Success(b, fb, 0, evaluations, lower, upper);

        if (Math.Sign(fa) == Math.Sign(fb))
            return new RootResult(false, double.NaN, double.NaN, 0, evaluations, lower, upper, RootStatus.NoBracket);

        var c = a;
        var fc = fa;
        var d = b - a;
        var e = d;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            if (Math.Sign(fb) == Math.Sign(fc))
            {
                c = a;
                fc = fa;
                d = b - a;
                e = d;
            }

            if (Math.Abs(fc) < Math.Abs(fb))
            {
                a = b;
                b = c;
                c = a;
                fa = fb;
                fb = fc;
                fc = fa;
            }

            var tolerance = 2.0 * double.Epsilon * Math.Abs(b) + 0.5 * absoluteTolerance;
            var midpoint = 0.5 * (c - b);

            if (Math.Abs(midpoint) <= tolerance || fb == 0.0)
                return Success(b, fb, iteration, evaluations, Math.Min(b, c), Math.Max(b, c));

            if (Math.Abs(e) >= tolerance && Math.Abs(fa) > Math.Abs(fb))
            {
                var s = fb / fa;
                double p;
                double q;

                if (a == c)
                {
                    p = 2.0 * midpoint * s;
                    q = 1.0 - s;
                }
                else
                {
                    q = fa / fc;
                    var r = fb / fc;
                    p = s * (2.0 * midpoint * q * (q - r) - (b - a) * (r - 1.0));
                    q = (q - 1.0) * (r - 1.0) * (s - 1.0);
                }

                if (p > 0.0)
                    q = -q;
                else
                    p = -p;

                var min1 = 3.0 * midpoint * q - Math.Abs(tolerance * q);
                var min2 = Math.Abs(e * q);

                if (2.0 * p < Math.Min(min1, min2))
                {
                    e = d;
                    d = p / q;
                }
                else
                {
                    d = midpoint;
                    e = d;
                }
            }
            else
            {
                d = midpoint;
                e = d;
            }

            a = b;
            fa = fb;
            b += Math.Abs(d) > tolerance ? d : Math.CopySign(tolerance, midpoint);
            fb = function(b);
            evaluations++;

            if (!double.IsFinite(fb))
                return new RootResult(false, b, fb, iteration, evaluations, Math.Min(b, c), Math.Max(b, c), RootStatus.NonFiniteFunctionValue);
        }

        return new RootResult(false, b, fb, maxIterations, evaluations, Math.Min(b, c), Math.Max(b, c), RootStatus.MaxIterations);
    }

    public static RootResult BrentFromGuess(
        Func<double, double> function,
        double guess,
        double step,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100,
        int maxBracketExpansions = 100)
    {
        if (function is null)
            return Failure(RootStatus.NonFiniteInput, guess, guess);

        if (!double.IsFinite(guess)
            || !double.IsFinite(step)
            || step <= 0.0
            || !IsValidTolerance(absoluteTolerance)
            || maxIterations <= 0
            || maxBracketExpansions <= 0)
        {
            return Failure(RootStatus.NonFiniteInput, guess, guess);
        }

        var evaluations = 0;
        var fGuess = Evaluate(function, guess, ref evaluations);

        if (!double.IsFinite(fGuess))
            return Failure(RootStatus.NonFiniteFunctionValue, guess, guess, evaluations);

        if (Math.Abs(fGuess) <= absoluteTolerance)
            return Success(guess, fGuess, 0, evaluations, guess, guess);

        var lower = guess;
        var upper = guess;
        var fLower = fGuess;
        var fUpper = fGuess;

        if (fGuess > 0.0)
        {
            lower = guess - step;
            fLower = Evaluate(function, lower, ref evaluations);
        }
        else
        {
            upper = guess + step;
            fUpper = Evaluate(function, upper, ref evaluations);
        }

        if (!double.IsFinite(fLower) || !double.IsFinite(fUpper))
            return new RootResult(false, double.NaN, double.NaN, 0, evaluations, Math.Min(lower, upper), Math.Max(lower, upper), RootStatus.NonFiniteFunctionValue);

        if (Math.Sign(fLower) != Math.Sign(fUpper))
            return BrentWithExternalEvaluationCount(function, lower, upper, absoluteTolerance, maxIterations, evaluations);

        const double growthFactor = 1.6;
        var flipFlop = -1;

        for (var expansion = 1; expansion <= maxBracketExpansions; expansion++)
        {
            if (Math.Abs(fLower) < Math.Abs(fUpper))
            {
                lower += growthFactor * (lower - upper);
                fLower = Evaluate(function, lower, ref evaluations);
            }
            else if (Math.Abs(fLower) > Math.Abs(fUpper))
            {
                upper += growthFactor * (upper - lower);
                fUpper = Evaluate(function, upper, ref evaluations);
            }
            else if (flipFlop == -1)
            {
                lower += growthFactor * (lower - upper);
                fLower = Evaluate(function, lower, ref evaluations);
                flipFlop = 1;
            }
            else
            {
                upper += growthFactor * (upper - lower);
                fUpper = Evaluate(function, upper, ref evaluations);
                flipFlop = -1;
            }

            if (!double.IsFinite(fLower) || !double.IsFinite(fUpper))
                return new RootResult(false, double.NaN, double.NaN, expansion, evaluations, Math.Min(lower, upper), Math.Max(lower, upper), RootStatus.NonFiniteFunctionValue);

            if (Math.Abs(fLower) <= absoluteTolerance)
                return Success(lower, fLower, expansion, evaluations, Math.Min(lower, upper), Math.Max(lower, upper));

            if (Math.Abs(fUpper) <= absoluteTolerance)
                return Success(upper, fUpper, expansion, evaluations, Math.Min(lower, upper), Math.Max(lower, upper));

            if (Math.Sign(fLower) != Math.Sign(fUpper))
                return BrentWithExternalEvaluationCount(function, lower, upper, absoluteTolerance, maxIterations, evaluations);
        }

        return new RootResult(false, double.NaN, double.NaN, maxBracketExpansions, evaluations, Math.Min(lower, upper), Math.Max(lower, upper), RootStatus.NoBracket);
    }

    public static RootResult Newton(
        Func<double, double> function,
        Func<double, double> derivative,
        double guess,
        double absoluteTolerance = 1e-12,
        int maxIterations = 50)
    {
        if (function is null || derivative is null)
            return Failure(RootStatus.NonFiniteInput, guess, guess);

        if (!double.IsFinite(guess) || !IsValidTolerance(absoluteTolerance) || maxIterations <= 0)
            return Failure(RootStatus.NonFiniteInput, guess, guess);

        var x = guess;
        var evaluations = 0;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var value = function(x);
            var slope = derivative(x);
            evaluations += 2;

            if (!double.IsFinite(value) || !double.IsFinite(slope))
                return new RootResult(false, x, value, iteration, evaluations, x, x, RootStatus.NonFiniteFunctionValue);

            if (Math.Abs(value) <= absoluteTolerance)
                return Success(x, value, iteration, evaluations, x, x);

            if (slope == 0.0)
                return new RootResult(false, x, value, iteration, evaluations, x, x, RootStatus.FlatDerivative);

            var next = x - value / slope;
            if (!double.IsFinite(next))
                return new RootResult(false, x, value, iteration, evaluations, x, x, RootStatus.NonFiniteFunctionValue);

            if (Math.Abs(next - x) <= absoluteTolerance)
            {
                var nextValue = function(next);
                if (!double.IsFinite(nextValue))
                    return new RootResult(false, next, nextValue, iteration, evaluations + 1, next, next, RootStatus.NonFiniteFunctionValue);

                return Success(next, nextValue, iteration, evaluations + 1, next, next);
            }

            x = next;
        }

        var finalValue = function(x);
        if (!double.IsFinite(finalValue))
            return new RootResult(false, x, finalValue, maxIterations, evaluations + 1, x, x, RootStatus.NonFiniteFunctionValue);

        return new RootResult(false, x, finalValue, maxIterations, evaluations + 1, x, x, RootStatus.MaxIterations);
    }

    public static RootResult NewtonSafe(
        Func<double, double> function,
        Func<double, double> derivative,
        double lower,
        double upper,
        double guess,
        double absoluteTolerance = 1e-12,
        int maxIterations = 100)
    {
        if (function is null || derivative is null)
            return Failure(RootStatus.NonFiniteInput, lower, upper);

        if (!double.IsFinite(lower)
            || !double.IsFinite(upper)
            || !double.IsFinite(guess)
            || !IsValidTolerance(absoluteTolerance)
            || maxIterations <= 0)
        {
            return Failure(RootStatus.NonFiniteInput, lower, upper);
        }

        if (lower > upper)
            (lower, upper) = (upper, lower);

        if (guess < lower || guess > upper)
            return Failure(RootStatus.NonFiniteInput, lower, upper);

        var fLower = function(lower);
        var fUpper = function(upper);
        var evaluations = 2;

        if (!double.IsFinite(fLower) || !double.IsFinite(fUpper))
            return Failure(RootStatus.NonFiniteFunctionValue, lower, upper, evaluations);

        if (fLower == 0.0)
            return Success(lower, fLower, 0, evaluations, lower, upper);

        if (fUpper == 0.0)
            return Success(upper, fUpper, 0, evaluations, lower, upper);

        if (Math.Sign(fLower) == Math.Sign(fUpper))
            return new RootResult(false, double.NaN, double.NaN, 0, evaluations, lower, upper, RootStatus.NoBracket);

        var xLow = fLower < 0.0 ? lower : upper;
        var xHigh = fLower < 0.0 ? upper : lower;
        var x = guess;
        var step = upper - lower;
        var previousStep = step;
        var value = function(x);
        var slope = derivative(x);
        evaluations += 2;

        if (!double.IsFinite(value) || !double.IsFinite(slope))
            return new RootResult(false, x, value, 0, evaluations, lower, upper, RootStatus.NonFiniteFunctionValue);

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var newtonOutOfBracket = ((x - xHigh) * slope - value) * ((x - xLow) * slope - value) > 0.0;
            var newtonTooSlow = Math.Abs(2.0 * value) > Math.Abs(previousStep * slope);

            previousStep = step;
            if (slope == 0.0 || newtonOutOfBracket || newtonTooSlow)
            {
                step = 0.5 * (xHigh - xLow);
                x = xLow + step;
            }
            else
            {
                step = value / slope;
                x -= step;
            }

            if (!double.IsFinite(x))
                return new RootResult(false, double.NaN, double.NaN, iteration, evaluations, Math.Min(xLow, xHigh), Math.Max(xLow, xHigh), RootStatus.NonFiniteFunctionValue);

            value = function(x);
            slope = derivative(x);
            evaluations += 2;

            if (!double.IsFinite(value) || !double.IsFinite(slope))
                return new RootResult(false, x, value, iteration, evaluations, Math.Min(xLow, xHigh), Math.Max(xLow, xHigh), RootStatus.NonFiniteFunctionValue);

            if (value == 0.0 || Math.Abs(step) <= absoluteTolerance)
                return Success(x, value, iteration, evaluations, Math.Min(xLow, xHigh), Math.Max(xLow, xHigh));

            if (value < 0.0)
                xLow = x;
            else
                xHigh = x;
        }

        return new RootResult(false, x, value, maxIterations, evaluations, Math.Min(xLow, xHigh), Math.Max(xLow, xHigh), RootStatus.MaxIterations);
    }

    private static bool IsValidTolerance(double absoluteTolerance) =>
        double.IsFinite(absoluteTolerance) && absoluteTolerance > 0.0;

    private static double Evaluate(Func<double, double> function, double x, ref int evaluations)
    {
        var value = function(x);
        evaluations++;
        return value;
    }

    private static RootResult BrentWithExternalEvaluationCount(
        Func<double, double> function,
        double lower,
        double upper,
        double absoluteTolerance,
        int maxIterations,
        int previousEvaluations)
    {
        var result = Brent(function, lower, upper, absoluteTolerance, maxIterations);

        return new RootResult(
            result.Converged,
            result.Root,
            result.FunctionValue,
            result.Iterations,
            previousEvaluations + result.FunctionEvaluations,
            result.Lower,
            result.Upper,
            result.Status);
    }

    private static RootResult Success(double root, double value, int iterations, int evaluations, double lower, double upper) =>
        new(true, root, value, iterations, evaluations, lower, upper, RootStatus.Converged);

    private static RootResult Failure(RootStatus status, double lower, double upper, int evaluations = 0) =>
        new(false, double.NaN, double.NaN, 0, evaluations, lower, upper, status);
}
