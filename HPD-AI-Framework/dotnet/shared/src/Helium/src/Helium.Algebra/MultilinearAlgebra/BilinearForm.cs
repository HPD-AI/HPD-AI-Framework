using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Bilinear form B : M × M → R, represented by a Gram matrix for finite-dimensional modules.
/// B(x, y) = x^T * G * y where G is the Gram matrix.
/// </summary>
public readonly struct BilinearForm<R> : IEquatable<BilinearForm<R>>
    where R : ICommRing<R>
{
    private readonly Matrix<R> _gramMatrix;

    public int Dimension => _gramMatrix.Rows;

    private BilinearForm(Matrix<R> gramMatrix) => _gramMatrix = gramMatrix;

    public static BilinearForm<R> FromGramMatrix(Matrix<R> gramMatrix) => new(gramMatrix);

    /// <summary>
    /// Standard dot product: Gram matrix = Identity.
    /// </summary>
    public static BilinearForm<R> Standard(int n) => new(Matrix<R>.Identity(n));

    // --- Evaluate ---

    /// <summary>
    /// B(x, y) = sum_{i,j} x[i] * G[i,j] * y[j].
    /// </summary>
    public R Apply(Vector<R> x, Vector<R> y)
    {
        var sum = R.AdditiveIdentity;
        for (int i = 0; i < Dimension; i++)
            for (int j = 0; j < Dimension; j++)
                sum = sum + x[i] * _gramMatrix[i, j] * y[j];
        return sum;
    }

    /// <summary>
    /// Associated quadratic form: Q(x) = B(x, x).
    /// </summary>
    public R Quadratic(Vector<R> x) => Apply(x, x);

    public Matrix<R> GramMatrix => _gramMatrix;

    // --- Properties ---

    public bool IsSymmetric
    {
        get
        {
            for (int i = 0; i < Dimension; i++)
                for (int j = i + 1; j < Dimension; j++)
                    if (!_gramMatrix[i, j].Equals(_gramMatrix[j, i]))
                        return false;
            return true;
        }
    }

    // --- Equality ---

    public bool Equals(BilinearForm<R> other) => _gramMatrix.Equals(other._gramMatrix);
    public override bool Equals(object? obj) => obj is BilinearForm<R> other && Equals(other);
    public override int GetHashCode() => _gramMatrix.GetHashCode();
    public static bool operator ==(BilinearForm<R> left, BilinearForm<R> right) => left.Equals(right);
    public static bool operator !=(BilinearForm<R> left, BilinearForm<R> right) => !left.Equals(right);
}
