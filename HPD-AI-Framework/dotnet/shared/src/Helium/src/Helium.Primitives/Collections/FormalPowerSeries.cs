using System.Collections.Concurrent;
using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Formal power series: lazy infinite sequence of coefficients where the n-th coefficient
/// is the coefficient of X^n. Backed by a generator function with memoization.
/// Truncate/FromPolynomial deferred to Algebra layer (requires Polynomial type).
/// </summary>
public readonly struct FormalPowerSeries<R>
    : IField<FormalPowerSeries<R>>
    where R : IField<R>
{
    private readonly Func<int, R>? _generator;
    private readonly ConcurrentDictionary<int, R>? _cache;

    private FormalPowerSeries(Func<int, R> generator)
    {
        _generator = generator;
        _cache = new ConcurrentDictionary<int, R>();
    }

    // --- Construction ---

    public static FormalPowerSeries<R> FromGenerator(Func<int, R> generator) => new(generator);

    public static FormalPowerSeries<R> Zero => new(_ => R.AdditiveIdentity);
    public static FormalPowerSeries<R> One => new(n => n == 0 ? R.MultiplicativeIdentity : R.AdditiveIdentity);

    /// <summary>
    /// The power series X = 0 + 1*X + 0*X^2 + ...
    /// </summary>
    public static FormalPowerSeries<R> X => new(n => n == 1 ? R.MultiplicativeIdentity : R.AdditiveIdentity);

    /// <summary>
    /// Constant power series: c + 0*X + 0*X^2 + ...
    /// </summary>
    public static FormalPowerSeries<R> Constant(R c) =>
        new(n => n == 0 ? c : R.AdditiveIdentity);

    // --- Coefficient access (lazy, memoized) ---

    public R Coefficient(int n)
    {
        if (n < 0)
            return R.AdditiveIdentity;
        if (_generator is null)
            return R.AdditiveIdentity;
        return _cache!.GetOrAdd(n, _generator);
    }

    // --- Arithmetic ---

    public static FormalPowerSeries<R> operator +(FormalPowerSeries<R> f, FormalPowerSeries<R> g) =>
        new(n => f.Coefficient(n) + g.Coefficient(n));

    public static FormalPowerSeries<R> operator -(FormalPowerSeries<R> f, FormalPowerSeries<R> g) =>
        new(n => f.Coefficient(n) - g.Coefficient(n));

    public static FormalPowerSeries<R> operator -(FormalPowerSeries<R> f) =>
        new(n => -f.Coefficient(n));

    /// <summary>
    /// Cauchy product: (f * g)[n] = sum_{i=0}^{n} f[i] * g[n-i].
    /// </summary>
    public static FormalPowerSeries<R> operator *(FormalPowerSeries<R> f, FormalPowerSeries<R> g) =>
        new(n =>
        {
            var sum = R.AdditiveIdentity;
            for (int i = 0; i <= n; i++)
                sum = sum + f.Coefficient(i) * g.Coefficient(n - i);
            return sum;
        });

    /// <summary>
    /// Formal composition f(g(x)): (f ∘ g)[n] is computed by substituting g into f.
    /// Requires g[0] == 0 for convergence of the formal composition.
    /// </summary>
    public static FormalPowerSeries<R> Compose(FormalPowerSeries<R> f, FormalPowerSeries<R> g)
    {
        // g^k is computed iteratively; result[n] = sum_k f[k] * (g^k)[n]
        // We truncate at n+1 terms since (g^k)[n] = 0 for k > n when g[0] = 0.
        return new(n =>
        {
            var result = R.AdditiveIdentity;
            // g^0 = 1
            var gPower = One;
            for (int k = 0; k <= n; k++)
            {
                result = result + f.Coefficient(k) * gPower.Coefficient(n);
                if (k < n)
                    gPower = gPower * g;
            }
            return result;
        });
    }

    // --- IField<FPS<R>> members ---

    public static FormalPowerSeries<R> AdditiveIdentity => Zero;
    public static FormalPowerSeries<R> MultiplicativeIdentity => One;

    /// <summary>
    /// Formal multiplicative inverse. Invert(0) = 0 (total function).
    /// Uses: inv[0] = R.Invert(f[0]), inv[n] = -inv[0] * sum_{i=1}^{n} f[i] * inv[n-i].
    /// </summary>
    public static FormalPowerSeries<R> Invert(FormalPowerSeries<R> a)
    {
        R a0 = a.Coefficient(0);
        if (a0.Equals(R.AdditiveIdentity)) return Zero;
        var invCache = new ConcurrentDictionary<int, R>();
        var f = a;
        R ComputeInv(int n)
        {
            if (invCache.TryGetValue(n, out var cached)) return cached;
            var f0Inv = R.Invert(f.Coefficient(0));
            R result;
            if (n == 0)
            {
                result = f0Inv;
            }
            else
            {
                var sum = R.AdditiveIdentity;
                for (int i = 1; i <= n; i++)
                    sum = sum + f.Coefficient(i) * ComputeInv(n - i);
                result = -(f0Inv * sum);
            }
            invCache.TryAdd(n, result);
            return result;
        }
        return FromGenerator(ComputeInv);
    }

    public static FormalPowerSeries<R> operator /(FormalPowerSeries<R> f, FormalPowerSeries<R> g)
        => f * Invert(g);

    public bool IsZero => Coefficient(0).Equals(R.AdditiveIdentity); // approximate: checks only constant term

    public bool Equals(FormalPowerSeries<R> other)
    {
        // Structural equality is undecidable for lazy series; compare first few coefficients.
        // Used only for pivot detection (IsZero check via == AdditiveIdentity).
        for (int i = 0; i < 8; i++)
            if (!Coefficient(i).Equals(other.Coefficient(i))) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is FormalPowerSeries<R> other && Equals(other);
    public override int GetHashCode() => Coefficient(0).GetHashCode();
    public static bool operator ==(FormalPowerSeries<R> a, FormalPowerSeries<R> b) => a.Equals(b);
    public static bool operator !=(FormalPowerSeries<R> a, FormalPowerSeries<R> b) => !a.Equals(b);

    /// <summary>
    /// Get coefficients 0 through n-1 as an array.
    /// </summary>
    public R[] GetCoefficients(int count)
    {
        var result = new R[count];
        for (int i = 0; i < count; i++)
            result[i] = Coefficient(i);
        return result;
    }

    /// <summary>
    /// Fill a span with coefficients 0 through buffer.Length-1. Allocation-free.
    /// </summary>
    public void GetCoefficients(Span<R> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Coefficient(i);
    }
}

/// <summary>
/// Field operations on FormalPowerSeries when R is a field.
/// C# 14 extension block: adds Inverse() conditionally when R : IField.
/// </summary>
public static class FormalPowerSeriesFieldExtensions
{
    extension<R>(FormalPowerSeries<R> self) where R : IField<R>
    {
        /// <summary>
        /// Formal inverse: f^(-1) where f[0] must be invertible.
        /// Uses the recurrence: inv[0] = f[0]^(-1), inv[n] = -f[0]^(-1) * sum_{i=1}^{n} f[i] * inv[n-i].
        /// </summary>
        public FormalPowerSeries<R> Inverse()
        {
            var f = self;
            var invCache = new ConcurrentDictionary<int, R>();

            R ComputeInv(int n)
            {
                if (invCache.TryGetValue(n, out var cached))
                    return cached;

                var f0Inv = R.Invert(f.Coefficient(0));
                R result;
                if (n == 0)
                {
                    result = f0Inv;
                }
                else
                {
                    var sum = R.AdditiveIdentity;
                    for (int i = 1; i <= n; i++)
                        sum = sum + f.Coefficient(i) * ComputeInv(n - i);
                    result = -(f0Inv * sum);
                }

                invCache.TryAdd(n, result);
                return result;
            }

            return FormalPowerSeries<R>.FromGenerator(ComputeInv);
        }
    }
}
