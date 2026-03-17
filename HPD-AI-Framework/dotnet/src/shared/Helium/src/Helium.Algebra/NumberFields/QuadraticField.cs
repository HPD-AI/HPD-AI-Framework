using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// An element of a quadratic field Q(√D): (A + B√D) / Denom, where D is squarefree.
/// A, B, Denom ∈ Z with Denom > 0, kept in reduced form (gcd(A, B, Denom) = 1).
///
/// Arithmetic is O(1) closed-form, unlike the O(n) polynomial arithmetic of NumberFieldElement.
/// D &lt; 0: imaginary quadratic field (e.g. Q(i) = Q(√-1), Q(√-3) = Q(ζ₃) for cyclotomic).
/// D &gt; 0: real quadratic field.
///
/// No structural relationship to NumberFieldElement — both implement IField&lt;T&gt; independently.
/// </summary>
public readonly struct QuadraticFieldElement : IField<QuadraticFieldElement>, IEquatable<QuadraticFieldElement>
{
    /// <summary>Rational part numerator.</summary>
    public Integer A { get; }

    /// <summary>Irrational part numerator (coefficient of √D).</summary>
    public Integer B { get; }

    /// <summary>Common denominator (always positive).</summary>
    public Integer Denom { get; }

    /// <summary>Squarefree discriminant D (may be negative).</summary>
    public Integer D { get; }

    private QuadraticFieldElement(Integer a, Integer b, Integer d, Integer denom)
    {
        // Normalize: denom always positive.
        if (denom < Integer.Zero)
        {
            a = -a;
            b = -b;
            denom = -denom;
        }

        // Reduce by gcd(|A|, |B|, Denom).
        var g = Integer.Gcd(Integer.Gcd(a.Abs(), b.Abs()), denom);
        if (g > Integer.One)
        {
            a = a / g;
            b = b / g;
            denom = denom / g;
        }

        A = a;
        B = b;
        D = d;
        Denom = denom;
    }

    /// <summary>Create element (A + B√D) / Denom in Q(√D).</summary>
    public static QuadraticFieldElement Create(Integer a, Integer b, Integer d, Integer denom) =>
        new(a, b, d, denom);

    /// <summary>Embed a rational: a/denom in Q(√D).</summary>
    public static QuadraticFieldElement Rational(Integer a, Integer denom, Integer d) =>
        new(a, Integer.Zero, d, denom);

    /// <summary>The generator √D in Q(√D).</summary>
    public static QuadraticFieldElement SqrtD(Integer d) =>
        new(Integer.Zero, Integer.One, d, Integer.One);

    // --- IField identity elements ---

    public static QuadraticFieldElement AdditiveIdentity =>
        new(Integer.Zero, Integer.Zero, Integer.Zero, Integer.One);

    public static QuadraticFieldElement MultiplicativeIdentity =>
        new(Integer.One, Integer.Zero, Integer.Zero, Integer.One);

    static QuadraticFieldElement IAdditiveIdentity<QuadraticFieldElement, QuadraticFieldElement>.AdditiveIdentity =>
        AdditiveIdentity;

    static QuadraticFieldElement IMultiplicativeIdentity<QuadraticFieldElement, QuadraticFieldElement>.MultiplicativeIdentity =>
        MultiplicativeIdentity;

    // --- Arithmetic ---

    public static QuadraticFieldElement operator +(QuadraticFieldElement left, QuadraticFieldElement right)
    {
        CheckCompatible(left, right);
        // (A₁/d₁) + (A₂/d₂): common denom = d₁*d₂
        var d1 = left.Denom;
        var d2 = right.Denom;
        return new(
            left.A * d2 + right.A * d1,
            left.B * d2 + right.B * d1,
            left.D,
            d1 * d2);
    }

    public static QuadraticFieldElement operator -(QuadraticFieldElement left, QuadraticFieldElement right)
    {
        CheckCompatible(left, right);
        var d1 = left.Denom;
        var d2 = right.Denom;
        return new(
            left.A * d2 - right.A * d1,
            left.B * d2 - right.B * d1,
            left.D,
            d1 * d2);
    }

    public static QuadraticFieldElement operator *(QuadraticFieldElement left, QuadraticFieldElement right)
    {
        CheckCompatible(left, right);
        // (A₁ + B₁√D)(A₂ + B₂√D) = (A₁A₂ + B₁B₂D) + (A₁B₂ + A₂B₁)√D
        return new(
            left.A * right.A + left.B * right.B * left.D,
            left.A * right.B + right.A * left.B,
            left.D,
            left.Denom * right.Denom);
    }

    public static QuadraticFieldElement operator -(QuadraticFieldElement value) =>
        new(-value.A, -value.B, value.D, value.Denom);

    /// <summary>
    /// Inversion via conjugate over norm: (A + B√D)⁻¹ = (A - B√D) / (A² - DB²).
    /// Convention: Invert(0) = 0.
    /// </summary>
    public static QuadraticFieldElement Invert(QuadraticFieldElement value)
    {
        if (value.A.IsZero && value.B.IsZero)
            return AdditiveIdentity;

        // Norm = A² - D*B² (over Z, before denominator)
        // Norm numerator over Z (Denom² in denominator absorbed by construction).
        var normNum = value.A * value.A - value.D * value.B * value.B;
        // (A + B√D)/Denom inverted = Denom*(A - B√D) / normNum
        return new(
            value.Denom * value.A,
            -(value.Denom * value.B),
            value.D,
            normNum);
    }

    public static QuadraticFieldElement operator /(QuadraticFieldElement left, QuadraticFieldElement right) =>
        left * Invert(right);

    // --- Number-theoretic operations ---

    /// <summary>Norm: N(α) = (A² - D*B²) / Denom² (element of Q).</summary>
    public Primitives.Rational Norm() =>
        Primitives.Rational.Create(A * A - D * B * B, Denom * Denom);

    /// <summary>Trace: Tr(α) = 2A / Denom (element of Q).</summary>
    public Primitives.Rational Trace() =>
        Primitives.Rational.Create((Integer)2 * A, Denom);

    /// <summary>Conjugate: (A - B√D) / Denom.</summary>
    public QuadraticFieldElement Conjugate() => new(A, -B, D, Denom);

    // --- Equality ---

    public static bool operator ==(QuadraticFieldElement left, QuadraticFieldElement right) =>
        left.D == right.D && left.A == right.A && left.B == right.B && left.Denom == right.Denom;

    public static bool operator !=(QuadraticFieldElement left, QuadraticFieldElement right) => !(left == right);

    public bool Equals(QuadraticFieldElement other) => this == other;
    public override bool Equals(object? obj) => obj is QuadraticFieldElement other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(A, B, D, Denom);

    // --- Display ---

    public override string ToString()
    {
        if (B.IsZero)
            return Denom == Integer.One ? A.ToString() : $"{A}/{Denom}";

        var sqrtPart = B == Integer.One ? $"√{D}" :
                       B == -Integer.One ? $"-√{D}" :
                       $"{B}√{D}";

        var num = A.IsZero ? sqrtPart : $"{A} + {sqrtPart}";
        return Denom == Integer.One ? num : $"({num})/{Denom}";
    }

    public static QuadraticFieldElement FromInt(int n) =>
        new((Integer)n, Integer.Zero, Integer.Zero, Integer.One);

    static QuadraticFieldElement IRing<QuadraticFieldElement>.FromInt(int n) => FromInt(n);

    // --- Helpers ---

    private static void CheckCompatible(QuadraticFieldElement left, QuadraticFieldElement right)
    {
        if (left.D != right.D && !(left.B.IsZero || right.B.IsZero))
            throw new InvalidOperationException(
                $"Cannot mix QuadraticFieldElement with D={left.D} and D={right.D}.");
    }
}
