using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// A finite field GF(p^n) represented as F_p[x]/(f), where p is prime and
/// f is an irreducible polynomial over F_p.
/// </summary>
public readonly struct FiniteField : IEquatable<FiniteField>
{
    public Integer Characteristic { get; }

    public SparsePolynomial<ZModElement> DefiningPolynomial { get; }

    public int Degree { get; }

    public string GeneratorName { get; }

    internal bool IsDefault => GeneratorName is null;

    public FiniteField(Integer characteristic, SparsePolynomial<ZModElement> definingPolynomial, string generatorName = "α")
    {
        if (characteristic <= Integer.One)
            throw new ArgumentOutOfRangeException(nameof(characteristic), "Finite field characteristic must be greater than one.");
        if (definingPolynomial.Degree <= 0)
            throw new ArgumentException("Finite field defining polynomial must have positive degree.", nameof(definingPolynomial));

        Characteristic = characteristic;
        DefiningPolynomial = definingPolynomial;
        Degree = definingPolynomial.Degree;
        GeneratorName = generatorName;
    }

    public static FiniteField CreatePrimePower(
        Integer characteristic,
        SparsePolynomial<ZModElement> definingPolynomial,
        string generatorName = "α") =>
        new(characteristic, definingPolynomial, generatorName);

    public static ZModElement Coefficient(Integer value, Integer characteristic) =>
        ZMod.Create(value, characteristic);

    internal SparsePolynomial<ZModElement> Reduce(SparsePolynomial<ZModElement> value) =>
        IsDefault ? value : DivMod(value, DefiningPolynomial, Characteristic).Remainder;

    public bool Equals(FiniteField other)
    {
        if (IsDefault || other.IsDefault)
            return IsDefault && other.IsDefault;
        return Characteristic.Equals(other.Characteristic) &&
               DefiningPolynomial.Equals(other.DefiningPolynomial);
    }

    public override bool Equals(object? obj) => obj is FiniteField other && Equals(other);
    public override int GetHashCode() => IsDefault ? 0 : HashCode.Combine(Characteristic, DefiningPolynomial);
    public static bool operator ==(FiniteField left, FiniteField right) => left.Equals(right);
    public static bool operator !=(FiniteField left, FiniteField right) => !left.Equals(right);

    public override string ToString() =>
        IsDefault ? "F_p" : $"GF({Characteristic}^{Degree}) = F_{Characteristic}[x]/({DefiningPolynomial})";

    internal static (SparsePolynomial<ZModElement> Quotient, SparsePolynomial<ZModElement> Remainder) DivMod(
        SparsePolynomial<ZModElement> dividend,
        SparsePolynomial<ZModElement> divisor,
        Integer characteristic)
    {
        if (divisor.IsZero)
            return (SparsePolynomial<ZModElement>.Zero, SparsePolynomial<ZModElement>.Zero);

        var remainder = dividend;
        var quotient = SparsePolynomial<ZModElement>.Zero;
        var divisorDegree = divisor.Degree;
        var divisorLcInv = InvertCoefficient(divisor.LeadingCoefficient, characteristic);

        while (!remainder.IsZero && remainder.Degree >= divisorDegree)
        {
            var coefficient = remainder.LeadingCoefficient * divisorLcInv;
            var degree = remainder.Degree - divisorDegree;
            var term = SparsePolynomial<ZModElement>.Monomial(degree, coefficient);
            quotient += term;
            remainder -= term * divisor;
        }

        return (quotient, remainder);
    }

    internal static (SparsePolynomial<ZModElement> Gcd, SparsePolynomial<ZModElement> U, SparsePolynomial<ZModElement> V)
        ExtendedGcd(SparsePolynomial<ZModElement> left, SparsePolynomial<ZModElement> right, Integer characteristic)
    {
        var oldR = left;
        var r = right;
        var oldU = SparsePolynomial<ZModElement>.One;
        var u = SparsePolynomial<ZModElement>.Zero;
        var oldV = SparsePolynomial<ZModElement>.Zero;
        var v = SparsePolynomial<ZModElement>.One;

        while (!r.IsZero)
        {
            var (q, rem) = DivMod(oldR, r, characteristic);
            (oldR, r) = (r, rem);
            (oldU, u) = (u, oldU - q * u);
            (oldV, v) = (v, oldV - q * v);
        }

        if (oldR.IsZero)
            return (oldR, oldU, oldV);

        var lcInv = InvertCoefficient(oldR.LeadingCoefficient, characteristic);
        var scale = SparsePolynomial<ZModElement>.C(lcInv);
        return (oldR * scale, oldU * scale, oldV * scale);
    }

    internal static ZModElement InvertCoefficient(ZModElement value, Integer characteristic)
    {
        if (value.Value.IsZero)
            return ZMod.Create(Integer.Zero, characteristic);

        var (g, s, _) = ExtendedIntegerGcd(value.Value, characteristic);
        if (g != Integer.One)
            throw new ArithmeticException($"Coefficient {value} is not invertible modulo {characteristic}.");

        return ZMod.Create(s, characteristic);
    }

    private static (Integer Gcd, Integer S, Integer T) ExtendedIntegerGcd(Integer a, Integer b)
    {
        var oldR = a;
        var r = b;
        var oldS = Integer.One;
        var s = Integer.Zero;
        var oldT = Integer.Zero;
        var t = Integer.One;

        while (!r.IsZero)
        {
            var (q, rem) = Integer.DivMod(oldR, r);
            (oldR, r) = (r, rem);
            (oldS, s) = (s, oldS - q * s);
            (oldT, t) = (t, oldT - q * t);
        }

        if (oldR.Sign < 0)
            return (-oldR, -oldS, -oldT);
        return (oldR, oldS, oldT);
    }
}

/// <summary>
/// An element of GF(p^n), represented by its canonical polynomial residue.
/// </summary>
public readonly struct FiniteFieldElement :
    IField<FiniteFieldElement>,
    IEquatable<FiniteFieldElement>
{
    public FiniteField Field { get; }

    public SparsePolynomial<ZModElement> Value { get; }

    private FiniteFieldElement(FiniteField field, SparsePolynomial<ZModElement> value)
    {
        Field = field;
        Value = field.Reduce(value);
    }

    public static FiniteFieldElement Create(SparsePolynomial<ZModElement> value, FiniteField field) =>
        new(field, value);

    public static FiniteFieldElement Generator(FiniteField field) =>
        Create(SparsePolynomial<ZModElement>.X, field);

    public static FiniteFieldElement AdditiveIdentity =>
        new(default, SparsePolynomial<ZModElement>.Zero);

    public static FiniteFieldElement MultiplicativeIdentity =>
        new(default, SparsePolynomial<ZModElement>.One);

    static FiniteFieldElement IAdditiveIdentity<FiniteFieldElement, FiniteFieldElement>.AdditiveIdentity =>
        AdditiveIdentity;

    static FiniteFieldElement IMultiplicativeIdentity<FiniteFieldElement, FiniteFieldElement>.MultiplicativeIdentity =>
        MultiplicativeIdentity;

    public static FiniteFieldElement operator +(FiniteFieldElement left, FiniteFieldElement right)
    {
        var field = ResolveField(left, right);
        return new(field, left.Value + right.Value);
    }

    public static FiniteFieldElement operator -(FiniteFieldElement left, FiniteFieldElement right)
    {
        var field = ResolveField(left, right);
        return new(field, left.Value - right.Value);
    }

    public static FiniteFieldElement operator *(FiniteFieldElement left, FiniteFieldElement right)
    {
        var field = ResolveField(left, right);
        return new(field, left.Value * right.Value);
    }

    public static FiniteFieldElement operator -(FiniteFieldElement value) =>
        new(value.Field, -value.Value);

    public static FiniteFieldElement operator /(FiniteFieldElement left, FiniteFieldElement right) =>
        left * Invert(right);

    public static FiniteFieldElement Invert(FiniteFieldElement value)
    {
        if (value.Value.IsZero)
            return AdditiveIdentity;

        if (value.Field.IsDefault)
            return value;

        var (_, u, _) = FiniteField.ExtendedGcd(
            value.Value,
            value.Field.DefiningPolynomial,
            value.Field.Characteristic);
        return Create(u, value.Field);
    }

    public static FiniteFieldElement FromInt(int n) =>
        new(default, SparsePolynomial<ZModElement>.C(ZMod.Create((Integer)n, Integer.Zero)));

    static FiniteFieldElement IRing<FiniteFieldElement>.FromInt(int n) => FromInt(n);

    public static bool operator ==(FiniteFieldElement left, FiniteFieldElement right) =>
        left.Field == right.Field && left.Value.Equals(right.Value);

    public static bool operator !=(FiniteFieldElement left, FiniteFieldElement right) => !(left == right);

    public bool Equals(FiniteFieldElement other) => this == other;
    public override bool Equals(object? obj) => obj is FiniteFieldElement other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Field, Value);

    public override string ToString()
    {
        var poly = Value.ToString();
        return Field.IsDefault ? poly : poly.Replace("x", Field.GeneratorName);
    }

    private static FiniteField ResolveField(FiniteFieldElement left, FiniteFieldElement right)
    {
        if (left.Field.IsDefault) return right.Field;
        if (right.Field.IsDefault) return left.Field;
        if (left.Field == right.Field) return left.Field;
        throw new InvalidOperationException(
            $"Cannot mix elements from finite fields {left.Field} and {right.Field}.");
    }
}
