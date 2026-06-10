namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned workspace for rational-function arithmetic.
/// </summary>
public readonly ref struct RationalFunctionArithmeticWorkspace<TCoefficient>
{
    public RationalFunctionArithmeticWorkspace(
        Span<int> leftProductDegrees,
        Span<TCoefficient> leftProductCoefficients,
        Span<int> rightProductDegrees,
        Span<TCoefficient> rightProductCoefficients,
        Span<int> multiplyWorkspaceDegrees,
        Span<TCoefficient> multiplyWorkspaceCoefficients)
    {
        LeftProductDegrees = leftProductDegrees;
        LeftProductCoefficients = leftProductCoefficients;
        RightProductDegrees = rightProductDegrees;
        RightProductCoefficients = rightProductCoefficients;
        MultiplyWorkspaceDegrees = multiplyWorkspaceDegrees;
        MultiplyWorkspaceCoefficients = multiplyWorkspaceCoefficients;
    }

    public Span<int> LeftProductDegrees { get; }
    public Span<TCoefficient> LeftProductCoefficients { get; }
    public Span<int> RightProductDegrees { get; }
    public Span<TCoefficient> RightProductCoefficients { get; }
    public Span<int> MultiplyWorkspaceDegrees { get; }
    public Span<TCoefficient> MultiplyWorkspaceCoefficients { get; }
}

/// <summary>
/// Caller-owned workspace for GCD-normalizing rational functions.
/// </summary>
public readonly ref struct RationalFunctionNormalizationWorkspace<TCoefficient>
{
    public RationalFunctionNormalizationWorkspace(
        Span<int> gcdDegrees,
        Span<TCoefficient> gcdCoefficients,
        Span<int> numeratorRemainderDegrees,
        Span<TCoefficient> numeratorRemainderCoefficients,
        Span<int> denominatorRemainderDegrees,
        Span<TCoefficient> denominatorRemainderCoefficients,
        Span<TCoefficient> gcdLeftWorkspace,
        Span<TCoefficient> gcdRightWorkspace,
        Span<TCoefficient> gcdRemainderWorkspace,
        Span<TCoefficient> quotientWorkspace,
        Span<TCoefficient> remainderWorkspace)
    {
        GcdDegrees = gcdDegrees;
        GcdCoefficients = gcdCoefficients;
        NumeratorRemainderDegrees = numeratorRemainderDegrees;
        NumeratorRemainderCoefficients = numeratorRemainderCoefficients;
        DenominatorRemainderDegrees = denominatorRemainderDegrees;
        DenominatorRemainderCoefficients = denominatorRemainderCoefficients;
        GcdLeftWorkspace = gcdLeftWorkspace;
        GcdRightWorkspace = gcdRightWorkspace;
        GcdRemainderWorkspace = gcdRemainderWorkspace;
        QuotientWorkspace = quotientWorkspace;
        RemainderWorkspace = remainderWorkspace;
    }

    public Span<int> GcdDegrees { get; }
    public Span<TCoefficient> GcdCoefficients { get; }
    public Span<int> NumeratorRemainderDegrees { get; }
    public Span<TCoefficient> NumeratorRemainderCoefficients { get; }
    public Span<int> DenominatorRemainderDegrees { get; }
    public Span<TCoefficient> DenominatorRemainderCoefficients { get; }
    public Span<TCoefficient> GcdLeftWorkspace { get; }
    public Span<TCoefficient> GcdRightWorkspace { get; }
    public Span<TCoefficient> GcdRemainderWorkspace { get; }
    public Span<TCoefficient> QuotientWorkspace { get; }
    public Span<TCoefficient> RemainderWorkspace { get; }
}
