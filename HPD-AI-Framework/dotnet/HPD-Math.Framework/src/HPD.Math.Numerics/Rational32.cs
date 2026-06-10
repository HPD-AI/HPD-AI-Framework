using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Canonical fixed-width rational number. Denominator is always positive.
/// Arithmetic that may overflow is exposed through status-returning kernels.
/// </summary>
public readonly struct Rational32 : IEquatable<Rational32>
{
    public Rational32(int numerator, int denominator)
    {
        if (denominator == 0)
        {
            Numerator = 0;
            Denominator = 1;
            return;
        }

        var status = Rational32Kernels.TryCreate(numerator, denominator, out var normalized);
        this = status == AlgebraStatus.Ok ? normalized : default;
    }

    internal Rational32(int numerator, int denominator, bool _)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public int Numerator { get; }

    public int Denominator { get; }

    public static Rational32 Zero => new(0, 1, true);

    public static Rational32 One => new(1, 1, true);

    public static bool Eq(in Rational32 left, in Rational32 right) =>
        left.Numerator == right.Numerator && left.Denominator == right.Denominator;

    public static bool LessEqual(in Rational32 left, in Rational32 right) =>
        (long)left.Numerator * right.Denominator <= (long)right.Numerator * left.Denominator;

    public static Ordering Compare(in Rational32 left, in Rational32 right)
    {
        var lhs = (long)left.Numerator * right.Denominator;
        var rhs = (long)right.Numerator * left.Denominator;
        return lhs < rhs ? Ordering.Less :
            lhs > rhs ? Ordering.Greater :
            Ordering.Equal;
    }

    public bool Equals(Rational32 other) => Eq(this, other);

    public override bool Equals(object? obj) => obj is Rational32 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public override string ToString() =>
        Denominator == 1 ? Numerator.ToString() : Numerator + "/" + Denominator;

    public static bool operator ==(Rational32 left, Rational32 right) => left.Equals(right);

    public static bool operator !=(Rational32 left, Rational32 right) => !left.Equals(right);
}
