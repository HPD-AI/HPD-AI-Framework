using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Linear map between finite-dimensional vector spaces, backed by a matrix.
/// f(a + b) == f(a) + f(b), f(r * a) == r * f(a).
/// </summary>
public readonly struct LinearMap<R> : IEquatable<LinearMap<R>>
    where R : IRing<R>
{
    private readonly Matrix<R> _matrix;

    public int DomainDim => _matrix.Cols;
    public int CodomainDim => _matrix.Rows;

    private LinearMap(Matrix<R> matrix) => _matrix = matrix;

    // --- Construction ---

    public static LinearMap<R> FromMatrix(Matrix<R> matrix) => new(matrix);

    public static LinearMap<R> Identity(int n) => new(Matrix<R>.Identity(n));

    public static LinearMap<R> Zero(int domainDim, int codomainDim) =>
        new(Matrix<R>.Zero(codomainDim, domainDim));

    // --- Apply ---

    public Vector<R> Apply(Vector<R> input) => _matrix * input;

    // --- Operations ---

    public LinearMap<R> Compose(LinearMap<R> other) => new(_matrix * other._matrix);

    public Matrix<R> ToMatrix() => _matrix;

    public static LinearMap<R> operator +(LinearMap<R> left, LinearMap<R> right) =>
        new(left._matrix + right._matrix);

    public static LinearMap<R> operator -(LinearMap<R> left, LinearMap<R> right) =>
        new(left._matrix - right._matrix);

    public static LinearMap<R> operator *(R scalar, LinearMap<R> map) =>
        new(scalar * map._matrix);

    // --- Equality ---

    public bool Equals(LinearMap<R> other) => _matrix.Equals(other._matrix);
    public override bool Equals(object? obj) => obj is LinearMap<R> other && Equals(other);
    public override int GetHashCode() => _matrix.GetHashCode();
    public static bool operator ==(LinearMap<R> left, LinearMap<R> right) => left.Equals(right);
    public static bool operator !=(LinearMap<R> left, LinearMap<R> right) => !left.Equals(right);
}
