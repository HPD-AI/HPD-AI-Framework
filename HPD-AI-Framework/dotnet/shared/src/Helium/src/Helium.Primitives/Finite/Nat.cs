using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Helium-owned natural number key.
/// </summary>
public readonly struct Nat : IEquatable<Nat>, IDecidableEq<Nat>, ITotalOrder<Nat>, IAddCommMonoid<Nat>, IFormattable
{
    public int Value { get; }

    public Nat(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Nat requires a nonnegative value.");
        Value = value;
    }

    public static implicit operator Nat(int value) => new(value);
    public static explicit operator int(Nat value) => value.Value;

    static Nat IAdditiveIdentity<Nat, Nat>.AdditiveIdentity => new(0);

    public static Nat operator +(Nat left, Nat right) => new(checked(left.Value + right.Value));

    public static bool DecidableEquals(Nat left, Nat right) => left.Value == right.Value;
    public static bool LessEqual(Nat left, Nat right) => left.Value <= right.Value;
    public static Ordering CompareOrder(Nat left, Nat right) =>
        left.Value < right.Value ? Ordering.Less :
        left.Value > right.Value ? Ordering.Greater :
        Ordering.Equal;

    public bool Equals(Nat other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Nat other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(Nat left, Nat right) => left.Equals(right);
    public static bool operator !=(Nat left, Nat right) => !left.Equals(right);
    public override string ToString() => Value.ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Value.ToString(format, formatProvider);
}
