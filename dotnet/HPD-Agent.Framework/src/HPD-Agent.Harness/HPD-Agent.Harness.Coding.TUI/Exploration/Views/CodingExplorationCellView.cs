using System.Text;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Views;

internal sealed class CodingExplorationCellView : IComponent
{
    private readonly CodingExplorationCell _cell;
    private readonly CodingHarnessTuiTheme _theme;

    public CodingExplorationCellView(CodingExplorationCell cell, CodingHarnessTuiTheme theme)
    {
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        return _cell.Rows.Count == 0
            ? new Measurement(0, 0, 0)
            : new Measurement(1, Math.Min(maxWidth, 100), _cell.Rows.Count);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        for (var i = 0; i < _cell.Rows.Count; i++)
        {
            if (i > 0)
            {
                output.WriteLineBreak();
            }

            var prefix = i == 0 ? "  └ " : "    ";
            output.Write(prefix.AsSpan(), _theme.ResolvePrefix(context.Theme));
            WriteWrapped(_cell.Rows[i], prefix.Length, maxWidth, _theme.ResolveText(context.Theme), ref output);
        }
    }

    public bool HandleInput(in TuiInputEvent input)
    {
        return false;
    }

    private static void WriteWrapped(
        string text,
        int prefixWidth,
        int maxWidth,
        Style style,
        ref SegmentWriter output)
    {
        var contentWidth = Math.Max(1, maxWidth - prefixWidth);
        var rows = Wrap(text, contentWidth);
        output.Write(rows[0].AsSpan(), style);
        for (var i = 1; i < rows.Count; i++)
        {
            output.WriteLineBreak();
            output.Write(new string(' ', prefixWidth).AsSpan(), style);
            output.Write(rows[i].AsSpan(), style);
        }
    }

    private static IReadOnlyList<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [""];
        }

        var rows = new List<string>();
        var current = new StringBuilder();
        var currentWidth = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeWidth = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (current.Length > 0 && currentWidth + runeWidth > width)
            {
                rows.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            current.Append(runeText);
            currentWidth += runeWidth;
        }

        rows.Add(current.ToString());
        return rows;
    }
}
