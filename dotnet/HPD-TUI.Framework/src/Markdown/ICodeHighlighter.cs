using System.Collections.Immutable;
using HPD.TUI.Core;

namespace HPD.TUI.Markdown;

/// <summary>Highlights fenced code independently from Markdown parsing and layout policy.</summary>
public interface ICodeHighlighter
{
    /// <summary>
    /// Highlights source using a normalized optional language identifier. Non-decorative output must either
    /// concatenate to the exact input (with line endings normalized to lines) or provide per-run source maps
    /// whose source offsets are relative to the supplied input.
    /// </summary>
    CodeHighlightResult Highlight(ReadOnlyMemory<char> source, string? language, MarkdownTheme theme);
}

/// <summary>Represents immutable highlighted code lines.</summary>
public sealed record CodeHighlightResult(ImmutableArray<StyledTerminalLine> Lines, string? NormalizedLanguage);

/// <summary>Provides lexical highlighting and a plain fallback for unsupported languages.</summary>
/// <remarks>Function calls and declarations use local token context. Types use built-in names,
/// declaration context, and uppercase naming conventions; this is not semantic symbol resolution.</remarks>
public sealed class BasicCodeHighlighter : ICodeHighlighter
{
    /// <inheritdoc />
    public CodeHighlightResult Highlight(ReadOnlyMemory<char> source, string? language, MarkdownTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var normalized = NormalizeLanguage(language);
        var lines = source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (normalized is not ("csharp" or "javascript" or "typescript" or "python" or "bash"))
            return new(lines.Select(line => new StyledTerminalLine([new StyledTerminalRun(line, theme.CodeBody)])).ToImmutableArray(), normalized);
        var highlighted = ImmutableArray.CreateBuilder<StyledTerminalLine>();
        var blockComment = false;
        foreach (var line in lines) highlighted.Add(HighlightLine(line, normalized, theme, ref blockComment));
        return new(highlighted.ToImmutable(), normalized);
    }

    private static StyledTerminalLine HighlightLine(string line, string? language, MarkdownTheme theme, ref bool blockComment)
    {
        var runs = ImmutableArray.CreateBuilder<StyledTerminalRun>();
        var position = 0;
        var previousWord = string.Empty;
        while (position < line.Length)
        {
            var start = position;
            Style style;
            var value = line[position];
            var slashComments = language is "csharp" or "javascript" or "typescript";
            if (blockComment || (slashComments && line.AsSpan(position).StartsWith("/*")))
            {
                var close = line.IndexOf("*/", position + (blockComment ? 0 : 2), StringComparison.Ordinal);
                blockComment = close < 0;
                position = close < 0 ? line.Length : close + 2;
                style = theme.Syntax.Comment;
            }
            else if ((slashComments && line.AsSpan(position).StartsWith("//")) ||
                (language is "python" or "bash" && value == '#'))
            {
                position = line.Length;
                style = theme.Syntax.Comment;
            }
            else if (char.IsWhiteSpace(value))
            {
                while (++position < line.Length && char.IsWhiteSpace(line[position])) { }
                style = theme.Syntax.Text;
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
                style = theme.Syntax.String;
            }
            else if (char.IsAsciiDigit(value))
            {
                while (++position < line.Length && (char.IsAsciiLetterOrDigit(line[position]) || line[position] is '.' or '_')) { }
                style = theme.Syntax.Number;
            }
            else if (char.IsLetter(value) || value == '_')
            {
                while (++position < line.Length && (char.IsLetterOrDigit(line[position]) || line[position] == '_')) { }
                var word = line[start..position];
                var next = position;
                while (next < line.Length && char.IsWhiteSpace(line[next])) next++;
                var isType = language != "bash" && (IsBuiltInType(word, language) ||
                    previousWord is "class" or "struct" or "interface" or "enum" or "record" or "new");
                style = isType ? theme.Syntax.Type
                    : IsKeyword(word.AsSpan(), language) ? theme.Syntax.Keyword
                    : previousWord is "def" or "function" || (next < line.Length && line[next] == '(')
                        ? theme.Syntax.Function
                    : language != "bash" && char.IsUpper(word[0]) ? theme.Syntax.Type
                    : theme.Syntax.Identifier;
                previousWord = word;
            }
            else
            {
                position++;
                style = "=+-*/%<>!&|^~?:".Contains(value) ? theme.Syntax.Operator : theme.Syntax.Punctuation;
                previousWord = string.Empty;
            }
            runs.Add(new(line[start..position], style));
        }
        if (runs.Count == 0) runs.Add(new(string.Empty, theme.Syntax.Text));
        return new(runs.ToImmutable());
    }

    private static bool IsBuiltInType(string word, string? language) => language switch
    {
        "csharp" => word is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double" or
            "float" or "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "object" or
            "string" or "void" or "nint" or "nuint",
        "typescript" => word is "string" or "number" or "boolean" or "any" or "unknown" or "never" or "void",
        "python" => word is "int" or "float" or "str" or "bool" or "list" or "dict" or "set" or "tuple" or "bytes",
        _ => false
    };

    private static bool IsKeyword(ReadOnlySpan<char> word, string? language)
    {
        var words = language switch
        {
            "python" => PythonKeywords,
            "javascript" or "typescript" => JavaScriptKeywords,
            "csharp" => CSharpKeywords,
            _ => Array.Empty<string>()
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
