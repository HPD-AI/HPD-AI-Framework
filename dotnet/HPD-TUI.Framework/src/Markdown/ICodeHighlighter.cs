using System.Collections.Immutable;
using HPD.TUI.Core;

namespace HPD.TUI.Markdown;

/// <summary>Highlights fenced code independently from Markdown parsing and layout policy.</summary>
public interface ICodeHighlighter
{
    /// <summary>Highlights source using a normalized optional language identifier.</summary>
    CodeHighlightResult Highlight(ReadOnlyMemory<char> source, string? language, MarkdownTheme theme);
}

/// <summary>Represents immutable highlighted code lines.</summary>
public sealed record CodeHighlightResult(ImmutableArray<StyledTerminalLine> Lines, string? NormalizedLanguage);

/// <summary>Provides the conservative built-in highlighter and plain fallback.</summary>
public sealed class BasicCodeHighlighter : ICodeHighlighter
{
    /// <inheritdoc />
    public CodeHighlightResult Highlight(ReadOnlyMemory<char> source, string? language, MarkdownTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var normalized = NormalizeLanguage(language);
        var lines = source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return new(lines.Select(line => HighlightLine(line, normalized, theme)).ToImmutableArray(), normalized);
    }

    private static StyledTerminalLine HighlightLine(string line, string? language, MarkdownTheme theme)
    {
        var runs = ImmutableArray.CreateBuilder<StyledTerminalRun>();
        var position = 0;
        while (position < line.Length)
        {
            var start = position;
            Style style;
            var value = line[position];
            if (char.IsWhiteSpace(value))
            {
                while (++position < line.Length && char.IsWhiteSpace(line[position])) { }
                style = theme.Body;
            }
            else if (value is '"' or '\'' or '`')
            {
                var delimiter = value;
                position++;
                while (position < line.Length)
                {
                    if (line[position] == '\\' && position + 1 < line.Length) { position += 2; continue; }
                    if (line[position++] == delimiter) break;
                }
                style = theme.CodeLanguage;
            }
            else if (char.IsAsciiDigit(value))
            {
                while (++position < line.Length && (char.IsAsciiLetterOrDigit(line[position]) || line[position] is '.' or '_')) { }
                style = theme.QuoteMarker;
            }
            else if (char.IsLetter(value) || value == '_')
            {
                while (++position < line.Length && (char.IsLetterOrDigit(line[position]) || line[position] == '_')) { }
                style = IsKeyword(line.AsSpan(start, position - start), language) ? theme.Heading1 : theme.Body;
            }
            else
            {
                position++;
                style = theme.CodeBorder;
            }
            runs.Add(new(line[start..position], style));
        }
        if (runs.Count == 0) runs.Add(new(string.Empty, theme.Body));
        return new(runs.ToImmutable());
    }

    private static bool IsKeyword(ReadOnlySpan<char> word, string? language)
    {
        var words = language switch
        {
            "python" => PythonKeywords,
            "javascript" or "typescript" => JavaScriptKeywords,
            _ => CSharpKeywords
        };
        foreach (var candidate in words)
            if (word.Equals(candidate, StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly string[] CSharpKeywords =
        ["abstract", "async", "await", "bool", "class", "const", "else", "enum", "false", "for", "foreach", "if", "int", "interface", "internal", "namespace", "new", "null", "private", "protected", "public", "readonly", "record", "return", "sealed", "static", "string", "struct", "switch", "true", "using", "var", "void", "while"];
    private static readonly string[] PythonKeywords =
        ["False", "None", "True", "and", "as", "async", "await", "class", "def", "elif", "else", "except", "for", "from", "if", "import", "in", "is", "lambda", "not", "or", "return", "try", "while", "with", "yield"];
    private static readonly string[] JavaScriptKeywords =
        ["async", "await", "class", "const", "else", "export", "extends", "false", "for", "function", "if", "import", "let", "new", "null", "return", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "while"];

    private static string? NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        "cs" or "c#" => "csharp",
        "js" => "javascript",
        "ts" => "typescript",
        "py" => "python",
        "sh" or "zsh" => "bash",
        "" => null,
        var value => value
    };
}
