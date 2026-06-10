namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned builder for univariate rational functions.
/// </summary>
public ref struct RationalFunctionBuilder<TCoefficient>
{
    public SparsePolynomialBuilder<TCoefficient> Numerator;
    public SparsePolynomialBuilder<TCoefficient> Denominator;

    public RationalFunctionBuilder(
        Span<int> numeratorDegrees,
        Span<TCoefficient> numeratorCoefficients,
        Span<int> denominatorDegrees,
        Span<TCoefficient> denominatorCoefficients)
    {
        Numerator = new SparsePolynomialBuilder<TCoefficient>(numeratorDegrees, numeratorCoefficients);
        Denominator = new SparsePolynomialBuilder<TCoefficient>(denominatorDegrees, denominatorCoefficients);
    }

    public void Clear()
    {
        Numerator.Clear();
        Denominator.Clear();
    }

    public RationalFunctionView<TCoefficient> AsView() =>
        new(Numerator.AsView(), Denominator.AsView());
}
