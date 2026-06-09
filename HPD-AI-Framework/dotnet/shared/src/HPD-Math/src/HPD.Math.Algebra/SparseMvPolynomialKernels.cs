using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Allocation-free kernels over sparse multivariate polynomial views.
/// </summary>
public static class SparseMvPolynomialKernels
{
    public static AlgebraStatus ValidateCanonical<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> value,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        var status = value.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        for (var i = 0; i < value.TermCount; i++)
        {
            if (coefficientOps.Eq(value.CoefficientAt(i), coefficientOps.Zero))
                return AlgebraStatus.InvalidInput;

            var monomial = value.MonomialAt(i);
            for (var j = 0; j < monomial.Length; j++)
            {
                if (monomial[j] < 0)
                    return AlgebraStatus.InvalidInput;
            }

            if (i > 0 && monomialOrder.Compare(value.MonomialAt(i - 1), monomial) != Ordering.Less)
                return AlgebraStatus.InvalidInput;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryTerm<TCoefficient, TCoefficientOps, TMonomialOrder>(
        ReadOnlySpan<int> monomial,
        in TCoefficient coefficient,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        destination.Clear();
        return destination.TryAppendTerm(monomial, coefficient, coefficientOps, monomialOrder);
    }

    public static AlgebraStatus TryConstant<TCoefficient, TCoefficientOps, TMonomialOrder>(
        in TCoefficient coefficient,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        destination.Clear();
        return destination.TryAppendConstant(coefficient, coefficientOps, monomialOrder);
    }

    public static AlgebraStatus TryVariable<TCoefficient, TCoefficientOps, TMonomialOrder>(
        int variableIndex,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        destination.Clear();
        return destination.TryAppendVariable(variableIndex, coefficientOps.One, coefficientOps, monomialOrder);
    }

    public static AlgebraStatus TryAdd<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> left,
        SparseMvPolynomialView<TCoefficient> right,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        var status = ValidateSameContext(left, right, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        var i = 0;
        var j = 0;

        while (i < left.TermCount && j < right.TermCount)
        {
            var compare = monomialOrder.Compare(left.MonomialAt(i), right.MonomialAt(j));
            if (compare == Ordering.Less)
            {
                status = destination.TryAppendTerm(left.MonomialAt(i), left.CoefficientAt(i), coefficientOps, monomialOrder);
                i++;
            }
            else if (compare == Ordering.Greater)
            {
                status = destination.TryAppendTerm(right.MonomialAt(j), right.CoefficientAt(j), coefficientOps, monomialOrder);
                j++;
            }
            else
            {
                var sum = coefficientOps.Zero;
                coefficientOps.Add(ref sum, left.CoefficientAt(i), right.CoefficientAt(j));
                status = destination.TryAppendTerm(left.MonomialAt(i), sum, coefficientOps, monomialOrder);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        while (i < left.TermCount)
        {
            status = destination.TryAppendTerm(left.MonomialAt(i), left.CoefficientAt(i), coefficientOps, monomialOrder);
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.TermCount)
        {
            status = destination.TryAppendTerm(right.MonomialAt(j), right.CoefficientAt(j), coefficientOps, monomialOrder);
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TrySub<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> left,
        SparseMvPolynomialView<TCoefficient> right,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        var status = ValidateSameContext(left, right, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        var i = 0;
        var j = 0;

        while (i < left.TermCount && j < right.TermCount)
        {
            var compare = monomialOrder.Compare(left.MonomialAt(i), right.MonomialAt(j));
            if (compare == Ordering.Less)
            {
                status = destination.TryAppendTerm(left.MonomialAt(i), left.CoefficientAt(i), coefficientOps, monomialOrder);
                i++;
            }
            else if (compare == Ordering.Greater)
            {
                var negated = coefficientOps.Zero;
                coefficientOps.Neg(ref negated, right.CoefficientAt(j));
                status = destination.TryAppendTerm(right.MonomialAt(j), negated, coefficientOps, monomialOrder);
                j++;
            }
            else
            {
                var difference = coefficientOps.Zero;
                coefficientOps.Sub(ref difference, left.CoefficientAt(i), right.CoefficientAt(j));
                status = destination.TryAppendTerm(left.MonomialAt(i), difference, coefficientOps, monomialOrder);
                i++;
                j++;
            }

            if (status != AlgebraStatus.Ok)
                return status;
        }

        while (i < left.TermCount)
        {
            status = destination.TryAppendTerm(left.MonomialAt(i), left.CoefficientAt(i), coefficientOps, monomialOrder);
            if (status != AlgebraStatus.Ok)
                return status;
            i++;
        }

        while (j < right.TermCount)
        {
            var negated = coefficientOps.Zero;
            coefficientOps.Neg(ref negated, right.CoefficientAt(j));
            status = destination.TryAppendTerm(right.MonomialAt(j), negated, coefficientOps, monomialOrder);
            if (status != AlgebraStatus.Ok)
                return status;
            j++;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryNeg<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> value,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        var status = ValidateCanonical(value, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        for (var i = 0; i < value.TermCount; i++)
        {
            var negated = coefficientOps.Zero;
            coefficientOps.Neg(ref negated, value.CoefficientAt(i));
            status = destination.TryAppendTerm(value.MonomialAt(i), negated, coefficientOps, monomialOrder);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryScale<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> value,
        in TCoefficient scalar,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        var status = ValidateCanonical(value, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        if (coefficientOps.Eq(scalar, coefficientOps.Zero))
            return AlgebraStatus.Ok;

        for (var i = 0; i < value.TermCount; i++)
        {
            var product = coefficientOps.Zero;
            coefficientOps.Mul(ref product, scalar, value.CoefficientAt(i));
            status = destination.TryAppendTerm(value.MonomialAt(i), product, coefficientOps, monomialOrder);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMul<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> left,
        SparseMvPolynomialView<TCoefficient> right,
        ref SparseMvPolynomialBuilder<TCoefficient> destination,
        Span<int> workspaceExponents,
        Span<TCoefficient> workspaceCoefficients,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        var status = ValidateSameContext(left, right, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;

        destination.Clear();
        if (left.IsZero || right.IsZero)
            return AlgebraStatus.Ok;

        var maxProductsLong = (long)left.TermCount * right.TermCount;
        if (maxProductsLong > int.MaxValue)
            return AlgebraStatus.Overflow;

        var maxProducts = (int)maxProductsLong;
        var requiredExponentCountLong = maxProductsLong * left.VariableCount;
        if (requiredExponentCountLong > int.MaxValue)
            return AlgebraStatus.Overflow;
        if (workspaceExponents.Length < (int)requiredExponentCountLong || workspaceCoefficients.Length < maxProducts)
            return AlgebraStatus.InsufficientWorkspace;

        var workspaceCount = 0;
        for (var i = 0; i < left.TermCount; i++)
        {
            for (var j = 0; j < right.TermCount; j++)
            {
                var target = workspaceExponents.Slice(workspaceCount * left.VariableCount, left.VariableCount);
                status = MonomialKernels.TryMul(left.MonomialAt(i), right.MonomialAt(j), target);
                if (status != AlgebraStatus.Ok)
                    return status;

                var product = coefficientOps.Zero;
                coefficientOps.Mul(ref product, left.CoefficientAt(i), right.CoefficientAt(j));
                Accumulate(target, product, workspaceExponents, workspaceCoefficients, ref workspaceCount, left.VariableCount, coefficientOps, monomialOrder);
            }
        }

        SortTerms(workspaceExponents, workspaceCoefficients, workspaceCount, left.VariableCount, monomialOrder);

        for (var i = 0; i < workspaceCount; i++)
        {
            status = destination.TryAppendTerm(
                workspaceExponents.Slice(i * left.VariableCount, left.VariableCount),
                workspaceCoefficients[i],
                coefficientOps,
                monomialOrder);
            if (status != AlgebraStatus.Ok)
                return status;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryLeadingTerm<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> value,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder,
        out int supportIndex)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        supportIndex = -1;
        var status = ValidateCanonical(value, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;

        if (value.IsZero)
            return AlgebraStatus.Ok;

        supportIndex = value.TermCount - 1;
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryEvaluate<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> value,
        ReadOnlySpan<TCoefficient> point,
        ref TCoefficient destination,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        var status = ValidateCanonical(value, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;
        if (point.Length != value.VariableCount)
            return AlgebraStatus.DimensionMismatch;

        destination = coefficientOps.Zero;
        for (var termIndex = 0; termIndex < value.TermCount; termIndex++)
        {
            var termValue = value.CoefficientAt(termIndex);
            var monomial = value.MonomialAt(termIndex);
            for (var variableIndex = 0; variableIndex < value.VariableCount; variableIndex++)
            {
                status = TryPow(point[variableIndex], monomial[variableIndex], ref termValue, coefficientOps);
                if (status != AlgebraStatus.Ok)
                    return status;
            }

            var sum = coefficientOps.Zero;
            coefficientOps.Add(ref sum, destination, termValue);
            destination = sum;
        }

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus ValidateSameContext<TCoefficient, TCoefficientOps, TMonomialOrder>(
        SparseMvPolynomialView<TCoefficient> left,
        SparseMvPolynomialView<TCoefficient> right,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        if (left.VariableCount != right.VariableCount)
            return AlgebraStatus.DimensionMismatch;

        var status = ValidateCanonical(left, coefficientOps, monomialOrder);
        if (status != AlgebraStatus.Ok)
            return status;

        return ValidateCanonical(right, coefficientOps, monomialOrder);
    }

    private static void Accumulate<TCoefficient, TCoefficientOps, TMonomialOrder>(
        ReadOnlySpan<int> monomial,
        in TCoefficient coefficient,
        Span<int> workspaceExponents,
        Span<TCoefficient> workspaceCoefficients,
        ref int workspaceCount,
        int variableCount,
        TCoefficientOps coefficientOps,
        TMonomialOrder monomialOrder)
        where TCoefficientOps : struct, IAdditiveCommutativeMonoidOps<TCoefficient>
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        if (coefficientOps.Eq(coefficient, coefficientOps.Zero))
            return;

        for (var i = 0; i < workspaceCount; i++)
        {
            var existing = workspaceExponents.Slice(i * variableCount, variableCount);
            if (monomialOrder.Compare(existing, monomial) != Ordering.Equal)
                continue;

            var sum = coefficientOps.Zero;
            coefficientOps.Add(ref sum, workspaceCoefficients[i], coefficient);
            workspaceCoefficients[i] = sum;
            return;
        }

        monomial.CopyTo(workspaceExponents.Slice(workspaceCount * variableCount, variableCount));
        workspaceCoefficients[workspaceCount] = coefficient;
        workspaceCount++;
    }

    private static AlgebraStatus TryPow<TCoefficient, TCoefficientOps>(
        in TCoefficient value,
        int exponent,
        ref TCoefficient accumulator,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IRingOps<TCoefficient>
    {
        if (exponent < 0)
            return AlgebraStatus.InvalidInput;
        if (exponent == 0)
            return AlgebraStatus.Ok;

        var power = coefficientOps.One;
        for (var i = 0; i < exponent; i++)
        {
            var next = coefficientOps.Zero;
            coefficientOps.Mul(ref next, power, value);
            power = next;
        }

        var product = coefficientOps.Zero;
        coefficientOps.Mul(ref product, accumulator, power);
        accumulator = product;
        return AlgebraStatus.Ok;
    }

    private static void SortTerms<TCoefficient, TMonomialOrder>(
        Span<int> exponents,
        Span<TCoefficient> coefficients,
        int count,
        int variableCount,
        TMonomialOrder monomialOrder)
        where TMonomialOrder : struct, IMonomialOrderOps
    {
        for (var i = 1; i < count; i++)
        {
            var j = i;
            while (j > 0 &&
                   monomialOrder.Compare(
                       exponents.Slice((j - 1) * variableCount, variableCount),
                       exponents.Slice(j * variableCount, variableCount)) == Ordering.Greater)
            {
                SwapTerms(exponents, coefficients, j - 1, j, variableCount);
                j--;
            }
        }
    }

    private static void SwapTerms<TCoefficient>(
        Span<int> exponents,
        Span<TCoefficient> coefficients,
        int leftIndex,
        int rightIndex,
        int variableCount)
    {
        for (var i = 0; i < variableCount; i++)
        {
            var leftOffset = (leftIndex * variableCount) + i;
            var rightOffset = (rightIndex * variableCount) + i;
            (exponents[leftOffset], exponents[rightOffset]) = (exponents[rightOffset], exponents[leftOffset]);
        }

        (coefficients[leftIndex], coefficients[rightIndex]) = (coefficients[rightIndex], coefficients[leftIndex]);
    }
}
