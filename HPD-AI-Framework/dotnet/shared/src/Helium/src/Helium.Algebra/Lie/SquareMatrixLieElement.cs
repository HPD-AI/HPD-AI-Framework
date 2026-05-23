using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Fixed-dimension square matrix Lie algebra element with bracket [X,Y] = XY - YX.
/// </summary>
public readonly struct SquareMatrixLieElement<R, D> :
    ILieAlgebra<R, SquareMatrixLieElement<R, D>>,
    IEquatable<SquareMatrixLieElement<R, D>>,
    IFormattable
    where R : ICommRing<R>
    where D : IFiniteDimension
{
    public Matrix<R> Value { get; }

    private SquareMatrixLieElement(Matrix<R> value)
    {
        if (value.Rows != D.Value || value.Cols != D.Value)
            throw new ArgumentException("Matrix dimensions must match the static dimension witness.", nameof(value));

        Value = value;
    }

    static SquareMatrixLieElement<R, D> System.Numerics.IAdditiveIdentity<SquareMatrixLieElement<R, D>, SquareMatrixLieElement<R, D>>.AdditiveIdentity =>
        Zero;

    public int Dimension => D.Value;

    public static SquareMatrixLieElement<R, D> Zero => new(Matrix<R>.Zero(D.Value, D.Value));

    public static SquareMatrixLieElement<R, D> Create(Matrix<R> value) => new(value);

    public static SquareMatrixLieElement<R, D> operator +(SquareMatrixLieElement<R, D> left, SquareMatrixLieElement<R, D> right) =>
        new(left.Value + right.Value);

    public static SquareMatrixLieElement<R, D> operator -(SquareMatrixLieElement<R, D> left, SquareMatrixLieElement<R, D> right) =>
        new(left.Value - right.Value);

    public static SquareMatrixLieElement<R, D> operator -(SquareMatrixLieElement<R, D> value) =>
        new(-value.Value);

    public static SquareMatrixLieElement<R, D> operator *(R scalar, SquareMatrixLieElement<R, D> value) =>
        new(scalar * value.Value);

    public static SquareMatrixLieElement<R, D> ScalarMultiply(R scalar, SquareMatrixLieElement<R, D> element) =>
        scalar * element;

    public static SquareMatrixLieElement<R, D> Bracket(SquareMatrixLieElement<R, D> left, SquareMatrixLieElement<R, D> right) =>
        new(left.Value * right.Value - right.Value * left.Value);

    public static bool DecidableEquals(SquareMatrixLieElement<R, D> left, SquareMatrixLieElement<R, D> right) =>
        left == right;

    public bool Equals(SquareMatrixLieElement<R, D> other) => Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is SquareMatrixLieElement<R, D> other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(SquareMatrixLieElement<R, D> left, SquareMatrixLieElement<R, D> right) => left.Equals(right);
    public static bool operator !=(SquareMatrixLieElement<R, D> left, SquareMatrixLieElement<R, D> right) => !left.Equals(right);
    public override string ToString() => Value.ToString();
    public string ToString(string? format, IFormatProvider? provider) => Value.ToString(format, provider);
}
