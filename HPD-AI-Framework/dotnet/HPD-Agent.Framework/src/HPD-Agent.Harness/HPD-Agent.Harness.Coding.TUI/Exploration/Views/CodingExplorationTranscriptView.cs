using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.Agent.ToolHarness.Coding.TUI.Exploration.Views;

internal sealed class CodingExplorationTranscriptView : IComponent
{
    private readonly CodingExplorationGroup _group;

    public CodingExplorationTranscriptView(CodingExplorationGroup group)
    {
        _group = group ?? throw new ArgumentNullException(nameof(group));
    }

    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var rows = CodingExplorationDisplayFormatter.BuildRows(_group);
        return rows.Count == 0
            ? new Measurement(0, 0, 0)
            : new Measurement(1, Math.Min(maxWidth, 100), rows.Count);
    }

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth <= 0)
        {
            return;
        }

        var rows = CodingExplorationDisplayFormatter.BuildRows(_group);
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                output.WriteLineBreak();
            }

            var prefix = i == 0 ? "  └ " : "    ";
            output.Write(prefix.AsSpan(), context.Theme.Border);
            WriteWrapped(rows[i], prefix.Length, maxWidth, context.Theme.Text, ref output);
        }
    }

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate()
    {
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
        var current = "";
        var currentWidth = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeWidth = Math.Max(0, UnicodeWidth.GetWidth(rune));
            if (current.Length > 0 && currentWidth + runeWidth > width)
            {
                rows.Add(current);
                current = "";
                currentWidth = 0;
            }

            current += runeText;
            currentWidth += runeWidth;
        }

        rows.Add(current);
        return rows;
    }
}
