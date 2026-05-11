using HPD.TUI.Core;

namespace HPD.TUI.Content;

public sealed class MarkupParser
{
    private readonly Theme _theme;

    public MarkupParser(Theme? theme = null)
    {
        _theme = theme ?? Theme.Default;
    }

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    private readonly List<string> _diagnostics = [];

    public StyledTextRun[] Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _diagnostics.Clear();
        if (source.Length == 0)
        {
            return [];
        }

        var runs = new List<StyledTextRun>();
        var buffer = new StringBuilder(source.Length);
        var styles = new Stack<Style>();
        var current = _theme.Text;
        var index = 0;

        while (index < source.Length)
        {
            var ch = source[index];
            if (ch == '[')
            {
                if (index + 1 < source.Length && source[index + 1] == '[')
                {
                    buffer.Append('[');
                    index += 2;
                    continue;
                }

                var close = FindClosingBracket(source, index + 1);
                if (close < 0)
                {
                    buffer.Append(ch);
                    index++;
                    continue;
                }

                var tag = source.AsSpan(index + 1, close - index - 1);
                if (tag.SequenceEqual("/".AsSpan()))
                {
                    Flush(runs, buffer, current);
                    if (styles.Count == 0)
                    {
                        _diagnostics.Add("Reset tag has no matching style tag.");
                        current = _theme.Text;
                    }
                    else
                    {
                        current = styles.Pop();
                    }

                    index = close + 1;
                    continue;
                }

                if (TryApplyTag(tag, current, out var next))
                {
                    Flush(runs, buffer, current);
                    styles.Push(current);
                    current = next;
                    index = close + 1;
                    continue;
                }

                buffer.Append(source.AsSpan(index, close - index + 1));
                _diagnostics.Add($"Unknown markup tag '{tag.ToString()}'.");
                index = close + 1;
                continue;
            }

            if (ch == ']' && index + 1 < source.Length && source[index + 1] == ']')
            {
                buffer.Append(']');
                index += 2;
                continue;
            }

            buffer.Append(ch);
            index++;
        }

        if (styles.Count > 0)
        {
            _diagnostics.Add("One or more style tags were not closed.");
        }

        Flush(runs, buffer, current);
        return runs.ToArray();
    }

    private static int FindClosingBracket(string source, int start)
    {
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == ']')
            {
                return i;
            }
        }

        return -1;
    }

    private bool TryApplyTag(ReadOnlySpan<char> tag, Style current, out Style style)
    {
        style = current;
        if (tag.SequenceEqual("bold".AsSpan()))
        {
            style = current with { Attributes = current.Attributes | TextAttributes.Bold };
            return true;
        }

        if (tag.SequenceEqual("italic".AsSpan()))
        {
            style = current with { Attributes = current.Attributes | TextAttributes.Italic };
            return true;
        }

        if (tag.SequenceEqual("underline".AsSpan()))
        {
            style = current with { Attributes = current.Attributes | TextAttributes.Underline };
            return true;
        }

        if (tag.SequenceEqual("dim".AsSpan()) || tag.SequenceEqual("gray".AsSpan()))
        {
            style = current with { Foreground = Color.Gray };
            return true;
        }

        if (tag.SequenceEqual("red".AsSpan()))
        {
            style = current with { Foreground = _theme.Error.Foreground };
            return true;
        }

        if (tag.SequenceEqual("green".AsSpan()))
        {
            style = current with { Foreground = _theme.Success.Foreground };
            return true;
        }

        if (tag.SequenceEqual("yellow".AsSpan()))
        {
            style = current with { Foreground = _theme.Warning.Foreground };
            return true;
        }

        if (tag.SequenceEqual("cyan".AsSpan()))
        {
            style = current with { Foreground = _theme.Accent.Foreground };
            return true;
        }

        if (tag.SequenceEqual("blue".AsSpan()))
        {
            style = current with { Foreground = _theme.Blue.Foreground };
            return true;
        }

        return false;
    }

    private static void Flush(List<StyledTextRun> runs, StringBuilder buffer, Style style)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        runs.Add(new StyledTextRun(buffer.ToString(), style));
        buffer.Clear();
    }
}
