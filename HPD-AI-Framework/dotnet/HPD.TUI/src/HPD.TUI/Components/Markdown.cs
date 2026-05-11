using HPD.TUI.Core;
using HPD.TUI.Utilities;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace HPD.TUI.Components;

public sealed class Markdown : IComponent
{
    private static readonly Markdig.MarkdownPipeline Pipeline = new Markdig.MarkdownPipelineBuilder()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .UseTaskLists()
        .UsePipeTables()
        .Build();

    private string _source;
    private readonly Theme? _themeOverride;
    private MarkdownDocument? _document;
    private bool _parseAttempted;

    public Markdown(string source, Theme? theme = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _themeOverride = theme;
        ParseSource();
    }

    public string Source => _source;

    public void SetSource(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        ParseSource();
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var maxLine = 0;
        foreach (var line in EnumerateLines(_source.AsSpan()))
        {
            maxLine = Math.Max(maxLine, Math.Min(maxWidth, UnicodeWidth.GetWidth(TrimMarkdownPrefix(line))));
        }

        return new Measurement(Math.Min(maxWidth, Math.Min(maxLine, 1)), Math.Min(maxWidth, maxLine));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        var theme = _themeOverride ?? context.Theme;
        var document = GetDocument();
        if (document is null)
        {
            output.Write(_source.AsSpan(), theme.Text);
            return;
        }

        var state = new RenderState(theme, maxWidth);
        var first = true;

        foreach (var block in document)
        {
            if (!first)
            {
                output.WriteLineBreak();
            }

            first = false;
            RenderBlock(block, ref state, ref output);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    private MarkdownDocument? GetDocument()
    {
        if (!_parseAttempted)
        {
            ParseSource();
        }

        return _document;
    }

    private void ParseSource()
    {
        _parseAttempted = true;

        try
        {
            _document = Markdig.Markdown.Parse(_source, Pipeline);
        }
        catch
        {
            _document = null;
        }
    }

    private static void RenderBlock(Block block, ref RenderState state, ref SegmentWriter output)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RenderHeading(heading, ref state, ref output);
                break;
            case ParagraphBlock paragraph:
                RenderParagraph(paragraph, ref state, ref output);
                break;
            case FencedCodeBlock fenced:
                RenderFencedCodeBlock(fenced, ref state, ref output);
                break;
            case CodeBlock code:
                RenderCodeBlock(code, ref state, ref output);
                break;
            case ListBlock list:
                RenderList(list, 0, ref state, ref output);
                break;
            case QuoteBlock quote:
                RenderQuote(quote, 0, ref state, ref output);
                break;
            case ThematicBreakBlock:
                RenderRule(ref state, ref output);
                break;
            case HtmlBlock html:
                output.Write(html.Lines.ToString().Trim().AsSpan(), state.Theme.Border);
                break;
            case Table table:
                RenderTable(table, ref state, ref output);
                break;
            case LeafBlock leaf when leaf.Lines.ToString() is { Length: > 0 } text:
                output.Write(text.TrimEnd().AsSpan(), state.Theme.Text);
                break;
        }
    }

    private static void RenderHeading(HeadingBlock heading, ref RenderState state, ref SegmentWriter output)
    {
        var style = heading.Level switch
        {
            1 => new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Bold | TextAttributes.Underline),
            2 => new Style(state.Theme.Accent.Foreground, state.Theme.Accent.Background, TextAttributes.Bold),
            3 => new Style(state.Theme.Warning.Foreground, state.Theme.Warning.Background, TextAttributes.Bold | TextAttributes.Italic),
            4 => new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Italic),
            _ => new Style(state.Theme.Border.Foreground, state.Theme.Border.Background, TextAttributes.Bold)
        };

        RenderInlines(heading.Inline, style, ref state, ref output);
    }

    private static void RenderParagraph(ParagraphBlock paragraph, ref RenderState state, ref SegmentWriter output)
    {
        RenderInlines(paragraph.Inline, state.Theme.Text, ref state, ref output);
    }

    private static void RenderFencedCodeBlock(FencedCodeBlock codeBlock, ref RenderState state, ref SegmentWriter output)
    {
        var language = codeBlock.Info.AsSpan().Trim();
        RenderCodeHeader(language, ref state, ref output);
        output.WriteLineBreak();

        var code = codeBlock.Lines.ToString().TrimEnd();
        RenderCodeLines(code.AsSpan(), language, ref state, ref output);

        output.WriteLineBreak();
        RenderRule(ref state, ref output);
    }

    private static void RenderCodeBlock(CodeBlock codeBlock, ref RenderState state, ref SegmentWriter output)
    {
        RenderCodeHeader(default, ref state, ref output);
        output.WriteLineBreak();

        var code = codeBlock.Lines.ToString().TrimEnd();
        RenderCodeLines(code.AsSpan(), default, ref state, ref output);

        output.WriteLineBreak();
        RenderRule(ref state, ref output);
    }

    private static void RenderCodeHeader(ReadOnlySpan<char> language, ref RenderState state, ref SegmentWriter output)
    {
        output.Write("╭ code", state.Theme.Border);
        if (!language.IsEmpty)
        {
            output.Write(" ", state.Theme.Border);
            output.Write(language, state.Theme.Warning);
        }

        output.Write(" ╮", state.Theme.Border);
    }

    private static void RenderCodeLines(ReadOnlySpan<char> code, ReadOnlySpan<char> language, ref RenderState state, ref SegmentWriter output)
    {
        var first = true;
        foreach (var line in EnumerateLines(code))
        {
            if (!first)
            {
                output.WriteLineBreak();
            }

            first = false;
            output.Write("│ ", state.Theme.Border);
            RenderHighlightedCode(line, language, ref state, ref output);
        }
    }

    private static void RenderList(ListBlock list, int depth, ref RenderState state, ref SegmentWriter output)
    {
        var index = list.OrderedStart is null ? 1 : int.Parse(list.OrderedStart);
        var first = true;

        foreach (var child in list)
        {
            if (child is not ListItemBlock item)
            {
                continue;
            }

            if (!first)
            {
                output.WriteLineBreak();
            }

            first = false;
            output.Write(new string(' ', depth * 2).AsSpan(), state.Theme.Text);

            if (list.IsOrdered)
            {
                output.Write(index.ToString().AsSpan(), state.Theme.Accent);
                output.Write(". ", state.Theme.Accent);
                index++;
            }
            else
            {
                var bullet = depth switch
                {
                    0 => "• ",
                    1 => "o ",
                    _ => "- "
                };
                output.Write(bullet, state.Theme.Border);
            }

            RenderListItem(item, depth, ref state, ref output);
        }
    }

    private static void RenderListItem(ListItemBlock item, int depth, ref RenderState state, ref SegmentWriter output)
    {
        var first = true;
        foreach (var block in item)
        {
            if (block is ParagraphBlock paragraph)
            {
                if (!first)
                {
                    output.WriteLineBreak();
                    output.Write(new string(' ', (depth + 1) * 2).AsSpan(), state.Theme.Text);
                }

                RenderInlines(paragraph.Inline, state.Theme.Text, ref state, ref output);
                first = false;
            }
            else if (block is ListBlock nested)
            {
                output.WriteLineBreak();
                RenderList(nested, depth + 1, ref state, ref output);
                first = false;
            }
            else
            {
                if (!first)
                {
                    output.WriteLineBreak();
                    output.Write(new string(' ', (depth + 1) * 2).AsSpan(), state.Theme.Text);
                }

                RenderBlock(block, ref state, ref output);
                first = false;
            }
        }
    }

    private static void RenderQuote(QuoteBlock quote, int depth, ref RenderState state, ref SegmentWriter output)
    {
        var first = true;
        foreach (var block in quote)
        {
            if (!first)
            {
                output.WriteLineBreak();
            }

            first = false;
            output.Write(new string('>', depth).AsSpan(), state.Theme.Success);
            output.Write("| ", state.Theme.Success);

            if (block is ParagraphBlock paragraph)
            {
                RenderInlines(paragraph.Inline, new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Italic), ref state, ref output);
            }
            else if (block is QuoteBlock nested)
            {
                RenderQuote(nested, depth + 1, ref state, ref output);
            }
            else
            {
                RenderBlock(block, ref state, ref output);
            }
        }
    }

    private static void RenderTable(Table table, ref RenderState state, ref SegmentWriter output)
    {
        var rows = table.OfType<TableRow>().ToArray();
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            if (rowIndex > 0)
            {
                output.WriteLineBreak();
            }

            output.Write("|", state.Theme.Border);
            foreach (var cell in rows[rowIndex].OfType<TableCell>())
            {
                output.Write(" ", state.Theme.Text);
                var style = rowIndex == 0
                    ? new Style(state.Theme.Text.Foreground, state.Theme.Text.Background, TextAttributes.Bold)
                    : state.Theme.Text;
                RenderTableCell(cell, style, ref state, ref output);
                output.Write(" |", state.Theme.Border);
            }
        }
    }

    private static void RenderTableCell(TableCell cell, Style style, ref RenderState state, ref SegmentWriter output)
    {
        var first = true;
        foreach (var block in cell)
        {
            if (!first)
            {
                output.Write(" ", state.Theme.Text);
            }

            first = false;
            if (block is ParagraphBlock paragraph)
            {
                RenderInlines(paragraph.Inline, style, ref state, ref output);
            }
            else if (block is LeafBlock leaf)
            {
                output.Write(leaf.Lines.ToString().Trim().AsSpan(), style);
            }
        }
    }

    private static void RenderInlines(ContainerInline? container, Style baseStyle, ref RenderState state, ref SegmentWriter output)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            RenderInline(inline, baseStyle, ref state, ref output);
        }
    }

    private static void RenderInline(Inline inline, Style baseStyle, ref RenderState state, ref SegmentWriter output)
    {
        switch (inline)
        {
            case LiteralInline literal:
                output.Write(literal.Content.AsSpan(), baseStyle);
                break;
            case EmphasisInline emphasis:
                RenderEmphasis(emphasis, baseStyle, ref state, ref output);
                break;
            case CodeInline code:
                output.Write(code.Content.AsSpan(), new Style(state.Theme.Accent.Foreground, Color.Gray));
                break;
            case LinkInline { IsImage: true } image:
                output.Write("[img] ", state.Theme.Border);
                RenderInlines(image, state.Theme.Border, ref state, ref output);
                break;
            case LinkInline link:
                RenderInlines(link, new Style(state.Theme.Accent.Foreground, state.Theme.Accent.Background, TextAttributes.Underline), ref state, ref output);
                break;
            case AutolinkInline autoLink:
                output.Write(autoLink.Url.AsSpan(), new Style(state.Theme.Accent.Foreground, state.Theme.Accent.Background, TextAttributes.Underline));
                break;
            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                {
                    output.WriteLineBreak();
                }
                else
                {
                    output.Write(" ", baseStyle);
                }

                break;
            case HtmlInline html:
                output.Write(html.Tag.AsSpan(), state.Theme.Border);
                break;
            case Markdig.Extensions.TaskLists.TaskList task:
                output.Write(task.Checked ? "[x] " : "[ ] ", task.Checked ? state.Theme.Success : state.Theme.Border);
                break;
            case ContainerInline nested:
                RenderInlines(nested, baseStyle, ref state, ref output);
                break;
        }
    }

    private static void RenderEmphasis(EmphasisInline emphasis, Style baseStyle, ref RenderState state, ref SegmentWriter output)
    {
        var attributes = emphasis.DelimiterChar == '~'
            ? baseStyle.Attributes | TextAttributes.Strikethrough
            : emphasis.DelimiterCount switch
            {
                1 => baseStyle.Attributes | TextAttributes.Italic,
                2 => baseStyle.Attributes | TextAttributes.Bold,
                3 => baseStyle.Attributes | TextAttributes.Bold | TextAttributes.Italic,
                _ => baseStyle.Attributes
            };

        RenderInlines(emphasis, new Style(baseStyle.Foreground, baseStyle.Background, attributes), ref state, ref output);
    }

    private static void RenderHighlightedCode(ReadOnlySpan<char> line, ReadOnlySpan<char> language, ref RenderState state, ref SegmentWriter output)
    {
        var pos = 0;
        var commentPrefix = GetCommentPrefix(language);

        while (pos < line.Length)
        {
            if (!commentPrefix.IsEmpty && line[pos..].StartsWith(commentPrefix, StringComparison.Ordinal))
            {
                output.Write(line[pos..], state.Theme.Border);
                return;
            }

            var ch = line[pos];
            if (char.IsWhiteSpace(ch))
            {
                output.Write(line[pos..(pos + 1)], state.Theme.Text);
                pos++;
                continue;
            }

            if (ch is '"' or '\'' or '`')
            {
                var end = FindQuotedStringEnd(line, pos, ch);
                output.Write(line[pos..end], state.Theme.Warning);
                pos = end;
                continue;
            }

            if (char.IsAsciiDigit(ch) || (ch == '-' && pos + 1 < line.Length && char.IsAsciiDigit(line[pos + 1])))
            {
                var end = pos + 1;
                while (end < line.Length && IsNumberChar(line[end]))
                {
                    end++;
                }

                output.Write(line[pos..end], state.Theme.Success);
                pos = end;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var end = pos + 1;
                while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '_'))
                {
                    end++;
                }

                var word = line[pos..end];
                output.Write(word, IsKeyword(word, language) ? state.Theme.Accent : state.Theme.Text);
                pos = end;
                continue;
            }

            output.Write(line[pos..(pos + 1)], IsPunctuation(ch) ? state.Theme.Border : state.Theme.Text);
            pos++;
        }
    }

    private static void RenderRule(ref RenderState state, ref SegmentWriter output)
    {
        Span<char> buffer = stackalloc char[Math.Min(Math.Max(state.MaxWidth, 1), 120)];
        buffer.Fill('─');
        output.Write(buffer, state.Theme.Border);
    }

    private static ReadOnlySpan<char> TrimMarkdownPrefix(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("### ", StringComparison.Ordinal))
        {
            return trimmed[4..];
        }

        if (trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return trimmed[3..];
        }

        if (trimmed.StartsWith("# ", StringComparison.Ordinal) ||
            trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal) ||
            trimmed.StartsWith("+ ", StringComparison.Ordinal) ||
            trimmed.StartsWith("> ", StringComparison.Ordinal))
        {
            return trimmed[2..];
        }

        return line;
    }

    private static ReadOnlySpan<char> GetCommentPrefix(ReadOnlySpan<char> language)
    {
        if (EqualsLanguage(language, "python") || EqualsLanguage(language, "py") ||
            EqualsLanguage(language, "bash") || EqualsLanguage(language, "sh") ||
            EqualsLanguage(language, "shell") || EqualsLanguage(language, "zsh") ||
            EqualsLanguage(language, "yaml") || EqualsLanguage(language, "yml"))
        {
            return "#";
        }

        if (EqualsLanguage(language, "sql"))
        {
            return "--";
        }

        return "//";
    }

    private static bool IsKeyword(ReadOnlySpan<char> word, ReadOnlySpan<char> language)
    {
        if (EqualsLanguage(language, "python") || EqualsLanguage(language, "py"))
        {
            return IsOneOf(word, "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "elif", "else", "except", "finally", "for", "from", "if", "import", "in", "is", "lambda", "not", "or", "pass", "raise", "return", "try", "while", "with", "yield");
        }

        if (EqualsLanguage(language, "javascript") || EqualsLanguage(language, "js") ||
            EqualsLanguage(language, "typescript") || EqualsLanguage(language, "ts"))
        {
            return IsOneOf(word, "async", "await", "break", "case", "catch", "class", "const", "continue", "default", "else", "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "let", "new", "null", "return", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "while");
        }

        if (EqualsLanguage(language, "sql"))
        {
            return IsOneOfIgnoreCase(word, "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "TABLE", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "AND", "OR", "NOT", "NULL", "ORDER", "BY", "GROUP", "LIMIT", "AS");
        }

        return IsOneOf(word, "abstract", "as", "base", "bool", "break", "case", "catch", "class", "const", "continue", "default", "delegate", "do", "else", "enum", "event", "false", "finally", "for", "foreach", "if", "in", "int", "interface", "internal", "is", "lock", "namespace", "new", "null", "object", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sealed", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "using", "var", "void", "while", "async", "await", "record", "yield");
    }

    private static bool IsOneOf(ReadOnlySpan<char> word, params string[] values)
    {
        foreach (var value in values)
        {
            if (word.Equals(value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOneOfIgnoreCase(ReadOnlySpan<char> word, params string[] values)
    {
        foreach (var value in values)
        {
            if (word.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool EqualsLanguage(ReadOnlySpan<char> language, string value) =>
        language.Equals(value, StringComparison.OrdinalIgnoreCase);

    private static bool IsNumberChar(char ch) =>
        char.IsAsciiDigit(ch) || ch is '.' or 'e' or 'E' or 'f' or 'F' or 'd' or 'D' or 'l' or 'L' or 'm' or 'M' or 'x' or 'X' ||
        (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

    private static bool IsPunctuation(char ch) => "+-*/%=<>!&|^~?:;,.()[]{}".Contains(ch);

    private static int FindQuotedStringEnd(ReadOnlySpan<char> line, int start, char delimiter)
    {
        var pos = start + 1;
        while (pos < line.Length)
        {
            if (line[pos] == '\\' && pos + 1 < line.Length)
            {
                pos += 2;
                continue;
            }

            if (line[pos] == delimiter)
            {
                return pos + 1;
            }

            pos++;
        }

        return line.Length;
    }

    private static LineEnumerable EnumerateLines(ReadOnlySpan<char> source) => new(source);

    private readonly ref struct LineEnumerable
    {
        private readonly ReadOnlySpan<char> _source;

        public LineEnumerable(ReadOnlySpan<char> source)
        {
            _source = source;
        }

        public LineEnumerator GetEnumerator() => new(_source);
    }

    private ref struct LineEnumerator
    {
        private ReadOnlySpan<char> _remaining;

        public LineEnumerator(ReadOnlySpan<char> source)
        {
            _remaining = source;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
            {
                return false;
            }

            var next = _remaining.IndexOf('\n');
            if (next < 0)
            {
                Current = _remaining.TrimEnd('\r');
                _remaining = [];
                return true;
            }

            Current = _remaining[..next].TrimEnd('\r');
            _remaining = _remaining[(next + 1)..];
            return true;
        }
    }

    private readonly ref struct RenderState
    {
        public RenderState(Theme theme, int maxWidth)
        {
            Theme = theme;
            MaxWidth = maxWidth;
        }

        public Theme Theme { get; }

        public int MaxWidth { get; }
    }
}
