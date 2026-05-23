namespace Helium.Hardware;

/// <summary>
/// Signed 256-bit hardware lane with two's-complement wraparound semantics.
/// This is not an arbitrary-precision exact integer.
/// </summary>
public readonly struct Int256 : IEquatable<Int256>, IComparable<Int256>, IFormattable
{
    public UInt256 Bits { get; }

    public Int256(long value)
    {
        Bits = value < 0
            ? new UInt256((ulong)value, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue)
            : new UInt256((ulong)value);
    }

    public Int256(UInt256 bits) => Bits = bits;

    public ulong L0 => Bits.L0;
    public ulong L1 => Bits.L1;
    public ulong L2 => Bits.L2;
    public ulong L3 => Bits.L3;

    public bool IsNegative => (L3 & (1UL << 63)) != 0;

    public static Int256 Zero => new(UInt256.Zero);
    public static Int256 One => new(UInt256.One);

    public static Int256 operator +(Int256 left, Int256 right) => new(left.Bits + right.Bits);
    public static Int256 operator -(Int256 left, Int256 right) => new(left.Bits - right.Bits);
    public static Int256 operator -(Int256 value) => new(UInt256.Zero - value.Bits);
    public static Int256 operator *(Int256 left, Int256 right) => new(left.Bits * right.Bits);

    public static bool operator ==(Int256 left, Int256 right) => left.Equals(right);
    public static bool operator !=(Int256 left, Int256 right) => !left.Equals(right);
    public static bool operator <(Int256 left, Int256 right) => left.CompareTo(right) < 0;
    public static bool operator >(Int256 left, Int256 right) => left.CompareTo(right) > 0;
    public static bool operator <=(Int256 left, Int256 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Int256 left, Int256 right) => left.CompareTo(right) >= 0;

    public int CompareTo(Int256 other)
    {
        if (IsNegative != other.IsNegative)
            return IsNegative ? -1 : 1;
        return Bits.CompareTo(other.Bits);
    }

    public bool Equals(Int256 other) => Bits.Equals(other.Bits);
    public override bool Equals(object? obj) => obj is Int256 other && Equals(other);
    public override int GetHashCode() => Bits.GetHashCode();
    public override string ToString() => IsNegative ? $"-{(-this).Bits}" : Bits.ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        format switch
        {
            "X" or "x" => Bits.ToString(format, formatProvider),
            _ => ToString()
        };
}
