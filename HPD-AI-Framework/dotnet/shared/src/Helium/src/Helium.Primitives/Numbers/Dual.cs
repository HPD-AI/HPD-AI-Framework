using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Dual number a + bε with ε² = 0. Used for exact forward-mode autodiff over fields.
/// </summary>
public readonly struct Dual<T> :
    IField<Dual<T>>,
    IEquatable<Dual<T>>,
    IFormattable
    where T : IField<T>
{
    public T Primal { get; }

    public T Tangent { get; }

    public Dual(T primal, T tangent)
    {
        Primal = primal;
        Tangent = tangent;
    }

    public static Dual<T> Constant(T value) => new(value, T.AdditiveIdentity);

    public static Dual<T> Seed(T value) => new(value, T.MultiplicativeIdentity);

    public static Dual<T> AdditiveIdentity =>
        new(T.AdditiveIdentity, T.AdditiveIdentity);

    public static Dual<T> MultiplicativeIdentity =>
        new(T.MultiplicativeIdentity, T.AdditiveIdentity);

    static Dual<T> IAdditiveIdentity<Dual<T>, Dual<T>>.AdditiveIdentity => AdditiveIdentity;

    static Dual<T> IMultiplicativeIdentity<Dual<T>, Dual<T>>.MultiplicativeIdentity =>
        MultiplicativeIdentity;

    static Dual<T> IRing<Dual<T>>.FromInt(int n) => Constant(T.FromInt(n));

    public static Dual<T> operator +(Dual<T> left, Dual<T> right) =>
        new(left.Primal + right.Primal, left.Tangent + right.Tangent);

    public static Dual<T> operator -(Dual<T> left, Dual<T> right) =>
        new(left.Primal - right.Primal, left.Tangent - right.Tangent);

    public static Dual<T> operator -(Dual<T> value) =>
        new(-value.Primal, -value.Tangent);

    public static Dual<T> operator *(Dual<T> left, Dual<T> right) =>
        new(
            left.Primal * right.Primal,
            left.Primal * right.Tangent + left.Tangent * right.Primal);

    public static Dual<T> operator /(Dual<T> left, Dual<T> right) =>
        left * Invert(right);

    public static Dual<T> Invert(Dual<T> value)
    {
        if (value.Primal.Equals(T.AdditiveIdentity))
            return AdditiveIdentity;

        var inv = T.Invert(value.Primal);
        return new(inv, -(value.Tangent * inv * inv));
    }

    public bool IsZero => Primal.Equals(T.AdditiveIdentity) && Tangent.Equals(T.AdditiveIdentity);

    public bool Equals(Dual<T> other) =>
        Primal.Equals(other.Primal) && Tangent.Equals(other.Tangent);

    public override bool Equals(object? obj) => obj is Dual<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Primal, Tangent);

    public static bool operator ==(Dual<T> left, Dual<T> right) => left.Equals(right);

    public static bool operator !=(Dual<T> left, Dual<T> right) => !left.Equals(right);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        var primal = Primal is IFormattable pf ? pf.ToString(format, provider) : Primal.ToString();
        var tangent = Tangent is IFormattable tf ? tf.ToString(format, provider) : Tangent.ToString();
        return $"{primal} + {tangent}ε";
    }
}
