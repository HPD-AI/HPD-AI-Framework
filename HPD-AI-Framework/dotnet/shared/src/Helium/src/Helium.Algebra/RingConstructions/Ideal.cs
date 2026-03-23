using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Ideal of a commutative ring. Currently supports principal ideals (single generator)
/// and finitely generated ideals.
/// </summary>
public class Ideal<R>
    where R : ICommRing<R>
{
    private readonly R[] _generators;

    private Ideal(R[] generators) => _generators = generators;

    public static Ideal<R> Principal(R generator) => new([generator]);

    public static Ideal<R> Generated(params R[] generators) => new((R[])generators.Clone());

    public IReadOnlyList<R> Generators => _generators;

    public bool IsPrincipal => _generators.Length == 1;

    public R Generator => _generators[0];

    /// <summary>
    /// The zero ideal (0): contains only zero.
    /// </summary>
    public static Ideal<R> ZeroIdeal => Principal(R.AdditiveIdentity);

    /// <summary>
    /// The unit ideal (1): contains everything.
    /// </summary>
    public static Ideal<R> UnitIdeal => Principal(R.MultiplicativeIdentity);
}

/// <summary>
/// Shared context for a quotient ring R / I. Holds the reduction function and
/// provides reference-equality-based context resolution for arithmetic.
///
/// The sentinel instance is used by the static AdditiveIdentity and MultiplicativeIdentity
/// members to satisfy the ICommRing interface without knowing the actual quotient.
/// </summary>
public sealed class QuotientContext<R> where R : ICommRing<R>
{
    /// <summary>The reduction function x ↦ canonical representative of [x].</summary>
    public Func<R, R> Reduce { get; }

    private readonly bool _isSentinel;

    private QuotientContext(Func<R, R> reduce, bool isSentinel)
    {
        Reduce = reduce;
        _isSentinel = isSentinel;
    }

    /// <summary>Create a real context with the given reduction function.</summary>
    public static QuotientContext<R> Create(Func<R, R> reduce) => new(reduce, isSentinel: false);

    /// <summary>The sentinel context used by identity elements.</summary>
    public static QuotientContext<R> Sentinel { get; } = new(x => x, isSentinel: true);

    public bool IsSentinel => _isSentinel;

    /// <summary>
    /// Resolves the context to use for an arithmetic operation.
    ///
    /// Rules:
    /// - Both same real context (reference equality): use it.
    /// - One sentinel, one real: use the real silently.
    ///   Required for generic accumulators seeded with AdditiveIdentity.
    /// - Both real but different: throw InvalidOperationException.
    ///   This catches silent mixing of different quotients.
    /// </summary>
    public static QuotientContext<R> Resolve(QuotientContext<R> left, QuotientContext<R> right)
    {
        if (ReferenceEquals(left, right)) return left;
        if (left._isSentinel) return right;
        if (right._isSentinel) return left;
        throw new InvalidOperationException(
            "Cannot mix elements from different quotient rings. " +
            "Ensure both operands belong to the same QuotientContext.");
    }
}

/// <summary>
/// Quotient ring R / I. Elements are equivalence classes represented by a canonical
/// representative, reduced modulo the ideal.
///
/// For principal ideals over Euclidean domains, reduction is via DivMod.
/// Elements carry a reference to a shared QuotientContext&lt;R&gt; rather than their
/// own closure, ensuring that operations on elements from different quotients are
/// detected and rejected rather than silently producing wrong results.
/// </summary>
public readonly struct QuotientRing<R> :
    ICommRing<QuotientRing<R>>,
    IEquatable<QuotientRing<R>>
    where R : ICommRing<R>
{
    /// <summary>The canonical representative of the equivalence class.</summary>
    public R Representative { get; }

    private readonly QuotientContext<R> _context;

    private QuotientRing(R representative, QuotientContext<R> context)
    {
        _context = context;
        Representative = context.Reduce(representative);
    }

    /// <summary>
    /// Create a quotient ring element with the given shared context.
    /// </summary>
    public static QuotientRing<R> Create(R value, QuotientContext<R> context) =>
        new(value, context);

    // --- Identity elements (use sentinel context) ---

    public static QuotientRing<R> AdditiveIdentity =>
        new(R.AdditiveIdentity, QuotientContext<R>.Sentinel);

    public static QuotientRing<R> MultiplicativeIdentity =>
        new(R.MultiplicativeIdentity, QuotientContext<R>.Sentinel);

    static QuotientRing<R> IAdditiveIdentity<QuotientRing<R>, QuotientRing<R>>.AdditiveIdentity => AdditiveIdentity;
    static QuotientRing<R> IMultiplicativeIdentity<QuotientRing<R>, QuotientRing<R>>.MultiplicativeIdentity => MultiplicativeIdentity;

    // --- Arithmetic (operates on reps, then reduces via resolved context) ---

    public static QuotientRing<R> operator +(QuotientRing<R> left, QuotientRing<R> right)
    {
        var ctx = QuotientContext<R>.Resolve(left._context, right._context);
        return new(left.Representative + right.Representative, ctx);
    }

    public static QuotientRing<R> operator -(QuotientRing<R> left, QuotientRing<R> right)
    {
        var ctx = QuotientContext<R>.Resolve(left._context, right._context);
        return new(left.Representative - right.Representative, ctx);
    }

    public static QuotientRing<R> operator *(QuotientRing<R> left, QuotientRing<R> right)
    {
        var ctx = QuotientContext<R>.Resolve(left._context, right._context);
        return new(left.Representative * right.Representative, ctx);
    }

    public static QuotientRing<R> operator -(QuotientRing<R> value) =>
        new(-value.Representative, value._context);

    // --- Equality (on representatives, which are canonical after reduction) ---

    public static bool operator ==(QuotientRing<R> left, QuotientRing<R> right) =>
        left.Representative.Equals(right.Representative);

    public static bool operator !=(QuotientRing<R> left, QuotientRing<R> right) => !(left == right);

    public bool Equals(QuotientRing<R> other) => Representative.Equals(other.Representative);
    public override bool Equals(object? obj) => obj is QuotientRing<R> other && Equals(other);
    public override int GetHashCode() => Representative.GetHashCode();
    public override string ToString() => Representative.ToString() ?? "0";
}

/// <summary>
/// Factory for ZMod(n): integers modulo n as a quotient ring.
/// Contexts are cached by modulus so that all elements of the same ZMod ring
/// share one context object, enabling reference-equality-based context resolution.
/// </summary>
public static class ZMod
{
    // Cache: absolute value of modulus → shared context.
    // Using Dictionary with lock for thread safety.
    private static readonly Dictionary<Integer, QuotientContext<Integer>> _cache = [];
    private static readonly Lock _lock = new();

    public static QuotientRing<Integer> Create(Integer value, Integer modulus)
    {
        var ctx = Context(modulus);
        return QuotientRing<Integer>.Create(value, ctx);
    }

    public static QuotientContext<Integer> Context(Integer modulus)
    {
        if (modulus.IsZero)
            return QuotientContext<Integer>.Create(x => x);

        var abs = modulus.Abs();
        lock (_lock)
        {
            if (_cache.TryGetValue(abs, out var cached))
                return cached;

            var ctx = QuotientContext<Integer>.Create(x =>
            {
                var (_, r) = Integer.DivMod(x, abs);
                if (r < Integer.Zero)
                    r += abs;
                return r;
            });
            _cache[abs] = ctx;
            return ctx;
        }
    }

    public static Func<Integer, Integer> Reducer(Integer modulus)
    {
        if (modulus.IsZero)
            return x => x;

        var abs = modulus.Abs();
        return x =>
        {
            var (_, r) = Integer.DivMod(x, abs);
            if (r < Integer.Zero)
                r = r + abs;
            return r;
        };
    }
}
