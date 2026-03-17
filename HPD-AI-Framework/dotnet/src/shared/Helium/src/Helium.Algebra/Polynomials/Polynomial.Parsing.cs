using Helium.Primitives;

namespace Helium.Algebra;

public static class PolynomialParsingExtensions
{
    extension<R>(Polynomial<R> self) where R : IRing<R>, ISpanParsable<R>
    {
        public static Polynomial<R> Parse(string s, IFormatProvider? provider = null) =>
            UnivariatePolynomialParser.Parse<R>(s.AsSpan(), provider);

        public static bool TryParse(string? s, IFormatProvider? provider, out Polynomial<R> result)
        {
            if (s is null)
            {
                result = Polynomial<R>.Zero;
                return false;
            }

            return UnivariatePolynomialParser.TryParse<R>(s.AsSpan(), provider, out result);
        }

        public static Polynomial<R> Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
            UnivariatePolynomialParser.Parse<R>(s, provider);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Polynomial<R> result) =>
            UnivariatePolynomialParser.TryParse<R>(s, provider, out result);
    }
}
