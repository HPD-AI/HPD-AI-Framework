namespace Helium.Hardware;

/// <summary>
/// Signed 4096-bit hardware lane with two's-complement wraparound semantics.
/// This is not an arbitrary-precision exact integer.
/// </summary>
public readonly struct Int4096 : IEquatable<Int4096>, IComparable<Int4096>, IFormattable
{
    public UInt4096 Bits { get; }

    public Int4096(long value)
    {
        Span<ulong> limbs = stackalloc ulong[UInt4096.LimbCount];
        limbs[0] = (ulong)value;
        if (value < 0)
        {
            for (var i = 1; i < limbs.Length; i++)
                limbs[i] = ulong.MaxValue;
        }

        Bits = new UInt4096(limbs);
    }

    public Int4096(UInt4096 bits) => Bits = bits;

    public ulong this[int index] => Bits[index];

    public bool IsNegative => (Bits[UInt4096.LimbCount - 1] & (1UL << 63)) != 0;

    public static Int4096 Zero => new(UInt4096.Zero);

    public static Int4096 One => new(UInt4096.One);

    public static Int4096 operator +(Int4096 left, Int4096 right) => new(left.Bits + right.Bits);
    public static Int4096 operator -(Int4096 left, Int4096 right) => new(left.Bits - right.Bits);
    public static Int4096 operator -(Int4096 value) => new(UInt4096.Zero - value.Bits);
    public static Int4096 operator *(Int4096 left, Int4096 right) => new(left.Bits * right.Bits);

    public static bool operator ==(Int4096 left, Int4096 right) => left.Equals(right);
    public static bool operator !=(Int4096 left, Int4096 right) => !left.Equals(right);
    public static bool operator <(Int4096 left, Int4096 right) => left.CompareTo(right) < 0;
    public static bool operator >(Int4096 left, Int4096 right) => left.CompareTo(right) > 0;
    public static bool operator <=(Int4096 left, Int4096 right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Int4096 left, Int4096 right) => left.CompareTo(right) >= 0;

    public int CompareTo(Int4096 other)
    {
        if (IsNegative != other.IsNegative)
            return IsNegative ? -1 : 1;

        return Bits.CompareTo(other.Bits);
    }

    public bool Equals(Int4096 other) => Bits.Equals(other.Bits);
    public override bool Equals(object? obj) => obj is Int4096 other && Equals(other);
    public override int GetHashCode() => Bits.GetHashCode();
    public override string ToString() => IsNegative ? $"-{(-this).Bits}" : Bits.ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        format switch
        {
            "X" or "x" => Bits.ToString(format, formatProvider),
            _ => ToString()
        };
}
