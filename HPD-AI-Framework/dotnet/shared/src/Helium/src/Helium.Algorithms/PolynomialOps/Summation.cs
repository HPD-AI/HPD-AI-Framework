using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algorithms;

public static class Summation
{
    public static class Gosper
    {
        public static RationalFunction<Rational>? FindRationalAntidifference(
            RationalFunction<Rational> term,
            int maxNumeratorDegree = 8)
        {
            if (maxNumeratorDegree < 0)
                throw new ArgumentOutOfRangeException(nameof(maxNumeratorDegree));

            var reducedTerm = term.Reduce();
            if (reducedTerm.IsZero)
                return RationalFunction<Rational>.Zero;

            var denominator = reducedTerm.Denominator;
            var shiftedDenominator = Shift(denominator, Rational.One);
            var right = reducedTerm.Numerator * shiftedDenominator;

            for (var degree = 0; degree <= maxNumeratorDegree; degree++)
            {
                var solution = SolveNumerator(denominator, shiftedDenominator, right, degree);
                if (solution is null)
                    continue;

                var candidate = RationalFunctionField.Of(solution.Value, denominator);
                if (Shift(candidate, Rational.One) - candidate == reducedTerm)
                    return candidate;
            }

            return null;
        }

        public static RationalFunction<Rational> Shift(RationalFunction<Rational> value, Rational amount) =>
            RationalFunctionField.Of(Shift(value.Numerator, amount), Shift(value.Denominator, amount));

        public static SparsePolynomial<Rational> Shift(SparsePolynomial<Rational> polynomial, Rational amount)
        {
            if (polynomial.IsZero)
                return SparsePolynomial<Rational>.Zero;

            var result = SparsePolynomial<Rational>.Zero;
            for (var degree = 0; degree <= polynomial.Degree; degree++)
            {
                var coefficient = polynomial[degree];
                if (coefficient == Rational.Zero)
                    continue;

                result += SparsePolynomial<Rational>.C(coefficient) * PowXPlusA(degree, amount);
            }

            return result;
        }

        private static SparsePolynomial<Rational>? SolveNumerator(
            SparsePolynomial<Rational> denominator,
            SparsePolynomial<Rational> shiftedDenominator,
            SparsePolynomial<Rational> right,
            int numeratorDegree)
        {
            var variableCount = numeratorDegree + 1;
            var basisImages = new SparsePolynomial<Rational>[variableCount];
            var maxDegree = right.IsZero ? 0 : right.Degree;

            for (var i = 0; i < variableCount; i++)
            {
                var basis = SparsePolynomial<Rational>.Monomial(i, Rational.One);
                var image = Shift(basis, Rational.One) * denominator - basis * shiftedDenominator;
                basisImages[i] = image;
                if (!image.IsZero)
                    maxDegree = Math.Max(maxDegree, image.Degree);
            }

            var equationCount = maxDegree + 1;
            var matrix = new Rational[equationCount, variableCount + 1];
            for (var row = 0; row < equationCount; row++)
            {
                for (var col = 0; col < variableCount; col++)
                    matrix[row, col] = basisImages[col][row];
                matrix[row, variableCount] = right[row];
            }

            var solution = SolveLinearSystem(matrix, equationCount, variableCount);
            if (solution is null)
                return null;

            return SparsePolynomial<Rational>.FromCoeffs(solution);
        }

        private static Rational[]? SolveLinearSystem(Rational[,] matrix, int rows, int variables)
        {
            var pivotColumns = new List<int>();
            var pivotRow = 0;

            for (var col = 0; col < variables && pivotRow < rows; col++)
            {
                var found = -1;
                for (var row = pivotRow; row < rows; row++)
                {
                    if (matrix[row, col] != Rational.Zero)
                    {
                        found = row;
                        break;
                    }
                }

                if (found < 0)
                    continue;

                if (found != pivotRow)
                    SwapRows(matrix, found, pivotRow, variables + 1);

                var pivotInv = Rational.Invert(matrix[pivotRow, col]);
                for (var j = col; j <= variables; j++)
                    matrix[pivotRow, j] *= pivotInv;

                for (var row = 0; row < rows; row++)
                {
                    if (row == pivotRow)
                        continue;

                    var factor = matrix[row, col];
                    if (factor == Rational.Zero)
                        continue;

                    for (var j = col; j <= variables; j++)
                        matrix[row, j] -= factor * matrix[pivotRow, j];
                }

                pivotColumns.Add(col);
                pivotRow++;
            }

            for (var row = pivotRow; row < rows; row++)
            {
                var allZero = true;
                for (var col = 0; col < variables; col++)
                {
                    if (matrix[row, col] != Rational.Zero)
                    {
                        allZero = false;
                        break;
                    }
                }

                if (allZero && matrix[row, variables] != Rational.Zero)
                    return null;
            }

            var solution = new Rational[variables];
            Array.Fill(solution, Rational.Zero);
            for (var row = 0; row < pivotColumns.Count; row++)
                solution[pivotColumns[row]] = matrix[row, variables];
            return solution;
        }

        private static void SwapRows(Rational[,] matrix, int a, int b, int cols)
        {
            for (var col = 0; col < cols; col++)
                (matrix[a, col], matrix[b, col]) = (matrix[b, col], matrix[a, col]);
        }

        private static SparsePolynomial<Rational> PowXPlusA(int exponent, Rational a)
        {
            var result = SparsePolynomial<Rational>.One;
            var factor = SparsePolynomial<Rational>.FromCoeffs(a, Rational.One);
            for (var i = 0; i < exponent; i++)
                result *= factor;
            return result;
        }
    }
}
