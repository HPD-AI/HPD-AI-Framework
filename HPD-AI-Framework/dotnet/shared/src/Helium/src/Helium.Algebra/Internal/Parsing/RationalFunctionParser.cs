using Helium.Primitives;

namespace Helium.Algebra;

internal static class RationalFunctionParser
{
    internal static RationalFunction<R> Parse<R>(ReadOnlySpan<char> s, IFormatProvider? provider)
        where R : IRing<R>, ISpanParsable<R>
    {
        // Find the top-level '/' that separates numerator from denominator.
        // Must respect parentheses: (x+1)/(x-1) splits at the '/' between the parens.
        int slashIndex = FindTopLevelSlash(s);

        if (slashIndex < 0)
        {
            // No slash — treat entire input as numerator, denominator = 1.
            var num = UnivariatePolynomialParser.Parse<R>(s, provider);
            return RationalFunction<R>.FromPolynomial(num);
        }

        var numSpan = s[..slashIndex].Trim();
        var denSpan = s[(slashIndex + 1)..].Trim();

        // Strip outer parentheses if present: (3x + 1) → 3x + 1
        numSpan = StripParens(numSpan);
        denSpan = StripParens(denSpan);

        var numerator = UnivariatePolynomialParser.Parse<R>(numSpan, provider);
        var denominator = UnivariatePolynomialParser.Parse<R>(denSpan, provider);

        return RationalFunction<R>.Create(numerator, denominator);
    }

    internal static bool TryParse<R>(ReadOnlySpan<char> s, IFormatProvider? provider, out RationalFunction<R> result)
        where R : IRing<R>, ISpanParsable<R>
    {
        try
        {
            result = Parse<R>(s, provider);
            return true;
        }
        catch
        {
            result = RationalFunction<R>.Zero;
            return false;
        }
    }

    private static int FindTopLevelSlash(ReadOnlySpan<char> s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            switch (s[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case '/' when depth == 0:
                    return i;
            }
        }
        return -1;
    }

    private static ReadOnlySpan<char> StripParens(ReadOnlySpan<char> s)
    {
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')')
            return s[1..^1].Trim();
        return s;
    }
}
