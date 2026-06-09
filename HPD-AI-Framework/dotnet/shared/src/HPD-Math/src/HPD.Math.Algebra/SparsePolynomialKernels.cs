using HPD.Math.Core;
using HPD.Math.Finite;

namespace HPD.Math.Algebra;

/// <summary>
/// Allocation-free sparse polynomial kernels.
/// </summary>
public static class SparsePolynomialKernels
{
    public static AlgebraStatus TryMonomial<TCoefficient, TCoefficientOps>(
        int degree,
        in TCoefficient coefficient,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        destination.Clear();
        return destination.TryAppendTerm(degree, coefficient, coefficientOps);
    }

    public static AlgebraStatus TryAdd<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> left,
        scoped SparsePolynomialView<TCoefficient> right,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        var status = left.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();

        var i = 0;
        var j = 0;
        while (i < left.TermCount && j < right.TermCount)
        {
            var leftDegree = left.DegreeAt(i);
            var rightDegree = right.DegreeAt(j);

            if (leftDegree < rightDegree)
            {
                status = destination.TryAppendTerm(leftDegree, left.CoefficientAt(i), coefficientOps);
                i++;
            }
            else if (leftDegree > rightDegree)
            {
                status = destination.TryAppendTerm(rightDegree, right.CoefficientAt(j), coefficientOps);
                j++;
            }
            else
            {
                var sum = coefficientOps.Zero;
                coefficientOps.Add(ref sum, left.CoefficientAt(i), right.CoefficientAt(j));
                status = destination.TryAppendTerm(leftDegree, sum, coefficientOps);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        while (i < left.TermCount)
        {
            status = destination.TryAppendTerm(left.DegreeAt(i), left.CoefficientAt(i), coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.TermCount)
        {
            status = destination.TryAppendTerm(right.DegreeAt(j), right.CoefficientAt(j), coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TrySub<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> left,
        scoped SparsePolynomialView<TCoefficient> right,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = left.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();

        var i = 0;
        var j = 0;
        while (i < left.TermCount && j < right.TermCount)
        {
            var leftDegree = left.DegreeAt(i);
            var rightDegree = right.DegreeAt(j);

            if (leftDegree < rightDegree)
            {
                status = destination.TryAppendTerm(leftDegree, left.CoefficientAt(i), coefficientOps);
                i++;
            }
            else if (leftDegree > rightDegree)
            {
                var negated = coefficientOps.Zero;
                coefficientOps.Neg(ref negated, right.CoefficientAt(j));
                status = destination.TryAppendTerm(rightDegree, negated, coefficientOps);
                j++;
            }
            else
            {
                var difference = coefficientOps.Zero;
                coefficientOps.Sub(ref difference, left.CoefficientAt(i), right.CoefficientAt(j));
                status = destination.TryAppendTerm(leftDegree, difference, coefficientOps);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        while (i < left.TermCount)
        {
            status = destination.TryAppendTerm(left.DegreeAt(i), left.CoefficientAt(i), coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.TermCount)
        {
            var negated = coefficientOps.Zero;
            coefficientOps.Neg(ref negated, right.CoefficientAt(j));
            status = destination.TryAppendTerm(right.DegreeAt(j), negated, coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryNeg<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> value,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = value.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        for (var i = 0; i < value.TermCount; i++)
        {
            var negated = coefficientOps.Zero;
            coefficientOps.Neg(ref negated, value.CoefficientAt(i));
            status = destination.TryAppendTerm(value.DegreeAt(i), negated, coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryScale<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> value,
        in TCoefficient scalar,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = value.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        if (coefficientOps.Eq(scalar, coefficientOps.Zero))
            return AlgebraStatus.Ok;

        for (var i = 0; i < value.TermCount; i++)
        {
            var product = coefficientOps.Zero;
            coefficientOps.Mul(ref product, scalar, value.CoefficientAt(i));
            status = destination.TryAppendTerm(value.DegreeAt(i), product, coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMul<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> left,
        scoped SparsePolynomialView<TCoefficient> right,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        scoped Span<int> workspaceDegrees,
        scoped Span<TCoefficient> workspaceCoefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        var status = left.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();

        if (left.IsZero || right.IsZero)
            return AlgebraStatus.Ok;

        var maxProducts = checked(left.TermCount * right.TermCount);
        if (workspaceDegrees.Length < maxProducts || workspaceCoefficients.Length < maxProducts)
            return AlgebraStatus.InsufficientWorkspace;

        var workspaceCount = 0;
        for (var i = 0; i < left.TermCount; i++)
        {
            for (var j = 0; j < right.TermCount; j++)
            {
                var degree = checked(left.DegreeAt(i) + right.DegreeAt(j));
                var product = coefficientOps.Zero;
                coefficientOps.Mul(ref product, left.CoefficientAt(i), right.CoefficientAt(j));
                Accumulate(degree, product, workspaceDegrees, workspaceCoefficients, ref workspaceCount, coefficientOps);
            }
        }

        SortByDegree(workspaceDegrees[..workspaceCount], workspaceCoefficients[..workspaceCount]);

        for (var i = 0; i < workspaceCount; i++)
        {
            status = destination.TryAppendTerm(workspaceDegrees[i], workspaceCoefficients[i], coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryDivMod<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> dividend,
        scoped SparsePolynomialView<TCoefficient> divisor,
        ref SparsePolynomialBuilder<TCoefficient> quotient,
        ref SparsePolynomialBuilder<TCoefficient> remainder,
        Span<TCoefficient> quotientWorkspace,
        Span<TCoefficient> remainderWorkspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = dividend.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = divisor.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        quotient.Clear();
        remainder.Clear();

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
        CopySparseToDense(dividend, remainderWorkspace[..remainderLength], coefficientOps);

        status = DenseDivMod(
            remainderWorkspace[..remainderLength],
            dividend.Degree,
            divisor,
            quotientWorkspace[..quotientLength],
            coefficientOps,
            out var quotientDegree,
            out var remainderDegree);
        if (status != AlgebraStatus.Ok)
            return status;

        status = EmitDense(quotientWorkspace[..(quotientDegree + 1)], ref quotient, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return EmitDense(remainderWorkspace[..(remainderDegree + 1)], ref remainder, coefficientOps);
    }

    public static AlgebraStatus TryGcd<TCoefficient, TCoefficientOps>(
        scoped SparsePolynomialView<TCoefficient> left,
        scoped SparsePolynomialView<TCoefficient> right,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        scoped Span<TCoefficient> leftWorkspace,
        scoped Span<TCoefficient> rightWorkspace,
        scoped Span<TCoefficient> remainderWorkspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = left.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var length = System.Math.Max(left.Degree, right.Degree) + 1;
        if (length <= 0)
        {
            destination.Clear();
            return AlgebraStatus.Ok;
        }

        if (leftWorkspace.Length < length || rightWorkspace.Length < length || remainderWorkspace.Length < length)
            return AlgebraStatus.InsufficientWorkspace;

        var a = leftWorkspace[..length];
        var b = rightWorkspace[..length];
        var r = remainderWorkspace[..length];
        Clear(a, coefficientOps);
        Clear(b, coefficientOps);
        Clear(r, coefficientOps);
        CopySparseToDense(left, a, coefficientOps);
        CopySparseToDense(right, b, coefficientOps);

        var degreeA = DenseDegree(a, coefficientOps);
        var degreeB = DenseDegree(b, coefficientOps);
        while (degreeB >= 0)
        {
            status = DenseRemainder(a, degreeA, b, degreeB, r, coefficientOps, out var degreeR);
            if (status != AlgebraStatus.Ok)
                return status;

            var temp = a;
            a = b;
            degreeA = degreeB;
            b = r;
            degreeB = degreeR;
            r = temp;
        }

        return EmitMonicDense(a[..(degreeA + 1)], ref destination, coefficientOps);
    }

    public static AlgebraStatus TryExtendedGcd<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> left,
        SparsePolynomialView<TCoefficient> right,
        scoped ref SparsePolynomialBuilder<TCoefficient> gcd,
        scoped ref SparsePolynomialBuilder<TCoefficient> leftBezout,
        scoped ref SparsePolynomialBuilder<TCoefficient> rightBezout,
        SparsePolynomialEuclideanWorkspace<TCoefficient> workspace,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = left.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        var length = System.Math.Max(left.Degree, right.Degree) + 1;
        if (length <= 0)
        {
            gcd.Clear();
            leftBezout.Clear();
            rightBezout.Clear();
            return AlgebraStatus.Ok;
        }

        if (!workspace.HasCapacity(length))
            return AlgebraStatus.InsufficientWorkspace;

        var oldR = workspace.OldR[..length];
        var r = workspace.R[..length];
        var rem = workspace.Remainder[..length];
        var q = workspace.Quotient[..length];
        var oldU = workspace.OldU[..length];
        var u = workspace.U[..length];
        var nextU = workspace.NextU[..length];
        var oldV = workspace.OldV[..length];
        var v = workspace.V[..length];
        var nextV = workspace.NextV[..length];
        var product = workspace.Product[..length];

        Clear(oldR, coefficientOps);
        Clear(r, coefficientOps);
        Clear(rem, coefficientOps);
        Clear(q, coefficientOps);
        Clear(oldU, coefficientOps);
        Clear(u, coefficientOps);
        Clear(nextU, coefficientOps);
        Clear(oldV, coefficientOps);
        Clear(v, coefficientOps);
        Clear(nextV, coefficientOps);
        Clear(product, coefficientOps);

        CopySparseToDense(left, oldR, coefficientOps);
        CopySparseToDense(right, r, coefficientOps);
        oldU[0] = coefficientOps.One;
        v[0] = coefficientOps.One;

        var degreeOldR = DenseDegree(oldR, coefficientOps);
        var degreeR = DenseDegree(r, coefficientOps);
        var degreeOldU = 0;
        var degreeU = -1;
        var degreeOldV = -1;
        var degreeV = 0;

        while (degreeR >= 0)
        {
            Clear(q, coefficientOps);
            status = DenseDivModDense(
                oldR,
                degreeOldR,
                r,
                degreeR,
                q,
                rem,
                coefficientOps,
                out var degreeQ,
                out var degreeRem);
            if (status != AlgebraStatus.Ok)
                return status;

            status = DenseSubtractProduct(oldU, degreeOldU, q, degreeQ, u, degreeU, nextU, product, coefficientOps, out var degreeNextU);
            if (status != AlgebraStatus.Ok)
                return status;

            status = DenseSubtractProduct(oldV, degreeOldV, q, degreeQ, v, degreeV, nextV, product, coefficientOps, out var degreeNextV);
            if (status != AlgebraStatus.Ok)
                return status;

            var oldRTemp = oldR;
            oldR = r;
            r = rem;
            rem = oldRTemp;
            degreeOldR = degreeR;
            degreeR = degreeRem;

            var oldUTemp = oldU;
            oldU = u;
            u = nextU;
            nextU = oldUTemp;
            degreeOldU = degreeU;
            degreeU = degreeNextU;

            var oldVTemp = oldV;
            oldV = v;
            v = nextV;
            nextV = oldVTemp;
            degreeOldV = degreeV;
            degreeV = degreeNextV;
        }

        if (degreeOldR < 0)
        {
            gcd.Clear();
            leftBezout.Clear();
            rightBezout.Clear();
            return AlgebraStatus.Ok;
        }

        var lcInverse = coefficientOps.Zero;
        status = coefficientOps.TryInvert(ref lcInverse, oldR[degreeOldR]);
        if (status != AlgebraStatus.Ok)
            return status;

        ScaleDense(oldR, degreeOldR, lcInverse, coefficientOps);
        ScaleDense(oldU, degreeOldU, lcInverse, coefficientOps);
        ScaleDense(oldV, degreeOldV, lcInverse, coefficientOps);

        status = EmitDense(oldR[..(degreeOldR + 1)], ref gcd, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        status = EmitDense(oldU[..(System.Math.Max(degreeOldU, -1) + 1)], ref leftBezout, coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        return EmitDense(oldV[..(System.Math.Max(degreeOldV, -1) + 1)], ref rightBezout, coefficientOps);
    }

    private static void Clear<TCoefficient, TCoefficientOps>(
        Span<TCoefficient> coefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        for (var i = 0; i < coefficients.Length; i++)
            coefficients[i] = coefficientOps.Zero;
    }

    private static void CopySparseToDense<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> source,
        Span<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        Clear(destination, coefficientOps);
        for (var i = 0; i < source.TermCount; i++)
            destination[source.DegreeAt(i)] = source.CoefficientAt(i);
    }

    private static int DenseDegree<TCoefficient, TCoefficientOps>(
        scoped ReadOnlySpan<TCoefficient> coefficients,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        for (var i = coefficients.Length - 1; i >= 0; i--)
            if (!coefficientOps.Eq(coefficients[i], coefficientOps.Zero))
                return i;

        return -1;
    }

    private static AlgebraStatus DenseDivMod<TCoefficient, TCoefficientOps>(
        Span<TCoefficient> remainderCoefficients,
        int dividendDegree,
        SparsePolynomialView<TCoefficient> divisor,
        Span<TCoefficient> quotientCoefficients,
        TCoefficientOps coefficientOps,
        out int quotientDegree,
        out int remainderDegree)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
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
            coefficientOps.Mul(ref quotientCoefficient, remainderCoefficients[remainderDegree], divisorLcInverse);
            quotientCoefficients[quotientTermDegree] = quotientCoefficient;
            if (quotientTermDegree > quotientDegree)
                quotientDegree = quotientTermDegree;

            for (var i = 0; i < divisor.TermCount; i++)
            {
                var targetDegree = quotientTermDegree + divisor.DegreeAt(i);
                var product = coefficientOps.Zero;
                coefficientOps.Mul(ref product, quotientCoefficient, divisor.CoefficientAt(i));
                var difference = coefficientOps.Zero;
                coefficientOps.Sub(ref difference, remainderCoefficients[targetDegree], product);
                remainderCoefficients[targetDegree] = difference;
            }

            while (remainderDegree >= 0 && coefficientOps.Eq(remainderCoefficients[remainderDegree], coefficientOps.Zero))
                remainderDegree--;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus DenseRemainder<TCoefficient, TCoefficientOps>(
        ReadOnlySpan<TCoefficient> dividend,
        int dividendDegree,
        ReadOnlySpan<TCoefficient> divisor,
        int divisorDegree,
        Span<TCoefficient> remainder,
        TCoefficientOps coefficientOps,
        out int remainderDegree)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        remainderDegree = -1;
        if (divisorDegree < 0)
            return AlgebraStatus.DivisionByZero;

        Clear(remainder, coefficientOps);
        for (var i = 0; i <= dividendDegree; i++)
            remainder[i] = dividend[i];

        var divisorLcInverse = coefficientOps.Zero;
        var status = coefficientOps.TryInvert(ref divisorLcInverse, divisor[divisorDegree]);
        if (status != AlgebraStatus.Ok)
            return status;

        remainderDegree = dividendDegree;
        while (remainderDegree >= divisorDegree)
        {
            if (coefficientOps.Eq(remainder[remainderDegree], coefficientOps.Zero))
            {
                remainderDegree--;
                continue;
            }

            var quotientCoefficient = coefficientOps.Zero;
            coefficientOps.Mul(ref quotientCoefficient, remainder[remainderDegree], divisorLcInverse);
            var quotientTermDegree = remainderDegree - divisorDegree;

            for (var i = 0; i <= divisorDegree; i++)
            {
                if (coefficientOps.Eq(divisor[i], coefficientOps.Zero))
                    continue;

                var targetDegree = quotientTermDegree + i;
                var product = coefficientOps.Zero;
                coefficientOps.Mul(ref product, quotientCoefficient, divisor[i]);
                var difference = coefficientOps.Zero;
                coefficientOps.Sub(ref difference, remainder[targetDegree], product);
                remainder[targetDegree] = difference;
            }

            while (remainderDegree >= 0 && coefficientOps.Eq(remainder[remainderDegree], coefficientOps.Zero))
                remainderDegree--;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus DenseDivModDense<TCoefficient, TCoefficientOps>(
        ReadOnlySpan<TCoefficient> dividend,
        int dividendDegree,
        ReadOnlySpan<TCoefficient> divisor,
        int divisorDegree,
        Span<TCoefficient> quotient,
        Span<TCoefficient> remainder,
        TCoefficientOps coefficientOps,
        out int quotientDegree,
        out int remainderDegree)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        quotientDegree = -1;
        Clear(quotient, coefficientOps);
        var status = DenseRemainderWithQuotient(
            dividend,
            dividendDegree,
            divisor,
            divisorDegree,
            quotient,
            remainder,
            coefficientOps,
            out quotientDegree,
            out remainderDegree);
        return status;
    }

    private static AlgebraStatus DenseRemainderWithQuotient<TCoefficient, TCoefficientOps>(
        ReadOnlySpan<TCoefficient> dividend,
        int dividendDegree,
        ReadOnlySpan<TCoefficient> divisor,
        int divisorDegree,
        Span<TCoefficient> quotient,
        Span<TCoefficient> remainder,
        TCoefficientOps coefficientOps,
        out int quotientDegree,
        out int remainderDegree)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        quotientDegree = -1;
        remainderDegree = -1;
        if (divisorDegree < 0)
            return AlgebraStatus.DivisionByZero;

        Clear(remainder, coefficientOps);
        for (var i = 0; i <= dividendDegree; i++)
            remainder[i] = dividend[i];

        var divisorLcInverse = coefficientOps.Zero;
        var status = coefficientOps.TryInvert(ref divisorLcInverse, divisor[divisorDegree]);
        if (status != AlgebraStatus.Ok)
            return status;

        remainderDegree = dividendDegree;
        while (remainderDegree >= divisorDegree)
        {
            if (coefficientOps.Eq(remainder[remainderDegree], coefficientOps.Zero))
            {
                remainderDegree--;
                continue;
            }

            var quotientCoefficient = coefficientOps.Zero;
            coefficientOps.Mul(ref quotientCoefficient, remainder[remainderDegree], divisorLcInverse);
            var quotientTermDegree = remainderDegree - divisorDegree;
            quotient[quotientTermDegree] = quotientCoefficient;
            if (quotientTermDegree > quotientDegree)
                quotientDegree = quotientTermDegree;

            for (var i = 0; i <= divisorDegree; i++)
            {
                if (coefficientOps.Eq(divisor[i], coefficientOps.Zero))
                    continue;

                var targetDegree = quotientTermDegree + i;
                var product = coefficientOps.Zero;
                coefficientOps.Mul(ref product, quotientCoefficient, divisor[i]);
                var difference = coefficientOps.Zero;
                coefficientOps.Sub(ref difference, remainder[targetDegree], product);
                remainder[targetDegree] = difference;
            }

            while (remainderDegree >= 0 && coefficientOps.Eq(remainder[remainderDegree], coefficientOps.Zero))
                remainderDegree--;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus DenseSubtractProduct<TCoefficient, TCoefficientOps>(
        ReadOnlySpan<TCoefficient> left,
        int leftDegree,
        ReadOnlySpan<TCoefficient> multiplier,
        int multiplierDegree,
        ReadOnlySpan<TCoefficient> value,
        int valueDegree,
        Span<TCoefficient> destination,
        Span<TCoefficient> product,
        TCoefficientOps coefficientOps,
        out int destinationDegree)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        destinationDegree = -1;
        Clear(destination, coefficientOps);
        Clear(product, coefficientOps);

        if (multiplierDegree >= 0 && valueDegree >= 0)
        {
            if (multiplierDegree + valueDegree >= product.Length)
                return AlgebraStatus.InsufficientWorkspace;

            for (var i = 0; i <= multiplierDegree; i++)
            {
                if (coefficientOps.Eq(multiplier[i], coefficientOps.Zero))
                    continue;

                for (var j = 0; j <= valueDegree; j++)
                {
                    if (coefficientOps.Eq(value[j], coefficientOps.Zero))
                        continue;

                    var term = coefficientOps.Zero;
                    coefficientOps.Mul(ref term, multiplier[i], value[j]);
                    var sum = coefficientOps.Zero;
                    coefficientOps.Add(ref sum, product[i + j], term);
                    product[i + j] = sum;
                }
            }
        }

        var maxDegree = System.Math.Max(leftDegree, DenseDegree(product, coefficientOps));
        if (maxDegree >= destination.Length)
            return AlgebraStatus.InsufficientWorkspace;

        for (var i = 0; i <= maxDegree; i++)
        {
            var leftCoefficient = i <= leftDegree ? left[i] : coefficientOps.Zero;
            var productCoefficient = product[i];
            var difference = coefficientOps.Zero;
            coefficientOps.Sub(ref difference, leftCoefficient, productCoefficient);
            destination[i] = difference;
        }

        destinationDegree = DenseDegree(destination, coefficientOps);
        return AlgebraStatus.Ok;
    }

    private static void ScaleDense<TCoefficient, TCoefficientOps>(
        Span<TCoefficient> coefficients,
        int degree,
        in TCoefficient scalar,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        for (var i = 0; i <= degree; i++)
        {
            var product = coefficientOps.Zero;
            coefficientOps.Mul(ref product, coefficients[i], scalar);
            coefficients[i] = product;
        }
    }

    private static AlgebraStatus EmitMonicDense<TCoefficient, TCoefficientOps>(
        scoped ReadOnlySpan<TCoefficient> coefficients,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var degree = DenseDegree(coefficients, coefficientOps);
        if (degree < 0)
        {
            destination.Clear();
            return AlgebraStatus.Ok;
        }

        var inverse = coefficientOps.Zero;
        var status = coefficientOps.TryInvert(ref inverse, coefficients[degree]);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        for (var i = 0; i <= degree; i++)
        {
            var scaled = coefficientOps.Zero;
            coefficientOps.Mul(ref scaled, coefficients[i], inverse);
            status = destination.TryAppendTerm(i, scaled, coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus EmitDense<TCoefficient, TCoefficientOps>(
        scoped ReadOnlySpan<TCoefficient> coefficients,
        scoped ref SparsePolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        destination.Clear();
        var degree = DenseDegree(coefficients, coefficientOps);
        for (var i = 0; i <= degree; i++)
        {
            var status = destination.TryAppendTerm(i, coefficients[i], coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    private static void Accumulate<TCoefficient, TCoefficientOps>(
        int degree,
        in TCoefficient value,
        Span<int> workspaceDegrees,
        Span<TCoefficient> workspaceCoefficients,
        ref int workspaceCount,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
    {
        for (var i = 0; i < workspaceCount; i++)
        {
            if (workspaceDegrees[i] != degree)
                continue;

            var sum = coefficientOps.Zero;
            coefficientOps.Add(ref sum, workspaceCoefficients[i], value);
            workspaceCoefficients[i] = sum;
            return;
        }

        workspaceDegrees[workspaceCount] = degree;
        workspaceCoefficients[workspaceCount] = value;
        workspaceCount++;
    }

    private static void SortByDegree<TCoefficient>(Span<int> degrees, Span<TCoefficient> coefficients)
    {
        for (var i = 1; i < degrees.Length; i++)
        {
            var degree = degrees[i];
            var coefficient = coefficients[i];
            var j = i - 1;

            while (j >= 0 && degrees[j] > degree)
            {
                degrees[j + 1] = degrees[j];
                coefficients[j + 1] = coefficients[j];
                j--;
            }

            degrees[j + 1] = degree;
            coefficients[j + 1] = coefficient;
        }
    }
}

public static class SparsePolynomialKernelExtensions
{
    extension<TCoefficient>(SparsePolynomialView<TCoefficient> self)
    {
        public AlgebraStatus TryAdd<TCoefficientOps>(
            SparsePolynomialView<TCoefficient> other,
            ref SparsePolynomialBuilder<TCoefficient> destination,
            TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        {
            return SparsePolynomialKernels.TryAdd(self, other, ref destination, coefficientOps);
        }

        public AlgebraStatus TrySub<TCoefficientOps>(
            SparsePolynomialView<TCoefficient> other,
            ref SparsePolynomialBuilder<TCoefficient> destination,
            TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IRingOps<TCoefficient>
        {
            return SparsePolynomialKernels.TrySub(self, other, ref destination, coefficientOps);
        }

        public AlgebraStatus TryNeg<TCoefficientOps>(
            ref SparsePolynomialBuilder<TCoefficient> destination,
            TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IRingOps<TCoefficient>
        {
            return SparsePolynomialKernels.TryNeg(self, ref destination, coefficientOps);
        }

        public AlgebraStatus TryScale<TCoefficientOps>(
            in TCoefficient scalar,
            ref SparsePolynomialBuilder<TCoefficient> destination,
            TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IRingOps<TCoefficient>
        {
            return SparsePolynomialKernels.TryScale(self, scalar, ref destination, coefficientOps);
        }

        public AlgebraStatus TryMul<TCoefficientOps>(
            SparsePolynomialView<TCoefficient> other,
            ref SparsePolynomialBuilder<TCoefficient> destination,
            Span<int> workspaceDegrees,
            Span<TCoefficient> workspaceCoefficients,
            TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IRingOps<TCoefficient>
        {
            return SparsePolynomialKernels.TryMul(
                self,
                other,
                ref destination,
                workspaceDegrees,
                workspaceCoefficients,
                coefficientOps);
        }

        public AlgebraStatus TryDivMod<TCoefficientOps>(
            SparsePolynomialView<TCoefficient> divisor,
            ref SparsePolynomialBuilder<TCoefficient> quotient,
            ref SparsePolynomialBuilder<TCoefficient> remainder,
            Span<TCoefficient> quotientWorkspace,
            Span<TCoefficient> remainderWorkspace,
            TCoefficientOps coefficientOps)
            where TCoefficientOps : struct, IFieldOps<TCoefficient>
        {
            return SparsePolynomialKernels.TryDivMod(
                self,
                divisor,
                ref quotient,
                ref remainder,
                quotientWorkspace,
                remainderWorkspace,
                coefficientOps);
        }
    }
}
