using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// IEEE 754 single-precision float wrapped to implement Helium's algebraic interfaces.
/// Follows the total-function convention: Invert(0) = 0.
/// Inexact arithmetic — not decidable — same status as Complex.
/// </summary>
public readonly struct Float :
    IField<Float>,
    ICharP<Float>,
    IFormattable
{
    private readonly float _value;

    public Float(float value) => _value = value;

    public static implicit operator Float(float f) => new(f);
    public static explicit operator float(Float f) => f._value;

    // --- Identity elements ---

    public static Float Zero => new(0.0f);
    public static Float One  => new(1.0f);

    static Float IAdditiveIdentity<Float, Float>.AdditiveIdentity        => Zero;
    static Float IMultiplicativeIdentity<Float, Float>.MultiplicativeIdentity => One;

    // --- IRing.FromInt override ---

    static Float IRing<Float>.FromInt(int n) => new((float)n);

    // --- Arithmetic operators ---

    public static Float operator +(Float a, Float b) => new(a._value + b._value);
    public static Float operator -(Float a, Float b) => new(a._value - b._value);
    public static Float operator *(Float a, Float b) => new(a._value * b._value);
    public static Float operator /(Float a, Float b) => new(a._value / b._value);
    public static Float operator -(Float a)          => new(-a._value);

    // --- IField ---

    public static Float Invert(Float a) =>
        a._value == 0.0f ? Zero : new(1.0f / a._value);

    // --- ICharP ---

    public static int Characteristic => 0;

    // --- Equality (IEEE 754 semantics: NaN != NaN) ---

    public bool Equals(Float other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Float other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public static bool operator ==(Float a, Float b) => a._value == b._value;
    public static bool operator !=(Float a, Float b) => a._value != b._value;

    // --- Helpers ---

    public bool IsZero => _value == 0.0f;

    // --- Formatting ---

    public override string ToString() => _value.ToString();

    public string ToString(string? format, IFormatProvider? provider) =>
        format switch
        {
            "M" => $"<mn>{_value}</mn>",
            _   => _value.ToString(format, provider)
        };
}
