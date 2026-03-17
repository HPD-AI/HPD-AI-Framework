using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Cache of QuotientContext instances keyed by defining polynomial.
/// Reference equality on context is required for QuotientContext.Resolve to correctly
/// detect element mixing. Two NumberField values with the same defining polynomial
/// must share exactly one context instance — this cache ensures that.
/// Same pattern as ZMod.Context(modulus).
/// </summary>
internal static class NumberFieldContext
{
    private static readonly Dictionary<Polynomial<Rational>, QuotientContext<Polynomial<Rational>>> _cache = [];
    private static readonly Lock _lock = new();

    public static QuotientContext<Polynomial<Rational>> For(Polynomial<Rational> f)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(f, out var cached)) return cached;
            var ctx = QuotientContext<Polynomial<Rational>>.Create(p => p.DivMod(f).Remainder);
            _cache[f] = ctx;
            return ctx;
        }
    }
}

/// <summary>
/// A number field K = Q(α) where α is a root of an irreducible polynomial f ∈ Q[x].
/// Immutable value type. Owns the shared QuotientContext for its elements.
/// All derived data (ring of integers, discriminant, etc.) lives in Helium.Algorithms.
/// </summary>
public readonly struct NumberField : IEquatable<NumberField>
{
    /// <summary>The defining irreducible polynomial f ∈ Q[x].</summary>
    public Polynomial<Rational> DefiningPolynomial { get; }

    /// <summary>Degree of the extension [K : Q] = deg(f).</summary>
    public int Degree { get; }

    /// <summary>Name for the generator α in display output.</summary>
    public string GeneratorName { get; }

    /// <summary>
    /// Shared reduction context: p ↦ p mod f.
    /// Retrieved from NumberFieldContext cache — same reference for all NumberField
    /// instances with the same defining polynomial.
    /// </summary>
    public QuotientContext<Polynomial<Rational>> Context { get; }

    public NumberField(Polynomial<Rational> f, string generatorName = "α")
    {
        DefiningPolynomial = f;
        Degree = f.Degree;
        GeneratorName = generatorName;
        Context = NumberFieldContext.For(f);
    }

    public bool Equals(NumberField other) => DefiningPolynomial.Equals(other.DefiningPolynomial);
    public override bool Equals(object? obj) => obj is NumberField other && Equals(other);
    public override int GetHashCode() => DefiningPolynomial.GetHashCode();
    public static bool operator ==(NumberField left, NumberField right) => left.Equals(right);
    public static bool operator !=(NumberField left, NumberField right) => !left.Equals(right);

    public override string ToString() => $"Q({GeneratorName}) = Q[x]/({DefiningPolynomial})";
}

/// <summary>
/// An element of a number field K = Q(α). Represented as p(α) where p ∈ Q[x] has deg &lt; [K:Q].
/// Implements IField — arithmetic delegates to QuotientRing&lt;Polynomial&lt;Rational&gt;&gt;;
/// inversion uses ExtendedGcd (Bezout's identity mod the irreducible f).
/// </summary>
public readonly struct NumberFieldElement : IField<NumberFieldElement>, IEquatable<NumberFieldElement>
{
    public NumberField Field { get; }
    private readonly QuotientRing<Polynomial<Rational>> _inner;

    /// <summary>The canonical polynomial representative, deg &lt; Field.Degree.</summary>
    public Polynomial<Rational> Value => _inner.Representative;

    private NumberFieldElement(NumberField field, QuotientRing<Polynomial<Rational>> inner)
    {
        Field = field;
        _inner = inner;
    }

    /// <summary>Create an element from a polynomial p; reduces p mod f automatically.</summary>
    public static NumberFieldElement Create(Polynomial<Rational> p, NumberField field) =>
        new(field, QuotientRing<Polynomial<Rational>>.Create(p, field.Context));

    /// <summary>The generator α (image of x in Q[x]/f).</summary>
    public static NumberFieldElement Generator(NumberField field) =>
        Create(Polynomial<Rational>.X, field);

    // --- IField identity elements (use sentinel context; Field will be default) ---

    public static NumberFieldElement AdditiveIdentity =>
        new(default, QuotientRing<Polynomial<Rational>>.AdditiveIdentity);

    public static NumberFieldElement MultiplicativeIdentity =>
        new(default, QuotientRing<Polynomial<Rational>>.MultiplicativeIdentity);

    static NumberFieldElement IAdditiveIdentity<NumberFieldElement, NumberFieldElement>.AdditiveIdentity =>
        AdditiveIdentity;

    static NumberFieldElement IMultiplicativeIdentity<NumberFieldElement, NumberFieldElement>.MultiplicativeIdentity =>
        MultiplicativeIdentity;

    // --- Arithmetic: delegate to QuotientRing ---

    public static NumberFieldElement operator +(NumberFieldElement left, NumberFieldElement right) =>
        new(ResolveField(left, right), left._inner + right._inner);

    public static NumberFieldElement operator -(NumberFieldElement left, NumberFieldElement right) =>
        new(ResolveField(left, right), left._inner - right._inner);

    public static NumberFieldElement operator *(NumberFieldElement left, NumberFieldElement right) =>
        new(ResolveField(left, right), left._inner * right._inner);

    public static NumberFieldElement operator -(NumberFieldElement value) =>
        new(value.Field, -value._inner);

    public static NumberFieldElement operator /(NumberFieldElement left, NumberFieldElement right) =>
        left * Invert(right);

    /// <summary>
    /// Inversion via Bezout: since f is irreducible and Value ≠ 0, gcd(Value, f) = 1.
    /// ExtendedGcd gives u with u * Value ≡ 1 (mod f).
    /// </summary>
    public static NumberFieldElement Invert(NumberFieldElement value)
    {
        // Convention: Invert(0) = 0 (total function, matching IField contract and Rational.Invert).
        if (value.Value.IsZero)
            return AdditiveIdentity;

        var (_, u, _) = value.Value.ExtendedGcd(value.Field.DefiningPolynomial);
        return Create(u, value.Field);
    }

    // --- Equality ---

    public static bool operator ==(NumberFieldElement left, NumberFieldElement right) =>
        left.Field == right.Field && left.Value.Equals(right.Value);

    public static bool operator !=(NumberFieldElement left, NumberFieldElement right) =>
        !(left == right);

    public bool Equals(NumberFieldElement other) => this == other;
    public override bool Equals(object? obj) => obj is NumberFieldElement other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Field, Value);

    // --- Display ---

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var polyStr = Value.ToString(format, formatProvider);
        // Substitute the generator name for "x" in the polynomial string.
        return polyStr.Replace("x", Field.GeneratorName);
    }

    // --- Helpers ---

    private static NumberField ResolveField(NumberFieldElement left, NumberFieldElement right)
    {
        // If either field is default (from identity elements), use the other.
        if (left.Field == default) return right.Field;
        if (right.Field == default) return left.Field;
        // QuotientContext.Resolve will throw if contexts differ — let it handle mismatch.
        return left.Field;
    }

    public static NumberFieldElement FromInt(int n) =>
        new(default, QuotientRing<Polynomial<Rational>>.Create(
            Polynomial<Rational>.C((Rational)n),
            QuotientContext<Polynomial<Rational>>.Sentinel));

    static NumberFieldElement IRing<NumberFieldElement>.FromInt(int n) => FromInt(n);
}
