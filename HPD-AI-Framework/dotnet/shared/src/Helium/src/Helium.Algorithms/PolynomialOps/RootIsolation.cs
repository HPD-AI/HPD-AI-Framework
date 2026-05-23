using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algorithms;

public static class RootIsolation
{
    public static class Sturm
    {
        public static IReadOnlyList<SparsePolynomial<Rational>> Sequence(SparsePolynomial<Rational> polynomial)
        {
            if (polynomial.IsZero)
                throw new ArgumentException("Sturm sequence is undefined for the zero polynomial.", nameof(polynomial));

            var sequence = new List<SparsePolynomial<Rational>>
            {
                polynomial,
                PolynomialCalculus.Derivative(polynomial)
            };

            if (sequence[1].IsZero)
                return sequence;

            while (!sequence[^1].IsZero)
            {
                var (_, remainder) = sequence[^2].DivMod(sequence[^1]);
                if (remainder.IsZero)
                    break;
                sequence.Add(-remainder);
            }

            return sequence;
        }

        public static int CountRootsInOpenInterval(
            SparsePolynomial<Rational> polynomial,
            Rational lower,
            Rational upper)
        {
            if (upper <= lower)
                throw new ArgumentException("Upper endpoint must be greater than lower endpoint.", nameof(upper));

            var sequence = Sequence(polynomial);
            return SignVariationsAt(sequence, lower) - SignVariationsAt(sequence, upper);
        }

        public static int CountDistinctRealRoots(SparsePolynomial<Rational> polynomial)
        {
            var sequence = Sequence(polynomial);
            return SignVariationsAtNegativeInfinity(sequence) - SignVariationsAtPositiveInfinity(sequence);
        }

        private static int SignVariationsAt(IReadOnlyList<SparsePolynomial<Rational>> sequence, Rational x) =>
            CountSignVariations(sequence.Select(p => Sign(Evaluate(p, x))));

        private static int SignVariationsAtPositiveInfinity(IReadOnlyList<SparsePolynomial<Rational>> sequence) =>
            CountSignVariations(sequence.Select(SignAtPositiveInfinity));

        private static int SignVariationsAtNegativeInfinity(IReadOnlyList<SparsePolynomial<Rational>> sequence) =>
            CountSignVariations(sequence.Select(SignAtNegativeInfinity));

        private static Rational Evaluate(SparsePolynomial<Rational> polynomial, Rational x)
        {
            if (polynomial.IsZero)
                return Rational.Zero;

            var result = Rational.Zero;
            for (var degree = polynomial.Degree; degree >= 0; degree--)
                result = result * x + polynomial[degree];
            return result;
        }

        private static int SignAtPositiveInfinity(SparsePolynomial<Rational> polynomial) =>
            polynomial.IsZero ? 0 : Sign(polynomial.LeadingCoefficient);

        private static int SignAtNegativeInfinity(SparsePolynomial<Rational> polynomial)
        {
            if (polynomial.IsZero)
                return 0;

            var sign = Sign(polynomial.LeadingCoefficient);
            return (polynomial.Degree & 1) == 0 ? sign : -sign;
        }

        private static int Sign(Rational value) =>
            value > Rational.Zero ? 1 :
            value < Rational.Zero ? -1 :
            0;

        private static int CountSignVariations(IEnumerable<int> signs)
        {
            var variations = 0;
            var previous = 0;
            foreach (var sign in signs)
            {
                if (sign == 0)
                    continue;

                if (previous != 0 && sign != previous)
                    variations++;

                previous = sign;
            }

            return variations;
        }
    }
}
