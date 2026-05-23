using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Fixed-dimension vector over a commutative ring. The static dimension witness makes the module identity lawful.
/// </summary>
public readonly struct FixedVector<R, D> :
    IModule<R, FixedVector<R, D>>,
    IEquatable<FixedVector<R, D>>,
    IFormattable
    where R : ICommRing<R>
    where D : IFiniteDimension
{
    private readonly Vector<R> _value;

    private FixedVector(Vector<R> value)
    {
        if (value.Length != D.Value)
            throw new ArgumentException("Vector length must match the static dimension witness.", nameof(value));

        _value = value;
    }

    public int Length => D.Value;

    public Vector<R> Value => _value;

    public R this[int index] => _value[index];

    static FixedVector<R, D> System.Numerics.IAdditiveIdentity<FixedVector<R, D>, FixedVector<R, D>>.AdditiveIdentity => Zero;

    public static FixedVector<R, D> Zero => new(Vector<R>.Zero(D.Value));

    public static FixedVector<R, D> FromVector(Vector<R> value) => new(value);

    public static FixedVector<R, D> FromArray(params ReadOnlySpan<R> values) =>
        new(Vector<R>.FromArray(values));

    public static FixedVector<R, D> operator +(FixedVector<R, D> left, FixedVector<R, D> right) =>
        new(left._value + right._value);

    public static FixedVector<R, D> operator -(FixedVector<R, D> left, FixedVector<R, D> right) =>
        new(left._value - right._value);

    public static FixedVector<R, D> operator -(FixedVector<R, D> value) =>
        new(-value._value);

    public static FixedVector<R, D> operator *(R scalar, FixedVector<R, D> value) =>
        new(scalar * value._value);

    public static FixedVector<R, D> ScalarMultiply(R scalar, FixedVector<R, D> element) =>
        scalar * element;

    public static bool DecidableEquals(FixedVector<R, D> left, FixedVector<R, D> right) =>
        left == right;

    public bool Equals(FixedVector<R, D> other) => _value.Equals(other._value);
    public override bool Equals(object? obj) => obj is FixedVector<R, D> other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public static bool operator ==(FixedVector<R, D> left, FixedVector<R, D> right) => left.Equals(right);
    public static bool operator !=(FixedVector<R, D> left, FixedVector<R, D> right) => !left.Equals(right);
    public override string ToString() => _value.ToString();
    public string ToString(string? format, IFormatProvider? provider) => _value.ToString(format, provider);
}
