using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Forward-mode automatic differentiation via dual numbers.
/// Seeding x as x + ε and reading the tangent of the result gives f'(x) exactly.
/// No tape, no mutable state.
/// Use only exact field types here; validated numerical differentiation lives in Helium.Validated.
/// </summary>
public static class ForwardDiff
{
    /// <summary>
    /// Computes f'(x) using dual numbers.
    /// The function must be written against Dual&lt;T&gt; — field operations compose automatically.
    /// </summary>
    public static T Diff<T>(Func<Dual<T>, Dual<T>> f, T x)
        where T : IField<T>
    {
        return f(Dual<T>.Seed(x)).Tangent;
    }

    /// <summary>
    /// Computes the value and derivative of f at x simultaneously.
    /// </summary>
    public static (T Value, T Deriv) ValueAndDiff<T>(
        Func<Dual<T>, Dual<T>> f, T x)
        where T : IField<T>
    {
        var result = f(Dual<T>.Seed(x));
        return (result.Primal, result.Tangent);
    }
}
