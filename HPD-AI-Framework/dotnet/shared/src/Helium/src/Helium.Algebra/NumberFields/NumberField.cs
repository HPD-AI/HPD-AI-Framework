using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// A number field K = Q(alpha) where alpha is a root of an irreducible polynomial f in Q[x].
/// The defining polynomial is the field context; element arithmetic reduces directly modulo f.
/// </summary>
public readonly struct NumberField : IEquatable<NumberField>
{
    /// <summary>The defining irreducible polynomial f in Q[x].</summary>
    public SparsePolynomial<Rational> DefiningPolynomial { get; }

    /// <summary>Degree of the extension [K : Q] = deg(f).</summary>
    public int Degree { get; }

    /// <summary>Name for the generator alpha in display output.</summary>
    public string GeneratorName { get; }

    internal bool IsDefault => GeneratorName is null;

    public NumberField(SparsePolynomial<Rational> f, string generatorName = "α")
    {
        DefiningPolynomial = f;
        Degree = f.Degree;
        GeneratorName = generatorName;
    }

    internal SparsePolynomial<Rational> Reduce(SparsePolynomial<Rational> value) =>
        IsDefault ? value : value.DivMod(DefiningPolynomial).Remainder;

    public bool Equals(NumberField other)
    {
        if (IsDefault || other.IsDefault)
            return IsDefault && other.IsDefault;
        return DefiningPolynomial.Equals(other.DefiningPolynomial);
    }

    public override bool Equals(object? obj) => obj is NumberField other && Equals(other);
    public override int GetHashCode() => IsDefault ? 0 : DefiningPolynomial.GetHashCode();
    public static bool operator ==(NumberField left, NumberField right) => left.Equals(right);
    public static bool operator !=(NumberField left, NumberField right) => !left.Equals(right);

    public override string ToString() =>
        IsDefault ? "Q" : $"Q({GeneratorName}) = Q[x]/({DefiningPolynomial})";
}

/// <summary>
/// An element of a number field K = Q(alpha). Represented as p(alpha) where p in Q[x]
/// has degree less than [K:Q]. Inversion uses Bezout's identity modulo the defining polynomial.
/// </summary>
public readonly struct NumberFieldElement : IField<NumberFieldElement>, IEquatable<NumberFieldElement>
{
    public NumberField Field { get; }

    /// <summary>The canonical polynomial representative, deg less than Field.Degree.</summary>
    public SparsePolynomial<Rational> Value { get; }

    private NumberFieldElement(NumberField field, SparsePolynomial<Rational> value)
    {
        Field = field;
        Value = field.Reduce(value);
    }

    /// <summary>Create an element from a polynomial p; reduces p mod f automatically.</summary>
    public static NumberFieldElement Create(SparsePolynomial<Rational> p, NumberField field) =>
        new(field, p);

    /// <summary>The generator alpha, the image of x in Q[x]/f.</summary>
    public static NumberFieldElement Generator(NumberField field) =>
        Create(SparsePolynomial<Rational>.X, field);

    public static NumberFieldElement AdditiveIdentity =>
        new(default, SparsePolynomial<Rational>.Zero);

    public static NumberFieldElement MultiplicativeIdentity =>
        new(default, SparsePolynomial<Rational>.One);

    static NumberFieldElement IAdditiveIdentity<NumberFieldElement, NumberFieldElement>.AdditiveIdentity =>
        AdditiveIdentity;

    static NumberFieldElement IMultiplicativeIdentity<NumberFieldElement, NumberFieldElement>.MultiplicativeIdentity =>
        MultiplicativeIdentity;

    public static NumberFieldElement operator +(NumberFieldElement left, NumberFieldElement right)
    {
        var field = ResolveField(left, right);
        return new(field, left.Value + right.Value);
    }

    public static NumberFieldElement operator -(NumberFieldElement left, NumberFieldElement right)
    {
        var field = ResolveField(left, right);
        return new(field, left.Value - right.Value);
    }

    public static NumberFieldElement operator *(NumberFieldElement left, NumberFieldElement right)
    {
        var field = ResolveField(left, right);
        return new(field, left.Value * right.Value);
    }

    public static NumberFieldElement operator -(NumberFieldElement value) =>
        new(value.Field, -value.Value);

    public static NumberFieldElement operator /(NumberFieldElement left, NumberFieldElement right) =>
        left * Invert(right);

    /// <summary>
    /// Inversion via Bezout: for nonzero a in Q[x]/(f), extended GCD gives u with u*a = 1 mod f.
    /// </summary>
    public static NumberFieldElement Invert(NumberFieldElement value)
    {
        // Convention: Invert(0) = 0, matching the total-function IField contract.
        if (value.Value.IsZero)
            return AdditiveIdentity;

        if (value.Field.IsDefault)
        {
            var inverse = Rational.Invert(value.Value[0]);
            return new(default, SparsePolynomial<Rational>.C(inverse));
        }

        var (_, u, _) = value.Value.ExtendedGcd(value.Field.DefiningPolynomial);
        return Create(u, value.Field);
    }

    public static bool operator ==(NumberFieldElement left, NumberFieldElement right) =>
        left.Field == right.Field && left.Value.Equals(right.Value);

    public static bool operator !=(NumberFieldElement left, NumberFieldElement right) =>
        !(left == right);

    public bool Equals(NumberFieldElement other) => this == other;
    public override bool Equals(object? obj) => obj is NumberFieldElement other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Field, Value);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var polyStr = Value.ToString(format, formatProvider);
        return Field.IsDefault ? polyStr : polyStr.Replace("x", Field.GeneratorName);
    }

    private static NumberField ResolveField(NumberFieldElement left, NumberFieldElement right)
    {
        if (left.Field.IsDefault) return right.Field;
        if (right.Field.IsDefault) return left.Field;
        if (left.Field == right.Field) return left.Field;
        throw new InvalidOperationException(
            $"Cannot mix elements from number fields {left.Field} and {right.Field}.");
    }

    public static NumberFieldElement FromInt(int n) =>
        new(default, SparsePolynomial<Rational>.C((Rational)n));

    static NumberFieldElement IRing<NumberFieldElement>.FromInt(int n) => FromInt(n);
}
