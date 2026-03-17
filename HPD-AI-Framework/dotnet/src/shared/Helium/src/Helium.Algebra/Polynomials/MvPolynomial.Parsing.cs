using Helium.Primitives;

namespace Helium.Algebra;

public static class MvPolynomialParsingExtensions
{
    extension<R>(MvPolynomial<R> self) where R : ICommRing<R>, ISpanParsable<R>
    {
        public static MvPolynomial<R> Parse(string s, IFormatProvider? provider = null) =>
            MvPolynomialParser.Parse<R>(s.AsSpan(), provider);

        public static bool TryParse(string? s, IFormatProvider? provider, out MvPolynomial<R> result)
        {
            if (s is null)
            {
                result = MvPolynomial<R>.Zero;
                return false;
            }

            return MvPolynomialParser.TryParse<R>(s.AsSpan(), provider, out result);
        }

        public static MvPolynomial<R> Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
            MvPolynomialParser.Parse<R>(s, provider);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out MvPolynomial<R> result) =>
            MvPolynomialParser.TryParse<R>(s, provider, out result);
    }
}
