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

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => _separator.Measure(in context, constraints);

    public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.Render(_separator, in context, output.MaxWidth);

    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    public static SeparatorBlock Create(string? title = null) => new(title);
}
