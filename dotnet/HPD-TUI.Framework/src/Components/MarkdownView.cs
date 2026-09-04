using HPD.TUI.Core;
using HPD.TUI.Markdown;

namespace HPD.TUI.Components;

/// <summary>Renders one immutable Markdown layout without parsing or recomputing layout.</summary>
public sealed class MarkdownView : Component
{
    private MarkdownLayout _layout;
    private readonly Func<int, MarkdownLayout>? _loadRawPage;
    private readonly Stack<MarkdownLayout> _previousPages = [];

    /// <summary>Creates a view over a prepared layout.</summary>
    public MarkdownView(MarkdownLayout layout) => _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    /// <summary>Creates a view that can disclose bounded raw continuation pages with PageDown/PageUp.</summary>
    public MarkdownView(MarkdownLayout layout, Func<int, MarkdownLayout> loadRawPage)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _loadRawPage = loadRawPage ?? throw new ArgumentNullException(nameof(loadRawPage));
    }

    /// <inheritdoc />
    public override Measurement Measure(in RenderContext context, int maxWidth)
    {
        Validate(in context, maxWidth);
        return new(Math.Min(_layout.Key.Width, 1), _layout.Key.Width, _layout.Height);
    }

    /// <inheritdoc />
    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
    {
        Validate(in context, maxWidth);

        for (var row = 0; row < _layout.Rows.Length && output.CursorY < context.Height; row++)
        {
            if (row > 0 && !output.WriteLineBreak()) break;
            foreach (var run in _layout.Rows[row].Line.Runs)
                output.Write(run.Text, run.Style, new TerminalRunMetadata(run.Hyperlink));
        }
    }

    /// <inheritdoc />
    public override bool HandleInput(in TuiInputEvent input)
    {
        if (input.Key == KeyCode.PageDown && _layout.NextSourceOffset is { } offset && _loadRawPage is not null)
        {
            _previousPages.Push(_layout);
            _layout = _loadRawPage(offset);
            return true;
        }
        if (input.Key == KeyCode.PageUp && _previousPages.TryPop(out var previous))
        {
            _layout = previous;
            return true;
        }
        return false;
    }

    private void Validate(in RenderContext context, int maxWidth)
    {
        if (maxWidth != _layout.Key.Width || context.Theme.Key != _layout.Key.ThemeKey || context.ColorSystem != _layout.Key.ColorSystem)
            throw new InvalidOperationException(
                $"MarkdownView context does not match its prepared layout key. " +
                $"Width actual/prepared={maxWidth}/{_layout.Key.Width}; " +
                $"themeMatch={context.Theme.Key == _layout.Key.ThemeKey}; " +
                $"colorSystem actual/prepared={context.ColorSystem}/{_layout.Key.ColorSystem}.");
    }
}
