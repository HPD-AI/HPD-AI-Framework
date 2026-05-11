using HPD.TUI.Core;
using HPD.TUI.Layout;

namespace HPD.TUI.Content;

public sealed class SeparatorBlock : IContentBlock
{
    private readonly Separator _separator;

    public SeparatorBlock(string? title = null)
    {
        Title = title;
        _separator = new Separator(title);
    }

    public ContentBlockKind Kind => ContentBlockKind.Separator;

    public string? Title { get; }

    public Measurement Measure(in RenderContext context, int maxWidth) => _separator.Measure(in context, maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => _separator.Render(in context, maxWidth, ref output);

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate() => _separator.Invalidate();

    public static SeparatorBlock Create(string? title = null) => new(title);
}
