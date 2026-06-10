namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned builder for polynomial quotient representatives.
/// </summary>
public ref struct PolynomialQuotientBuilder<TCoefficient>
{
    public SparsePolynomialBuilder<TCoefficient> Representative;

    public PolynomialQuotientBuilder(Span<int> degrees, Span<TCoefficient> coefficients)
    {
        Representative = new SparsePolynomialBuilder<TCoefficient>(degrees, coefficients);
    }

    public void Clear() => Representative.Clear();

    public PolynomialQuotientView<TCoefficient> AsView() =>
        new(Representative.AsView());
}
