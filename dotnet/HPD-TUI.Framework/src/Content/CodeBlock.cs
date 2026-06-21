using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Content;

public sealed class CodeBlock : IContentBlock
{
    private readonly List<string> _lines = [];

    public CodeBlock(string code, string? language = null)
    {
        Language = string.IsNullOrWhiteSpace(language) ? null : language;
        SetCode(code);
    }

    public ContentBlockKind Kind => ContentBlockKind.Code;

    public string? Language { get; }

    public IReadOnlyList<string> Lines => _lines;

    public void SetCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        _lines.Clear();

        var start = 0;
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] != '\n')
            {
                continue;
            }

            var length = i > start && code[i - 1] == '\r' ? i - start - 1 : i - start;
            _lines.Add(code.Substring(start, length));
            start = i + 1;
        }

        if (start < code.Length)
        {
            _lines.Add(code[start..]);
        }
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = 0;
        foreach (var line in _lines)
        {
            width = Math.Max(width, Math.Min(maxWidth, UnicodeWidth.GetWidth(line) + 2));
        }

        if (Language is not null)
        {
            width = Math.Max(width, Math.Min(maxWidth, UnicodeWidth.GetWidth(Language) + 7));
        }

        return new Measurement(Math.Min(width, maxWidth), Math.Min(width, maxWidth));
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        output.Write("╭ code", context.Theme.Border);
        if (Language is not null)
        {
            output.Write(" ", context.Theme.Border);
            output.Write(Language.AsSpan(), context.Theme.Warning);
        }

        output.Write(" ╮", context.Theme.Border);

        foreach (var line in _lines)
        {
            output.WriteLineBreak();
            output.Write("│ ", context.Theme.Border);
            WriteClipped(line, Math.Max(0, maxWidth - 2), context.Theme.Text, ref output);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
    }

    public static CodeBlock Create(string code, string? language = null) => new(code, language);

    private static void WriteClipped(string value, int maxWidth, Style style, ref SegmentWriter output)
    {
        var used = 0;
        var enumerator = new RuneEnumerator(value.AsSpan());
        Span<char> buffer = stackalloc char[2];
        while (enumerator.MoveNext())
        {
            var width = UnicodeWidth.GetWidth(enumerator.Current);
            if (used + width > maxWidth)
            {
                break;
            }

            if (enumerator.Current.TryEncodeToUtf16(buffer, out var written))
            {
                output.Write(buffer[..written], style);
            }

            used += width;
        }
    }
}
