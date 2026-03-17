using Helium.Primitives;

namespace Helium.Algebra;

public static class RationalFunctionParsingExtensions
{
    extension<R>(RationalFunction<R> self) where R : IRing<R>, ISpanParsable<R>
    {
        public static RationalFunction<R> Parse(string s, IFormatProvider? provider = null) =>
            RationalFunctionParser.Parse<R>(s.AsSpan(), provider);

        public static bool TryParse(string? s, IFormatProvider? provider, out RationalFunction<R> result)
        {
            if (s is null)
            {
                result = RationalFunction<R>.Zero;
                return false;
            }

            return RationalFunctionParser.TryParse<R>(s.AsSpan(), provider, out result);
        }

        public static RationalFunction<R> Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
            RationalFunctionParser.Parse<R>(s, provider);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out RationalFunction<R> result) =>
            RationalFunctionParser.TryParse<R>(s, provider, out result);
    }
}
