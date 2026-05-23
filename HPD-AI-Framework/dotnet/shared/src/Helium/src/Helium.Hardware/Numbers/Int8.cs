namespace Helium.Hardware;

/// <summary>
/// Signed 8-bit hardware lane with two's-complement wraparound semantics.
/// This is not an exact integer type.
/// </summary>
public readonly struct Int8 : IEquatable<Int8>, IComparable<Int8>, IFormattable
{
    public sbyte Value { get; }

    public Int8(int value) => Value = unchecked((sbyte)value);

    public byte RawByte => unchecked((byte)Value);

    public static Int8 FromRawByte(byte rawByte) => new(unchecked((sbyte)rawByte));

    public static Int8 operator +(Int8 left, Int8 right) => new(left.Value + right.Value);
    public static Int8 operator -(Int8 left, Int8 right) => new(left.Value - right.Value);
    public static Int8 operator -(Int8 value) => new(-value.Value);
    public static Int8 operator *(Int8 left, Int8 right) => new(left.Value * right.Value);

    public static bool operator ==(Int8 left, Int8 right) => left.Equals(right);
    public static bool operator !=(Int8 left, Int8 right) => !left.Equals(right);
    public static bool operator <(Int8 left, Int8 right) => left.Value < right.Value;
    public static bool operator >(Int8 left, Int8 right) => left.Value > right.Value;
    public static bool operator <=(Int8 left, Int8 right) => left.Value <= right.Value;
    public static bool operator >=(Int8 left, Int8 right) => left.Value >= right.Value;

    public int CompareTo(Int8 other) => Value.CompareTo(other.Value);
    public bool Equals(Int8 other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is Int8 other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Value.ToString(format, formatProvider);

    public static explicit operator int(Int8 value) => value.Value;
    public static explicit operator Int8(int value) => new(value);
}
