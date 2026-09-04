using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Content;

public sealed class MarkupBlock : Component, IContentBlock
{
    private readonly MarkupParser _parser;
    private StyledTextRun[] _runs;

    public MarkupBlock(string source, Theme? theme = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _parser = new MarkupParser(theme);
        _runs = _parser.Parse(Source);
    }

    public ContentBlockKind Kind => ContentBlockKind.Markup;

    public string Source { get; private set; }

    public ReadOnlySpan<StyledTextRun> Runs => _runs;

    public IReadOnlyList<string> ParseDiagnostics => _parser.Diagnostics;

    public void SetSource(string source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _runs = _parser.Parse(Source);
    }

    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        var maxLine = 0;
        var currentLine = 0;
        var maxWord = 0;
        var currentWord = 0;

        foreach (var run in _runs)
        {
            var enumerator = new RuneEnumerator(run.Text);
            while (enumerator.MoveNext())
            {
                var rune = enumerator.Current;
                if (rune.Value is '\r')
                {
                    continue;
                }

                if (rune.Value is '\n')
                {
                    maxLine = Math.Max(maxLine, currentLine);
                    maxWord = Math.Max(maxWord, currentWord);
                    currentLine = 0;
                    currentWord = 0;
                    continue;
                }

                var width = UnicodeWidth.GetWidth(rune);
                currentLine += width;

                if (Rune.IsWhiteSpace(rune))
                {
                    maxWord = Math.Max(maxWord, currentWord);
                    currentWord = 0;
                }
                else
                {
                    currentWord += width;
                }
            }
        }

        maxLine = Math.Max(maxLine, currentLine);
        maxWord = Math.Max(maxWord, currentWord);
        return new Measurement(Math.Min(maxWidth, maxWord), Math.Min(maxWidth, maxLine));
    }

    public override void Render(in RenderContext context, int maxWidth, ref DisplayListBuilder output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var lineWidth = 0;
        foreach (var run in _runs)
        {
            WriteRun(run, maxWidth, ref lineWidth, ref output);
        }
    }

    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    public static MarkupBlock Create(string source, Theme? theme = null) => new(source, theme);

    private static void WriteRun(StyledTextRun run, int maxWidth, ref int lineWidth, ref DisplayListBuilder output)
    {
        var lineStart = 0;
        var pos = 0;
        var text = run.Text;

        var enumerator = new RuneEnumerator(text);
        while (enumerator.MoveNext())
        {
            var rune = enumerator.Current;
            var runeLength = rune.Utf16SequenceLength;

            if (rune.Value is '\r')
            {
                pos += runeLength;
                continue;
            }

            if (rune.Value is '\n')
            {
                if (pos > lineStart)
                {
                    output.Write(text.AsSpan(lineStart, pos - lineStart), run.Style);
                }

                output.WriteLineBreak();
                pos += runeLength;
                lineStart = pos;
                lineWidth = 0;
                continue;
            }

            var width = UnicodeWidth.GetWidth(rune);
            if (lineWidth > 0 && lineWidth + width > maxWidth)
            {
                if (pos > lineStart)
                {
                    output.Write(text.AsSpan(lineStart, pos - lineStart), run.Style);
                }

                output.WriteLineBreak();
                lineStart = pos;
                lineWidth = 0;
            }

            lineWidth += width;
            pos += runeLength;
        }

        if (pos > lineStart)
        {
            output.Write(text.AsSpan(lineStart, pos - lineStart), run.Style);
        }
    }
}
