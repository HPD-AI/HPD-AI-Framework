using HPD.Math.Core;

namespace HPD.Math.Algebra;

/// <summary>
/// Small-degree irreducibility checks over finite fields.
/// </summary>
public static class PolynomialIrreducibilityKernels
{
    public static AlgebraStatus TryEvaluate<TCoefficient, TCoefficientOps>(
        SparsePolynomialView<TCoefficient> value,
        in TCoefficient point,
        ref TCoefficient destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        var status = value.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        destination = coefficientOps.Zero;
        for (var i = 0; i < value.TermCount; i++)
        {
            var power = coefficientOps.One;
            status = TryPow(point, value.DegreeAt(i), ref power, coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;

            var term = coefficientOps.Zero;
            coefficientOps.Mul(ref term, value.CoefficientAt(i), power);
            coefficientOps.Add(ref destination, destination, term);
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryIsIrreducibleOverFiniteField<TCoefficient, TCoefficientOps, TEnumerationOps>(
        SparsePolynomialView<TCoefficient> value,
        out bool isIrreducible,
        TCoefficientOps coefficientOps,
        TEnumerationOps enumerationOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
        where TEnumerationOps : struct, IFiniteEnumerationOps<TCoefficient>
    {
        isIrreducible = false;
        var status = value.ValidateCanonical(coefficientOps);
        if (status != AlgebraStatus.Ok)
            return status;

        if (value.Degree <= 0)
            return AlgebraStatus.Ok;

        if (value.Degree == 1)
        {
            isIrreducible = true;
            return AlgebraStatus.Ok;
        }

        if (value.Degree > 3)
            return AlgebraStatus.InvalidInput;

        for (var i = 0; i < enumerationOps.Cardinality; i++)
        {
            status = enumerationOps.TryGetElement(i, out var point);
            if (status != AlgebraStatus.Ok)
                return status;

            var evaluated = coefficientOps.Zero;
            status = TryEvaluate(value, point, ref evaluated, coefficientOps);
            if (status != AlgebraStatus.Ok)
                return status;

            if (!coefficientOps.Eq(evaluated, coefficientOps.Zero))
                continue;

            isIrreducible = false;
            return AlgebraStatus.Ok;
        }

        isIrreducible = true;
        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus TryPow<TCoefficient, TCoefficientOps>(
        in TCoefficient value,
        int exponent,
        ref TCoefficient destination,
        TCoefficientOps coefficientOps)
        where TCoefficientOps : struct, IFieldOps<TCoefficient>
    {
        if (exponent < 0)
            return AlgebraStatus.InvalidInput;

        destination = coefficientOps.One;
        for (var i = 0; i < exponent; i++)
            coefficientOps.Mul(ref destination, destination, value);

        return AlgebraStatus.Ok;
    }
}
