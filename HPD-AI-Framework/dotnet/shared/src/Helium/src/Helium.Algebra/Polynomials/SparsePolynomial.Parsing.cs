using Helium.Primitives;

namespace Helium.Algebra;

public static class SparsePolynomialParsingExtensions
{
    extension<R>(SparsePolynomial<R> self) where R : IRing<R>, ISpanParsable<R>
    {
        public static SparsePolynomial<R> Parse(string s, IFormatProvider? provider = null) =>
            UnivariatePolynomialParser.Parse<R>(s.AsSpan(), provider);

        public static bool TryParse(string? s, IFormatProvider? provider, out SparsePolynomial<R> result)
        {
            if (s is null)
            {
                result = SparsePolynomial<R>.Zero;
                return false;
            }

            return UnivariatePolynomialParser.TryParse<R>(s.AsSpan(), provider, out result);
        }

        public static SparsePolynomial<R> Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
            UnivariatePolynomialParser.Parse<R>(s, provider);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out SparsePolynomial<R> result) =>
            UnivariatePolynomialParser.TryParse<R>(s, provider, out result);
    }
}
