using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Helium-owned polynomial degree key.
/// </summary>
public readonly struct Degree : IEquatable<Degree>, IDecidableEq<Degree>, ITotalOrder<Degree>, IAddCommMonoid<Degree>, IFormattable
{
    public int Value { get; }

    public Degree(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Degree requires a nonnegative value.");
        Value = value;
    }

    public static implicit operator Degree(int value) => new(value);
    public static explicit operator int(Degree value) => value.Value;

    static Degree IAdditiveIdentity<Degree, Degree>.AdditiveIdentity => new(0);

    public static Degree operator +(Degree left, Degree right) => new(checked(left.Value + right.Value));

    public static bool DecidableEquals(Degree left, Degree right) => left.Value == right.Value;
    public static bool LessEqual(Degree left, Degree right) => left.Value <= right.Value;
    public static Ordering CompareOrder(Degree left, Degree right) =>
        left.Value < right.Value ? Ordering.Less :
        left.Value > right.Value ? Ordering.Greater :
        Ordering.Equal;

    public bool Equals(Degree other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Degree other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(Degree left, Degree right) => left.Equals(right);
    public static bool operator !=(Degree left, Degree right) => !left.Equals(right);
    public override string ToString() => Value.ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Value.ToString(format, formatProvider);
}
