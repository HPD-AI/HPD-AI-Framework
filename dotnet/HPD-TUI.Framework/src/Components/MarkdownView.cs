using HPD.TUI.Core;
using HPD.TUI.Markdown;

namespace HPD.TUI.Components;

/// <summary>Renders one immutable Markdown layout without parsing or recomputing layout.</summary>
public sealed class MarkdownView : IComponent
{
    private readonly MarkdownLayout _layout;

    /// <summary>Creates a view over a prepared layout.</summary>
    public MarkdownView(MarkdownLayout layout) => _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    /// <inheritdoc />
    public Measurement Measure(in RenderContext context, int maxWidth)
    {
        var width = Math.Min(maxWidth, _layout.Key.Width);
        return new(Math.Min(width, 1), width, _layout.Height);
    }

    /// <inheritdoc />
    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        if (maxWidth != _layout.Key.Width || context.Theme.Key != _layout.Key.ThemeKey || context.ColorSystem != _layout.Key.ColorSystem)
            throw new InvalidOperationException("MarkdownView render context does not match its prepared layout key.");

        for (var row = 0; row < _layout.Rows.Length && output.CursorY < context.Height; row++)
        {
            if (row > 0 && !output.WriteLineBreak()) break;
            foreach (var run in _layout.Rows[row].Line.Runs)
                output.Write(run.Text, run.Style, new TerminalRunMetadata(run.Hyperlink));
        }
    }

    /// <inheritdoc />
    public bool HandleInput(in TuiInputEvent input) => false;
}
