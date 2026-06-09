namespace HPD.Math.Algebra;

/// <summary>
/// Caller-owned workspace for reducing a polynomial modulo a quotient modulus.
/// </summary>
public readonly ref struct PolynomialQuotientReductionWorkspace<TCoefficient>
{
    public PolynomialQuotientReductionWorkspace(
        Span<int> quotientDegrees,
        Span<TCoefficient> quotientCoefficients,
        Span<int> remainderDegrees,
        Span<TCoefficient> remainderCoefficients,
        Span<TCoefficient> quotientWorkspace,
        Span<TCoefficient> remainderWorkspace)
    {
        QuotientDegrees = quotientDegrees;
        QuotientCoefficients = quotientCoefficients;
        RemainderDegrees = remainderDegrees;
        RemainderCoefficients = remainderCoefficients;
        QuotientWorkspace = quotientWorkspace;
        RemainderWorkspace = remainderWorkspace;
    }

    public Span<int> QuotientDegrees { get; }
    public Span<TCoefficient> QuotientCoefficients { get; }
    public Span<int> RemainderDegrees { get; }
    public Span<TCoefficient> RemainderCoefficients { get; }
    public Span<TCoefficient> QuotientWorkspace { get; }
    public Span<TCoefficient> RemainderWorkspace { get; }
}

/// <summary>
/// Caller-owned workspace for quotient-ring binary operations.
/// </summary>
public readonly ref struct PolynomialQuotientArithmeticWorkspace<TCoefficient>
{
    public PolynomialQuotientArithmeticWorkspace(
        Span<int> intermediateDegrees,
        Span<TCoefficient> intermediateCoefficients,
        Span<int> multiplyWorkspaceDegrees,
        Span<TCoefficient> multiplyWorkspaceCoefficients,
        PolynomialQuotientReductionWorkspace<TCoefficient> reduction)
    {
        IntermediateDegrees = intermediateDegrees;
        IntermediateCoefficients = intermediateCoefficients;
        MultiplyWorkspaceDegrees = multiplyWorkspaceDegrees;
        MultiplyWorkspaceCoefficients = multiplyWorkspaceCoefficients;
        Reduction = reduction;
    }

    public Span<int> IntermediateDegrees { get; }
    public Span<TCoefficient> IntermediateCoefficients { get; }
    public Span<int> MultiplyWorkspaceDegrees { get; }
    public Span<TCoefficient> MultiplyWorkspaceCoefficients { get; }
    public PolynomialQuotientReductionWorkspace<TCoefficient> Reduction { get; }
}

/// <summary>
/// Caller-owned workspace for quotient-ring inversion.
/// </summary>
public readonly ref struct PolynomialQuotientInversionWorkspace<TCoefficient>
{
    public PolynomialQuotientInversionWorkspace(
        Span<int> gcdDegrees,
        Span<TCoefficient> gcdCoefficients,
        Span<int> bezoutValueDegrees,
        Span<TCoefficient> bezoutValueCoefficients,
        Span<int> bezoutModulusDegrees,
        Span<TCoefficient> bezoutModulusCoefficients,
        SparsePolynomialEuclideanWorkspace<TCoefficient> euclidean,
        PolynomialQuotientReductionWorkspace<TCoefficient> reduction)
    {
        GcdDegrees = gcdDegrees;
        GcdCoefficients = gcdCoefficients;
        BezoutValueDegrees = bezoutValueDegrees;
        BezoutValueCoefficients = bezoutValueCoefficients;
        BezoutModulusDegrees = bezoutModulusDegrees;
        BezoutModulusCoefficients = bezoutModulusCoefficients;
        Euclidean = euclidean;
        Reduction = reduction;
    }

    public Span<int> GcdDegrees { get; }
    public Span<TCoefficient> GcdCoefficients { get; }
    public Span<int> BezoutValueDegrees { get; }
    public Span<TCoefficient> BezoutValueCoefficients { get; }
    public Span<int> BezoutModulusDegrees { get; }
    public Span<TCoefficient> BezoutModulusCoefficients { get; }
    public SparsePolynomialEuclideanWorkspace<TCoefficient> Euclidean { get; }
    public PolynomialQuotientReductionWorkspace<TCoefficient> Reduction { get; }
}
