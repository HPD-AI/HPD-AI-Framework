using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Tensor product element: formal R-linear combination of elementary tensors m ⊗ n.
/// For finite-dimensional modules with known bases, reduces to Kronecker product.
/// </summary>
public readonly struct TensorProduct<R> : IEquatable<TensorProduct<R>>
    where R : ICommRing<R>
{
    // Internal: list of (coefficient, left-index, right-index) triples.
    // For R^m ⊗ R^n, an element is a linear combination of e_i ⊗ e_j.
    // Stored as a flat coefficient matrix (m x n) via Finsupp.
    private readonly Finsupp<(int Left, int Right), R> _data;

    public int LeftDim { get; }
    public int RightDim { get; }

    private TensorProduct(int leftDim, int rightDim, Finsupp<(int, int), R> data)
    {
        LeftDim = leftDim;
        RightDim = rightDim;
        _data = data;
    }

    public static TensorProduct<R> Zero(int leftDim, int rightDim) =>
        new(leftDim, rightDim, Finsupp<(int, int), R>.Empty);

    /// <summary>
    /// Elementary tensor: e_i ⊗ e_j with coefficient r.
    /// </summary>
    public static TensorProduct<R> Elementary(int leftDim, int rightDim, int i, int j, R coefficient) =>
        new(leftDim, rightDim, Finsupp<(int, int), R>.Single((i, j), coefficient));

    /// <summary>
    /// Tensor product of two basis vectors with coefficient 1.
    /// </summary>
    public static TensorProduct<R> Of(int leftDim, int rightDim, int i, int j) =>
        Elementary(leftDim, rightDim, i, j, R.MultiplicativeIdentity);

    public R this[int i, int j] => _data[(i, j)];

    public bool IsZero => _data.IsZero;

    // --- Arithmetic (R-module structure) ---

    public static TensorProduct<R> operator +(TensorProduct<R> left, TensorProduct<R> right) =>
        new(left.LeftDim, left.RightDim, left._data + right._data);

    public static TensorProduct<R> operator -(TensorProduct<R> left, TensorProduct<R> right) =>
        new(left.LeftDim, left.RightDim, left._data - right._data);

    public static TensorProduct<R> operator -(TensorProduct<R> t) =>
        new(t.LeftDim, t.RightDim, -t._data);

    public static TensorProduct<R> operator *(R scalar, TensorProduct<R> t) =>
        new(t.LeftDim, t.RightDim, t._data.ScalarMultiply(scalar));

    // --- Equality ---

    public bool Equals(TensorProduct<R> other) => _data == other._data;
    public override bool Equals(object? obj) => obj is TensorProduct<R> other && Equals(other);
    public override int GetHashCode() => _data.GetHashCode();
    public static bool operator ==(TensorProduct<R> left, TensorProduct<R> right) => left.Equals(right);
    public static bool operator !=(TensorProduct<R> left, TensorProduct<R> right) => !left.Equals(right);
}
