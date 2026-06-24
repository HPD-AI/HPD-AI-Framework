using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Components;

public sealed class Text : IComponent
{
    private string _value;
    private Style _style;

    public Text(string value, Style? style = null)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
        _style = style ?? Style.Default;
    }

    public string Value => _value;

    public Style Style => _style;

    public void SetText(string value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void SetStyle(Style style)
    {
        _style = style;
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        if (maxWidth <= 0)
        {
            return new Measurement(0, 0);
        }

        var maxLine = 0;
        var currentLine = 0;
        var maxWord = 0;
        var currentWord = 0;
        var height = 1;

        var enumerator = new RuneEnumerator(_value);
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
                height++;
                continue;
            }

            var width = UnicodeWidth.GetWidth(rune);
            currentLine += width;
            if (maxWidth > 0 && lineWidthWouldWrap(currentLine, width, maxWidth))
            {
                height++;
                currentLine = width;
            }

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

        maxLine = Math.Max(maxLine, currentLine);
        maxWord = Math.Max(maxWord, currentWord);

        var minMeasuredWidth = Math.Min(maxWidth, maxWord);
        var maxMeasuredWidth = Math.Min(maxWidth, Math.Max(maxLine, maxWord));
        return new Measurement(minMeasuredWidth, maxMeasuredWidth, height);

        static bool lineWidthWouldWrap(int currentLine, int width, int maxWidth)
            => currentLine > width && currentLine > maxWidth;
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var lineStart = 0;
        var lineWidth = 0;
        var pos = 0;

        var enumerator = new RuneEnumerator(_value);
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
                    output.Write(_value.AsSpan(lineStart, pos - lineStart), _style);
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
                output.Write(_value.AsSpan(lineStart, pos - lineStart), _style);
                output.WriteLineBreak();
                lineStart = pos;
                lineWidth = 0;
            }

            lineWidth += width;
            pos += runeLength;
        }

        if (pos > lineStart)
        {
            output.Write(_value.AsSpan(lineStart, pos - lineStart), _style);
        }
    }

    public bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }
}
