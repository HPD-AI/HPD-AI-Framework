using HPD.TUI.Core;
using HPD.TUI.Layout;

namespace HPD.TUI.Content;

public sealed class SeparatorBlock : Component, IContentBlock
{
    private readonly Separator _separator;

    public SeparatorBlock(string? title = null)
    {
        Title = title;
        _separator = new Separator(title);
    }

    public ContentBlockKind Kind => ContentBlockKind.Separator;

    public string? Title { get; }

    public override Measurement Measure(in RenderContext context, int maxWidth) => _separator.Measure(in context, maxWidth);

    public override void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => _separator.Render(in context, maxWidth, ref output);

    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    public static SeparatorBlock Create(string? title = null) => new(title);
}
