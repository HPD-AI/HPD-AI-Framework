using Helium.Primitives;

namespace Helium.Algebra;

internal readonly struct TensorProductKey<A, B> :
    IEquatable<TensorProductKey<A, B>>,
    IDecidableEq<TensorProductKey<A, B>>,
    ITotalOrder<TensorProductKey<A, B>>
    where A : ITotalOrder<A>
    where B : ITotalOrder<B>
{
    public A Left { get; }
    public B Right { get; }

    public TensorProductKey(A left, B right)
    {
        Left = left;
        Right = right;
    }

    public static bool DecidableEquals(TensorProductKey<A, B> left, TensorProductKey<A, B> right) =>
        left == right;

    public static bool LessEqual(TensorProductKey<A, B> left, TensorProductKey<A, B> right) =>
        CompareOrder(left, right) != Ordering.Greater;

    public static Ordering CompareOrder(TensorProductKey<A, B> left, TensorProductKey<A, B> right)
    {
        var leftCompare = A.CompareOrder(left.Left, right.Left);
        return leftCompare != Ordering.Equal
            ? leftCompare
            : B.CompareOrder(left.Right, right.Right);
    }

    public bool Equals(TensorProductKey<A, B> other) =>
        A.DecidableEquals(Left, other.Left) && B.DecidableEquals(Right, other.Right);

    public override bool Equals(object? obj) => obj is TensorProductKey<A, B> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Left, Right);
    public static bool operator ==(TensorProductKey<A, B> left, TensorProductKey<A, B> right) => left.Equals(right);
    public static bool operator !=(TensorProductKey<A, B> left, TensorProductKey<A, B> right) => !left.Equals(right);
}

/// <summary>
/// Finite R-linear combinations of elementary tensors over explicit ordered basis keys.
/// This represents the free-module tensor basis R[A] ⊗ R[B] ≅ R[A × B].
/// </summary>
public readonly struct TensorProduct<R, A, B> :
    IModule<R, TensorProduct<R, A, B>>,
    IEquatable<TensorProduct<R, A, B>>,
    IFormattable
    where R : ICommRing<R>
    where A : ITotalOrder<A>
    where B : ITotalOrder<B>
{
    private readonly Finsupp<TensorProductKey<A, B>, R> _data;

    private TensorProduct(Finsupp<TensorProductKey<A, B>, R> data)
    {
        _data = data;
    }

    static TensorProduct<R, A, B> System.Numerics.IAdditiveIdentity<TensorProduct<R, A, B>, TensorProduct<R, A, B>>.AdditiveIdentity => Zero;

    public static TensorProduct<R, A, B> Zero => new(Finsupp<TensorProductKey<A, B>, R>.Empty);

    public bool IsZero => _data.IsZero;

    public IEnumerable<(A Left, B Right, R Coefficient)> Components
    {
        get
        {
            var data = _data;
            foreach (var key in data.Support)
                yield return (key.Left, key.Right, data[key]);
        }
    }

    public static TensorProduct<R, A, B> Elementary(A left, B right, R coefficient) =>
        new(Finsupp<TensorProductKey<A, B>, R>.Single(new TensorProductKey<A, B>(left, right), coefficient));

    public R Coefficient(A left, B right) =>
        _data[new TensorProductKey<A, B>(left, right)];

    public static TensorProduct<R, A, B> operator +(TensorProduct<R, A, B> left, TensorProduct<R, A, B> right) =>
        new(left._data + right._data);

    public static TensorProduct<R, A, B> operator -(TensorProduct<R, A, B> left, TensorProduct<R, A, B> right) =>
        new(left._data - right._data);

    public static TensorProduct<R, A, B> operator -(TensorProduct<R, A, B> value) =>
        new(-value._data);

    public static TensorProduct<R, A, B> operator *(R scalar, TensorProduct<R, A, B> value) =>
        new(value._data.ScalarMultiply(scalar));

    public static TensorProduct<R, A, B> ScalarMultiply(R scalar, TensorProduct<R, A, B> element) =>
        scalar * element;

    public static bool DecidableEquals(TensorProduct<R, A, B> left, TensorProduct<R, A, B> right) =>
        left == right;

    public bool Equals(TensorProduct<R, A, B> other) => _data == other._data;
    public override bool Equals(object? obj) => obj is TensorProduct<R, A, B> other && Equals(other);
    public override int GetHashCode() => _data.GetHashCode();
    public static bool operator ==(TensorProduct<R, A, B> left, TensorProduct<R, A, B> right) => left.Equals(right);
    public static bool operator !=(TensorProduct<R, A, B> left, TensorProduct<R, A, B> right) => !left.Equals(right);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero)
            return "0";

        return string.Join(" + ", Components.Select(c => $"{FormatHelpers.FormatElement(c.Coefficient, format, provider)}*({c.Left} tensor {c.Right})"));
    }
}
