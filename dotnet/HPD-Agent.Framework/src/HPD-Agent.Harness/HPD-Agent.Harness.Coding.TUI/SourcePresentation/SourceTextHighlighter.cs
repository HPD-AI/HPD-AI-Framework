using HPD.TUI.Core;

namespace HPD.Agent.ToolHarness.Coding.TUI.SourcePresentation;

internal static class SourceTextHighlighter
{
    private static readonly HashSet<string> CommonKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte",
        "case", "catch", "char", "class", "const", "continue", "decimal",
        "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "export", "extends", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "from", "func", "function", "get", "global", "go", "if",
        "implements", "implicit", "import", "in", "init", "int", "interface",
        "internal", "is", "let", "lock", "long", "match", "module", "namespace",
        "new", "null", "object", "operator", "out", "override", "package", "params",
        "partial", "private", "protected", "public", "readonly", "record", "ref",
        "return", "sbyte", "sealed", "set", "short", "sizeof", "static", "string",
        "struct", "super", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual",
        "void", "volatile", "when", "where", "while", "with", "yield"
    };

    public static void Render(
        string text,
        string? language,
        Style baseStyle,
        Theme theme,
        ref DisplayListBuilder output)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(language))
        {
            output.Write(text.AsSpan(), baseStyle);
            return;
        }

        var pos = 0;
        var commentPrefix = CommentPrefix(language);
        while (pos < text.Length)
        {
            if (commentPrefix is not null &&
                text.AsSpan(pos).StartsWith(commentPrefix, StringComparison.Ordinal))
            {
                output.Write(text.AsSpan(pos), WithBackground(theme.Border, baseStyle.Background));
                return;
            }

            var ch = text[pos];
            if (char.IsWhiteSpace(ch))
            {
                output.Write(text.AsSpan(pos, 1), baseStyle);
                pos++;
                continue;
            }

            if (ch is '"' or '\'' or '`')
            {
                var end = FindQuotedEnd(text, pos, ch);
                output.Write(text.AsSpan(pos, end - pos), WithBackground(theme.Warning, baseStyle.Background));
                pos = end;
                continue;
            }

            if (char.IsAsciiDigit(ch) ||
                ch == '-' && pos + 1 < text.Length && char.IsAsciiDigit(text[pos + 1]))
            {
                var end = pos + 1;
                while (end < text.Length &&
                       (char.IsAsciiLetterOrDigit(text[end]) || text[end] is '.' or '_' or '+' or '-'))
                {
                    end++;
                }

                output.Write(text.AsSpan(pos, end - pos), WithBackground(theme.Success, baseStyle.Background));
                pos = end;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var end = pos + 1;
                while (end < text.Length &&
                       (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                {
                    end++;
                }

                var word = text[pos..end];
                output.Write(
                    word.AsSpan(),
                    CommonKeywords.Contains(word)
                        ? WithBackground(theme.Accent, baseStyle.Background)
                        : baseStyle);
                pos = end;
                continue;
            }

            output.Write(text.AsSpan(pos, 1), baseStyle);
            pos++;
        }
    }

    private static int FindQuotedEnd(string text, int start, char quote)
    {
        var escaped = false;
        for (var i = start + 1; i < text.Length; i++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[i] == '\\')
            {
                escaped = true;
                continue;
            }

            if (text[i] == quote)
            {
                return i + 1;
            }
        }

        return text.Length;
    }

    private static string? CommentPrefix(string language)
        => language.ToLowerInvariant() switch
        {
            "python" or "ruby" or "shell" or "powershell" or "yaml" or "toml" => "#",
            "sql" => "--",
            "xml" => null,
            _ => "//"
        };

    private static Style WithBackground(Style style, Color background)
        => background.IsDefault
            ? style
            : new Style(style.Foreground, background, style.Attributes);
}
