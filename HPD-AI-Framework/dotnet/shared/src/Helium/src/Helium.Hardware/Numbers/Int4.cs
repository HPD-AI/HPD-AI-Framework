namespace Helium.Hardware;

/// <summary>
/// Signed 4-bit hardware lane with two's-complement wraparound semantics.
/// Value range is -8..7. This is not an exact integer type.
/// </summary>
public readonly struct Int4 : IEquatable<Int4>, IComparable<Int4>, IFormattable
{
    private readonly byte _rawNibble;

    private Int4(byte rawNibble) => _rawNibble = (byte)(rawNibble & 0x0F);

    public Int4(int value) => _rawNibble = (byte)(value & 0x0F);

    public byte RawNibble => _rawNibble;

    public int Value
    {
        get
        {
            var value = _rawNibble & 0x0F;
            return (value & 0x08) == 0 ? value : value - 0x10;
        }
    }

    public static Int4 FromRawNibble(byte rawNibble) => new(rawNibble);

    public static Int4 operator +(Int4 left, Int4 right) => new(left.Value + right.Value);
    public static Int4 operator -(Int4 left, Int4 right) => new(left.Value - right.Value);
    public static Int4 operator -(Int4 value) => new(-value.Value);
    public static Int4 operator *(Int4 left, Int4 right) => new(left.Value * right.Value);

    public static bool operator ==(Int4 left, Int4 right) => left.Equals(right);
    public static bool operator !=(Int4 left, Int4 right) => !left.Equals(right);
    public static bool operator <(Int4 left, Int4 right) => left.Value < right.Value;
    public static bool operator >(Int4 left, Int4 right) => left.Value > right.Value;
    public static bool operator <=(Int4 left, Int4 right) => left.Value <= right.Value;
    public static bool operator >=(Int4 left, Int4 right) => left.Value >= right.Value;

    public int CompareTo(Int4 other) => Value.CompareTo(other.Value);
    public bool Equals(Int4 other) => _rawNibble == other._rawNibble;
    public override bool Equals(object? obj) => obj is Int4 other && Equals(other);
    public override int GetHashCode() => _rawNibble.GetHashCode();
    public override string ToString() => Value.ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Value.ToString(format, formatProvider);

    public static explicit operator int(Int4 value) => value.Value;
    public static explicit operator Int4(int value) => new(value);
}
