using System.Numerics;
using Helium.Primitives;

namespace Helium.Hardware;

/// <summary>
/// Exact finite-field element modulo 2^64 - 2^32 + 1.
/// This is a hardware-friendly domain type, not a raw overflow lane.
/// </summary>
public readonly struct GoldilocksElement :
    IField<GoldilocksElement>,
    IEquatable<GoldilocksElement>,
    IFormattable
{
    public const ulong Modulus = 0xFFFFFFFF00000001UL;
    public const ulong PrimitiveRoot = 7UL;

    public ulong Value { get; }

    public GoldilocksElement(ulong value) => Value = Reduce(value);

    private GoldilocksElement(ulong value, bool alreadyReduced) => Value = alreadyReduced ? value : Reduce(value);

    public static GoldilocksElement AdditiveIdentity => new(0UL, alreadyReduced: true);
    public static GoldilocksElement MultiplicativeIdentity => new(1UL, alreadyReduced: true);

    static GoldilocksElement IAdditiveIdentity<GoldilocksElement, GoldilocksElement>.AdditiveIdentity =>
        AdditiveIdentity;

    static GoldilocksElement IMultiplicativeIdentity<GoldilocksElement, GoldilocksElement>.MultiplicativeIdentity =>
        MultiplicativeIdentity;

    static GoldilocksElement IRing<GoldilocksElement>.FromInt(int n)
    {
        if (n >= 0)
            return new GoldilocksElement((ulong)n);

        var magnitude = (ulong)(-(long)n);
        return magnitude == 0
            ? AdditiveIdentity
            : new GoldilocksElement(Modulus - magnitude, alreadyReduced: true);
    }

    public static GoldilocksElement operator +(GoldilocksElement left, GoldilocksElement right) =>
        new(Reduce((System.UInt128)left.Value + right.Value), alreadyReduced: true);

    public static GoldilocksElement operator -(GoldilocksElement left, GoldilocksElement right) =>
        left.Value >= right.Value
            ? new GoldilocksElement(left.Value - right.Value, alreadyReduced: true)
            : new GoldilocksElement(Modulus - (right.Value - left.Value), alreadyReduced: true);

    public static GoldilocksElement operator -(GoldilocksElement value) =>
        value.Value == 0
            ? AdditiveIdentity
            : new GoldilocksElement(Modulus - value.Value, alreadyReduced: true);

    public static GoldilocksElement operator *(GoldilocksElement left, GoldilocksElement right) =>
        new(Reduce((System.UInt128)left.Value * right.Value), alreadyReduced: true);

    public static GoldilocksElement operator /(GoldilocksElement left, GoldilocksElement right) =>
        left * Invert(right);

    public static GoldilocksElement Invert(GoldilocksElement a)
    {
        if (a.Value == 0)
            return AdditiveIdentity;

        return Pow(a, Modulus - 2);
    }

    public static GoldilocksElement Pow(GoldilocksElement value, ulong exponent)
    {
        var result = MultiplicativeIdentity;
        var factor = value;
        var e = exponent;
        while (e != 0)
        {
            if ((e & 1UL) != 0)
                result *= factor;
            factor *= factor;
            e >>= 1;
        }
        return result;
    }

    public static bool operator ==(GoldilocksElement left, GoldilocksElement right) => left.Equals(right);
    public static bool operator !=(GoldilocksElement left, GoldilocksElement right) => !left.Equals(right);

    private static ulong Reduce(System.UInt128 value)
    {
        System.UInt128 remainder = 0;
        for (var bit = 127; bit >= 0; bit--)
        {
            remainder = (remainder << 1) | ((value >> bit) & 1);
            if (remainder >= Modulus)
                remainder -= Modulus;
        }

        return (ulong)remainder;
    }

    public bool Equals(GoldilocksElement other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is GoldilocksElement other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        Value.ToString(format, formatProvider);
}
