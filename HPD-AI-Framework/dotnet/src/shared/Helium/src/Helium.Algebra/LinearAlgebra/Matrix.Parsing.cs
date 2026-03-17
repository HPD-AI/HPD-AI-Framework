using Helium.Primitives;

namespace Helium.Algebra;

public static class MatrixParsingExtensions
{
    extension<R>(Matrix<R> self) where R : IRing<R>, ISpanParsable<R>
    {
        public static Matrix<R> Parse(string s, IFormatProvider? provider = null) =>
            MatrixParser.Parse<R>(s.AsSpan(), provider);

        public static bool TryParse(string? s, IFormatProvider? provider, out Matrix<R> result)
        {
            if (s is null)
            {
                result = Matrix<R>.Zero(0, 0);
                return false;
            }

            return MatrixParser.TryParse<R>(s.AsSpan(), provider, out result);
        }

        public static Matrix<R> Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
            MatrixParser.Parse<R>(s, provider);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Matrix<R> result) =>
            MatrixParser.TryParse<R>(s, provider, out result);
    }
}
