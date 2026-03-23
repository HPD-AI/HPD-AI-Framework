using System.Text;

namespace Helium.Algebra;

/// <summary>
/// Shared formatting utilities for mathematical types.
/// </summary>
internal static class FormatHelpers
{
    private static readonly char[] SuperscriptDigits =
        ['\u2070', '\u00B9', '\u00B2', '\u00B3', '\u2074', '\u2075', '\u2076', '\u2077', '\u2078', '\u2079'];

    internal static string ToSuperscript(int n)
    {
        if (n < 0)
            return "\u207B" + ToSuperscript(-n);
        if (n < 10)
            return SuperscriptDigits[n].ToString();

        var sb = new StringBuilder();
        foreach (var c in n.ToString())
            sb.Append(SuperscriptDigits[c - '0']);
        return sb.ToString();
    }

    internal static string FormatElement<R>(R element, string? format, IFormatProvider? provider)
    {
        if (format is not null && element is IFormattable f)
            return f.ToString(format, provider);
        return element?.ToString() ?? "0";
    }

    /// <summary>
    /// Whether a coefficient string needs parentheses when placed before a variable.
    /// True if (after stripping a leading minus) the string contains +, -, or /.
    /// </summary>
    internal static bool NeedsParentheses(string coeffStr)
    {
        var s = coeffStr.AsSpan();
        if (s.Length > 0 && s[0] == '-') s = s[1..];
        foreach (var c in s)
        {
            if (c == '/' || c == '+' || c == '-')
                return true;
        }
        return false;
    }

    internal static void AppendTerm(StringBuilder sb, string coeffStr, string varStr, bool first)
    {
        if (first)
        {
            sb.Append(coeffStr);
            sb.Append(varStr);
        }
        else
        {
            if (coeffStr.StartsWith('-'))
            {
                sb.Append(" - ");
                sb.Append(coeffStr.AsSpan(1));
            }
            else
            {
                sb.Append(" + ");
                sb.Append(coeffStr);
            }
            sb.Append(varStr);
        }
    }

    internal static void AppendSignedTerm(StringBuilder sb, bool positive, string varStr, bool first)
    {
        if (first)
        {
            if (!positive) sb.Append('-');
            sb.Append(varStr);
        }
        else
        {
            sb.Append(positive ? " + " : " - ");
            sb.Append(varStr);
        }
    }

    /// <summary>
    /// Returns true if the default text representation of a ring element starts with '-'.
    /// Used by MathML formatters to decide whether to emit a minus sign separately.
    /// </summary>
    internal static bool IsNegativeLike<R>(R coeff, IFormatProvider? provider)
    {
        var defaultText = FormatElement(coeff, null, provider);
        return defaultText.Length > 0 && defaultText[0] == '-';
    }
}
