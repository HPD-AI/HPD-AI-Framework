using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Forward-mode automatic differentiation via dual numbers.
/// Uses FormalPowerSeries&lt;R&gt; truncated at degree 1: seeding with [x, 1, 0, ...] and
/// reading coefficient 1 of the result gives f'(x) exactly. No tape, no mutable state.
/// Over Rational: exact rational derivatives. Over Double: standard floating-point AD.
/// </summary>
public static class ForwardDiff
{
    /// <summary>
    /// Computes f'(x) using dual numbers.
    /// The function must be written against FormalPowerSeries&lt;T&gt; — field operations
    /// compose through the series automatically.
    /// </summary>
    public static T Diff<T>(Func<FormalPowerSeries<T>, FormalPowerSeries<T>> f, T x)
        where T : IField<T>
    {
        var dual = FormalPowerSeries<T>.FromGenerator(n =>
            n == 0 ? x :
            n == 1 ? T.MultiplicativeIdentity :
                     T.AdditiveIdentity);
        return f(dual).Coefficient(1);
    }

    /// <summary>
    /// Computes the value and derivative of f at x simultaneously.
    /// </summary>
    public static (T Value, T Deriv) ValueAndDiff<T>(
        Func<FormalPowerSeries<T>, FormalPowerSeries<T>> f, T x)
        where T : IField<T>
    {
        var dual = FormalPowerSeries<T>.FromGenerator(n =>
            n == 0 ? x :
            n == 1 ? T.MultiplicativeIdentity :
                     T.AdditiveIdentity);
        var result = f(dual);
        return (result.Coefficient(0), result.Coefficient(1));
    }
}
