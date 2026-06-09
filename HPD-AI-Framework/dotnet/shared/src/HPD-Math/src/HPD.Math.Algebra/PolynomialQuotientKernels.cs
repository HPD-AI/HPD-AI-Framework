using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Allocation-free kernels for univariate polynomial quotient rings R[x]/(f).
/// </summary>
public static class PolynomialQuotientKernels
{
    public static AlgebraStatus ValidateContext<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> modulus,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var status = modulus.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return modulus.IsZero || modulus.Degree <= 0
            ? AlgebraStatus.InvalidInput
            : AlgebraStatus.Ok;
    }

    public static AlgebraStatus ValidateContextStatus<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> modulus,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusFieldOps<TCoefficient>
    {
        var status = modulus.ValidateCanonicalStatus(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return modulus.IsZero || modulus.Degree <= 0
            ? AlgebraStatus.InvalidInput
            : AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryReduce<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> value,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientReductionWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = value.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = ValidateContext(modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var quotient = new SparsePolynomialBuilder<TCoefficient>(
            workspace.QuotientDegrees,
            workspace.QuotientCoefficients);
        var remainder = new SparsePolynomialBuilder<TCoefficient>(
            workspace.RemainderDegrees,
            workspace.RemainderCoefficients);

        status = SparsePolynomialKernels.TryDivMod(
            value,
            modulus,
            ref quotient,
            ref remainder,
            workspace.QuotientWorkspace,
            workspace.RemainderWorkspace,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        return Copy(remainder.AsView(), ref destination.Representative, coefficientOps);
    }

    public static AlgebraStatus TryAdd<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> left,
        scoped PolynomialQuotientView<TCoefficient> right,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientArithmeticWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = ValidateSame(left, right, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var intermediate = new SparsePolynomialBuilder<TCoefficient>(
            workspace.IntermediateDegrees,
            workspace.IntermediateCoefficients);

        status = SparsePolynomialKernels.TryAdd(left.Representative, right.Representative, ref intermediate, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return TryReduce(intermediate.AsView(), modulus, ref destination, workspace.Reduction, coefficientOps);
    }

    public static AlgebraStatus TryReduceStatus<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> value,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientReductionWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusFieldOps<TCoefficient>
    {
        var status = value.ValidateCanonicalStatus(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = ValidateContextStatus(modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        if (value.IsZero)
            return AlgebraStatus.Ok;

        var quotientLength = value.Degree >= modulus.Degree
            ? value.Degree - modulus.Degree + 1
            : 0;
        var remainderLength = value.Degree + 1;

        if (workspace.QuotientCoefficients.Length < quotientLength ||
            workspace.RemainderCoefficients.Length < remainderLength)
            return AlgebraStatus.InsufficientWorkspace;

        var quotient = workspace.QuotientCoefficients[..quotientLength];
        var remainder = workspace.RemainderCoefficients[..remainderLength];
        ClearStatus(quotient, coefficientOps);
        ClearStatus(remainder, coefficientOps);
        CopySparseToDenseStatus(value, remainder, coefficientOps);

        status = DenseDivModStatus(
            remainder,
            value.Degree,
            modulus,
            quotient,
            coefficientOps,
            out _,
            out var remainderDegree);
        if (status != AlgebraStatus.Ok)
            return status;

        return EmitDenseStatus(remainder[..(remainderDegree + 1)], ref destination.Representative, coefficientOps);
    }

    public static AlgebraStatus TryAddStatus<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> left,
        scoped PolynomialQuotientView<TCoefficient> right,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientArithmeticWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusFieldOps<TCoefficient>
    {
        var status = ValidateSameStatus(left, right, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var intermediate = new SparsePolynomialBuilder<TCoefficient>(
            workspace.IntermediateDegrees,
            workspace.IntermediateCoefficients);

        status = StatusSparsePolynomialKernels.TryAdd(left.Representative, right.Representative, ref intermediate, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return TryReduceStatus(intermediate.AsView(), modulus, ref destination, workspace.Reduction, coefficientOps);
    }

    public static AlgebraStatus TryMulStatus<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> left,
        scoped PolynomialQuotientView<TCoefficient> right,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientArithmeticWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusFieldOps<TCoefficient>
    {
        var status = ValidateSameStatus(left, right, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var intermediate = new SparsePolynomialBuilder<TCoefficient>(
            workspace.IntermediateDegrees,
            workspace.IntermediateCoefficients);

        status = StatusSparsePolynomialKernels.TryMul(
            left.Representative,
            right.Representative,
            ref intermediate,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return TryReduceStatus(intermediate.AsView(), modulus, ref destination, workspace.Reduction, coefficientOps);
    }

    public static AlgebraStatus TrySub<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> left,
        scoped PolynomialQuotientView<TCoefficient> right,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientArithmeticWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = ValidateSame(left, right, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var intermediate = new SparsePolynomialBuilder<TCoefficient>(
            workspace.IntermediateDegrees,
            workspace.IntermediateCoefficients);

        status = SparsePolynomialKernels.TrySub(left.Representative, right.Representative, ref intermediate, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return TryReduce(intermediate.AsView(), modulus, ref destination, workspace.Reduction, coefficientOps);
    }

    public static AlgebraStatus TryNeg<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> value,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientReductionWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = Validate(value, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var negDegrees = workspace.QuotientDegrees;
        var negCoefficients = workspace.QuotientCoefficients;
        var negated = new SparsePolynomialBuilder<TCoefficient>(negDegrees, negCoefficients);

        status = SparsePolynomialKernels.TryNeg(value.Representative, ref negated, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return TryReduce(negated.AsView(), modulus, ref destination, workspace, coefficientOps);
    }

    public static AlgebraStatus TryMul<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> left,
        scoped PolynomialQuotientView<TCoefficient> right,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientArithmeticWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = ValidateSame(left, right, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var intermediate = new SparsePolynomialBuilder<TCoefficient>(
            workspace.IntermediateDegrees,
            workspace.IntermediateCoefficients);

        status = SparsePolynomialKernels.TryMul(
            left.Representative,
            right.Representative,
            ref intermediate,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return TryReduce(intermediate.AsView(), modulus, ref destination, workspace.Reduction, coefficientOps);
    }

    public static AlgebraStatus TryInvert<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> value,
        scoped SparsePolynomialView<TCoefficient> modulus,
        ref PolynomialQuotientBuilder<TCoefficient> destination,
        scoped PolynomialQuotientInversionWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = Validate(value, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;
        if (value.IsZero)
            return AlgebraStatus.DivisionByZero;

        var gcd = new SparsePolynomialBuilder<TCoefficient>(workspace.GcdDegrees, workspace.GcdCoefficients);
        var bezoutValue = new SparsePolynomialBuilder<TCoefficient>(
            workspace.BezoutValueDegrees,
            workspace.BezoutValueCoefficients);
        var bezoutModulus = new SparsePolynomialBuilder<TCoefficient>(
            workspace.BezoutModulusDegrees,
            workspace.BezoutModulusCoefficients);

        status = SparsePolynomialKernels.TryExtendedGcd(
            value.Representative,
            modulus,
            ref gcd,
            ref bezoutValue,
            ref bezoutModulus,
            workspace.Euclidean,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        if (gcd.AsView().TermCount != 1 ||
            gcd.AsView().DegreeAt(0) != 0 ||
            !coefficientOps.Eq(gcd.AsView().CoefficientAt(0), coefficientOps.One))
            return AlgebraStatus.NonInvertible;

        return TryReduce(bezoutValue.AsView(), modulus, ref destination, workspace.Reduction, coefficientOps);
    }

    private static AlgebraStatus Validate<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> value,
        scoped SparsePolynomialView<TCoefficient> modulus,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = value.Validate(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return ValidateContext(modulus, coefficientOps);
    }

    private static AlgebraStatus ValidateStatus<TCoefficient, TCoefficientOps>(
        PolynomialQuotientView<TCoefficient> value,
        SparsePolynomialView<TCoefficient> modulus,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusFieldOps<TCoefficient>
    {
        var status = value.ValidateStatus(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return ValidateContextStatus(modulus, coefficientOps);
    }

    private static AlgebraStatus ValidateSame<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> left,
        scoped PolynomialQuotientView<TCoefficient> right,
        scoped SparsePolynomialView<TCoefficient> modulus,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = Validate(left, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return right.Validate(coefficientOps);
    }

    private static AlgebraStatus ValidateSameStatus<TCoefficient, TCoefficientOps>(
        scoped PolynomialQuotientView<TCoefficient> left,
        scoped PolynomialQuotientView<TCoefficient> right,
        scoped SparsePolynomialView<TCoefficient> modulus,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusFieldOps<TCoefficient>
    {
        var status = ValidateStatus(left, modulus, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return right.ValidateStatus(coefficientOps);
    }

    private static AlgebraStatus Copy<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> source,
        ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        destination.Clear();
        for (var i = 0; i < source.TermCount; i++)
        {
            var status = destination.TryAppendTerm(source.DegreeAt(i), source.CoefficientAt(i), coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    private static void ClearStatus<TCoefficient, TCoefficientOps>(
        Span<TCoefficient> coefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
    {
        for (var i = 0; i < coefficients.Length; i++)
            coefficients[i] = coefficientOps.Zero;
    }

    private static void CopySparseToDenseStatus<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> source,
        Span<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
    {
        ClearStatus(destination, coefficientOps);
        for (var i = 0; i < source.TermCount; i++)
            destination[source.DegreeAt(i)] = source.CoefficientAt(i);
    }

    private static AlgebraStatus DenseDivModStatus<TCoefficient, TCoefficientOps>(
        Span<TCoefficient> remainderCoefficients,
        int dividendDegree,
        SparsePolynomialView<TCoefficient> divisor,
        Span<TCoefficient> quotientCoefficients,
        TCoefficientOps coefficientOps,
        out int quotientDegree,
        out int remainderDegree)
        where TCoefficientOps : struct, IStatusFieldOps<TCoefficient>
    {
        quotientDegree = -1;
        remainderDegree = dividendDegree;

        var divisorLcInverse = coefficientOps.Zero;
        var status = coefficientOps.TryInvert(ref divisorLcInverse, divisor.CoefficientAt(divisor.TermCount - 1));
        if (status != AlgebraStatus.Ok)
            return status;

        var divisorDegree = divisor.Degree;
        while (remainderDegree >= divisorDegree)
        {
            if (coefficientOps.Eq(remainderCoefficients[remainderDegree], coefficientOps.Zero))
            {
                remainderDegree--;
                continue;
            }

            var quotientTermDegree = remainderDegree - divisorDegree;
            var quotientCoefficient = coefficientOps.Zero;
            status = coefficientOps.TryMul(ref quotientCoefficient, remainderCoefficients[remainderDegree], divisorLcInverse);
            if (status != AlgebraStatus.Ok)
                return status;

            quotientCoefficients[quotientTermDegree] = quotientCoefficient;
            if (quotientTermDegree > quotientDegree)
                quotientDegree = quotientTermDegree;

            for (var i = 0; i < divisor.TermCount; i++)
            {
                var targetDegree = quotientTermDegree + divisor.DegreeAt(i);
                var product = coefficientOps.Zero;
                status = coefficientOps.TryMul(ref product, quotientCoefficient, divisor.CoefficientAt(i));
                if (status != AlgebraStatus.Ok)
                    return status;

                var difference = coefficientOps.Zero;
                status = coefficientOps.TrySub(ref difference, remainderCoefficients[targetDegree], product);
                if (status != AlgebraStatus.Ok)
                    return status;

                remainderCoefficients[targetDegree] = difference;
            }

            while (remainderDegree >= 0 && coefficientOps.Eq(remainderCoefficients[remainderDegree], coefficientOps.Zero))
                remainderDegree--;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus EmitDenseStatus<TCoefficient, TCoefficientOps>(
        scoped ReadOnlySpan<TCoefficient> coefficients,
        ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IStatusRingOps<TCoefficient>
    {
        destination.Clear();
        for (var degree = coefficients.Length - 1; degree >= 0; degree--)
        {
            if (!coefficientOps.Eq(coefficients[degree], coefficientOps.Zero))
            {
                for (var i = 0; i <= degree; i++)
                {
                    var status = destination.TryAppendTermStatus(i, coefficients[i], coefficientOps);
                    if (status != AlgebraStatus.Ok)
                        return status;
                }

                break;
            }
        }

        return AlgebraStatus.Ok;
    }
}
