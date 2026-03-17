using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// IEEE 754 double-precision float wrapped to implement Helium's algebraic interfaces.
/// Follows the total-function convention: Invert(0) = 0.
/// Inexact arithmetic — not decidable — same status as Complex.
/// </summary>
public readonly struct Double :
    IField<Double>,
    ICharP<Double>,
    IFormattable
{
    private readonly double _value;

    public Double(double value) => _value = value;

    public static implicit operator Double(double d) => new(d);
    public static explicit operator double(Double d) => d._value;

    // --- Identity elements ---

    public static Double Zero => new(0.0);
    public static Double One  => new(1.0);

    static Double IAdditiveIdentity<Double, Double>.AdditiveIdentity        => Zero;
    static Double IMultiplicativeIdentity<Double, Double>.MultiplicativeIdentity => One;

    // --- IRing.FromInt override ---

    static Double IRing<Double>.FromInt(int n) => new((double)n);

    // --- Arithmetic operators ---

    public static Double operator +(Double a, Double b) => new(a._value + b._value);
    public static Double operator -(Double a, Double b) => new(a._value - b._value);
    public static Double operator *(Double a, Double b) => new(a._value * b._value);
    public static Double operator /(Double a, Double b) => new(a._value / b._value);
    public static Double operator -(Double a)           => new(-a._value);

    // --- IField ---

    public static Double Invert(Double a) =>
        a._value == 0.0 ? Zero : new(1.0 / a._value);

    // --- ICharP ---

    public static int Characteristic => 0;

    // --- Equality (IEEE 754 semantics: NaN != NaN) ---

    public bool Equals(Double other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Double other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public static bool operator ==(Double a, Double b) => a._value == b._value;
    public static bool operator !=(Double a, Double b) => a._value != b._value;

    // --- Helpers ---

    public bool IsZero => _value == 0.0;

    // --- Formatting ---

    public override string ToString() => _value.ToString();

    public string ToString(string? format, IFormatProvider? provider) =>
        format switch
        {
            "M" => $"<mn>{_value}</mn>",
            _   => _value.ToString(format, provider)
        };
}
