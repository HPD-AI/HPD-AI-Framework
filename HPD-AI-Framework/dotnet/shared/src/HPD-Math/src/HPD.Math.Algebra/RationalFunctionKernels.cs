using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Allocation-free kernels over univariate rational-function views.
/// </summary>
public static class RationalFunctionKernels
{
    public static AlgebraStatus Validate<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> value,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var status = value.Numerator.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = value.Denominator.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return value.Denominator.IsZero
            ? AlgebraStatus.DivisionByZero
            : AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryFromPolynomial<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> polynomial,
        ref RationalFunctionBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = polynomial.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        for (var i = 0; i < polynomial.TermCount; i++)
        {
            status = destination.Numerator.TryAppendTerm(polynomial.DegreeAt(i), polynomial.CoefficientAt(i), coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return SparsePolynomialKernels.TryMonomial(0, coefficientOps.One, ref destination.Denominator, coefficientOps);
    }

    public static AlgebraStatus TryNeg<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> value,
        ref RationalFunctionBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = Validate(value, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        status = SparsePolynomialKernels.TryNeg(value.Numerator, ref destination.Numerator, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return Copy(value.Denominator, ref destination.Denominator, coefficientOps);
    }

    public static AlgebraStatus TryAdd<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> left,
        RationalFunctionView<TCoefficient> right,
        ref RationalFunctionBuilder<TCoefficient> destination,
        scoped RationalFunctionArithmeticWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = ValidateSame(left, right, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var leftProduct = new SparsePolynomialBuilder<TCoefficient>(
            workspace.LeftProductDegrees,
            workspace.LeftProductCoefficients);
        var rightProduct = new SparsePolynomialBuilder<TCoefficient>(
            workspace.RightProductDegrees,
            workspace.RightProductCoefficients);

        status = SparsePolynomialKernels.TryMul(
            left.Numerator,
            right.Denominator,
            ref leftProduct,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = SparsePolynomialKernels.TryMul(
            right.Numerator,
            left.Denominator,
            ref rightProduct,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        status = SparsePolynomialKernels.TryAdd(leftProduct.AsView(), rightProduct.AsView(), ref destination.Numerator, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return SparsePolynomialKernels.TryMul(
            left.Denominator,
            right.Denominator,
            ref destination.Denominator,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
    }

    public static AlgebraStatus TrySub<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> left,
        RationalFunctionView<TCoefficient> right,
        ref RationalFunctionBuilder<TCoefficient> destination,
        scoped RationalFunctionArithmeticWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = ValidateSame(left, right, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var leftProduct = new SparsePolynomialBuilder<TCoefficient>(
            workspace.LeftProductDegrees,
            workspace.LeftProductCoefficients);
        var rightProduct = new SparsePolynomialBuilder<TCoefficient>(
            workspace.RightProductDegrees,
            workspace.RightProductCoefficients);

        status = SparsePolynomialKernels.TryMul(
            left.Numerator,
            right.Denominator,
            ref leftProduct,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = SparsePolynomialKernels.TryMul(
            right.Numerator,
            left.Denominator,
            ref rightProduct,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        status = SparsePolynomialKernels.TrySub(leftProduct.AsView(), rightProduct.AsView(), ref destination.Numerator, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return SparsePolynomialKernels.TryMul(
            left.Denominator,
            right.Denominator,
            ref destination.Denominator,
            workspace.MultiplyWorkspaceDegrees,
            workspace.MultiplyWorkspaceCoefficients,
            coefficientOps);
    }

    public static AlgebraStatus TryMul<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> left,
        RationalFunctionView<TCoefficient> right,
        ref RationalFunctionBuilder<TCoefficient> destination,
        scoped Span<int> workspaceDegrees,
        scoped Span<TCoefficient> workspaceCoefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = ValidateSame(left, right, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        status = SparsePolynomialKernels.TryMul(left.Numerator, right.Numerator, ref destination.Numerator, workspaceDegrees, workspaceCoefficients, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return SparsePolynomialKernels.TryMul(left.Denominator, right.Denominator, ref destination.Denominator, workspaceDegrees, workspaceCoefficients, coefficientOps);
    }

    public static AlgebraStatus TryDiv<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> left,
        RationalFunctionView<TCoefficient> right,
        ref RationalFunctionBuilder<TCoefficient> destination,
        scoped Span<int> workspaceDegrees,
        scoped Span<TCoefficient> workspaceCoefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = ValidateSame(left, right, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;
        if (right.Numerator.IsZero)
            return AlgebraStatus.DivisionByZero;

        destination.Clear();
        status = SparsePolynomialKernels.TryMul(left.Numerator, right.Denominator, ref destination.Numerator, workspaceDegrees, workspaceCoefficients, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return SparsePolynomialKernels.TryMul(left.Denominator, right.Numerator, ref destination.Denominator, workspaceDegrees, workspaceCoefficients, coefficientOps);
    }

    public static AlgebraStatus TryNormalize<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> value,
        ref RationalFunctionBuilder<TCoefficient> destination,
        scoped RationalFunctionNormalizationWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = Validate(value, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        if (value.Numerator.IsZero)
        {
            status = SparsePolynomialKernels.TryMonomial(0, coefficientOps.One, ref destination.Denominator, coefficientOps);
            return status;
        }

        var gcd = new SparsePolynomialBuilder<TCoefficient>(workspace.GcdDegrees, workspace.GcdCoefficients);
        status = SparsePolynomialKernels.TryGcd(
            value.Numerator,
            value.Denominator,
            ref gcd,
            workspace.GcdLeftWorkspace,
            workspace.GcdRightWorkspace,
            workspace.GcdRemainderWorkspace,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        if (gcd.AsView().IsZero)
            return AlgebraStatus.InvalidInput;

        status = TryDivExact(
            value.Numerator,
            gcd.AsView(),
            ref destination.Numerator,
            workspace.QuotientWorkspace,
            workspace.RemainderWorkspace,
            coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return TryDivExact(
            value.Denominator,
            gcd.AsView(),
            ref destination.Denominator,
            workspace.QuotientWorkspace,
            workspace.RemainderWorkspace,
            coefficientOps);
    }

    private static AlgebraStatus ValidateSame<TCoefficient, TCoefficientOps>(
        RationalFunctionView<TCoefficient> left,
        RationalFunctionView<TCoefficient> right,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var status = Validate(left, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return Validate(right, coefficientOps);
    }

    private static AlgebraStatus Copy<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> source,
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

    private static AlgebraStatus TryDivExact<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> dividend,
        scoped SparsePolynomialView<TCoefficient> divisor,
        scoped ref SparsePolynomialBuilder<TCoefficient> quotient,
        scoped Span<TCoefficient> quotientWorkspace,
        scoped Span<TCoefficient> remainderWorkspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        quotient.Clear();
        if (divisor.IsZero)
            return AlgebraStatus.DivisionByZero;
        if (dividend.IsZero)
            return AlgebraStatus.Ok;

        var quotientLength = dividend.Degree >= divisor.Degree
            ? dividend.Degree - divisor.Degree + 1
            : 0;
        var remainderLength = dividend.Degree + 1;
        if (quotientWorkspace.Length < quotientLength || remainderWorkspace.Length < remainderLength)
            return AlgebraStatus.InsufficientWorkspace;

        Clear(quotientWorkspace[..quotientLength], coefficientOps);
        Clear(remainderWorkspace[..remainderLength], coefficientOps);
        CopySparseToDense(dividend, remainderWorkspace[..remainderLength]);

        var divisorLcInverse = coefficientOps.Zero;
        var status = coefficientOps.TryInvert(ref divisorLcInverse, divisor.CoefficientAt(divisor.TermCount - 1));
        if (status != AlgebraStatus.Ok)
            return status;

        var remainderDegree = dividend.Degree;
        var divisorDegree = divisor.Degree;
        while (remainderDegree >= divisorDegree)
        {
            if (coefficientOps.Eq(remainderWorkspace[remainderDegree], coefficientOps.Zero))
            {
                remainderDegree--;
                continue;
            }

            var quotientTermDegree = remainderDegree - divisorDegree;
            var quotientCoefficient = coefficientOps.Zero;
            coefficientOps.Mul(ref quotientCoefficient, remainderWorkspace[remainderDegree], divisorLcInverse);
            quotientWorkspace[quotientTermDegree] = quotientCoefficient;

            for (var i = 0; i < divisor.TermCount; i++)
            {
                var targetDegree = quotientTermDegree + divisor.DegreeAt(i);
                var product = coefficientOps.Zero;
                coefficientOps.Mul(ref product, quotientCoefficient, divisor.CoefficientAt(i));
                var difference = coefficientOps.Zero;
                coefficientOps.Sub(ref difference, remainderWorkspace[targetDegree], product);
                remainderWorkspace[targetDegree] = difference;
            }

            while (remainderDegree >= 0 && coefficientOps.Eq(remainderWorkspace[remainderDegree], coefficientOps.Zero))
                remainderDegree--;
        }

        for (var i = 0; i <= remainderDegree; i++)
            if (!coefficientOps.Eq(remainderWorkspace[i], coefficientOps.Zero))
                return AlgebraStatus.InvalidInput;

        return EmitDense(quotientWorkspace[..quotientLength], ref quotient, coefficientOps);
    }

    private static void Clear<TCoefficient, TCoefficientOps>(
        Span<TCoefficient> coefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        for (var i = 0; i < coefficients.Length; i++)
            coefficients[i] = coefficientOps.Zero;
    }

    private static void CopySparseToDense<TCoefficient>(
        scoped SparsePolynomialView<TCoefficient> source,
        Span<TCoefficient> destination)
    {
        for (var i = 0; i < source.TermCount; i++)
            destination[source.DegreeAt(i)] = source.CoefficientAt(i);
    }

    private static AlgebraStatus EmitDense<TCoefficient, TCoefficientOps>(
        scoped ReadOnlySpan<TCoefficient> coefficients,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        destination.Clear();
        for (var degree = coefficients.Length - 1; degree >= 0; degree--)
        {
            if (coefficientOps.Eq(coefficients[degree], coefficientOps.Zero))
                continue;

            for (var i = 0; i <= degree; i++)
            {
                var status = destination.TryAppendTerm(i, coefficients[i], coefficientOps);
                if (status != AlgebraStatus.Ok)
                    return status;
            }

            break;
        }

        return AlgebraStatus.Ok;
    }
}
