namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned dense coefficient workspace for sparse polynomial Euclidean algorithms.
/// Each span must have capacity at least max(left.Degree, right.Degree) + 1.
/// </summary>
public readonly ref struct SparsePolynomialEuclideanWorkspace<TCoefficient>
{
    public SparsePolynomialEuclideanWorkspace(
        Span<TCoefficient> oldR,
        Span<TCoefficient> r,
        Span<TCoefficient> remainder,
        Span<TCoefficient> quotient,
        Span<TCoefficient> oldU,
        Span<TCoefficient> u,
        Span<TCoefficient> nextU,
        Span<TCoefficient> oldV,
        Span<TCoefficient> v,
        Span<TCoefficient> nextV,
        Span<TCoefficient> product)
    {
        OldR = oldR;
        R = r;
        Remainder = remainder;
        Quotient = quotient;
        OldU = oldU;
        U = u;
        NextU = nextU;
        OldV = oldV;
        V = v;
        NextV = nextV;
        Product = product;
    }

    public Span<TCoefficient> OldR { get; }
    public Span<TCoefficient> R { get; }
    public Span<TCoefficient> Remainder { get; }
    public Span<TCoefficient> Quotient { get; }
    public Span<TCoefficient> OldU { get; }
    public Span<TCoefficient> U { get; }
    public Span<TCoefficient> NextU { get; }
    public Span<TCoefficient> OldV { get; }
    public Span<TCoefficient> V { get; }
    public Span<TCoefficient> NextV { get; }
    public Span<TCoefficient> Product { get; }

    public bool HasCapacity(int length) =>
        OldR.Length >= length &&
        R.Length >= length &&
        Remainder.Length >= length &&
        Quotient.Length >= length &&
        OldU.Length >= length &&
        U.Length >= length &&
        NextU.Length >= length &&
        OldV.Length >= length &&
        V.Length >= length &&
        NextV.Length >= length &&
        Product.Length >= length;
}
