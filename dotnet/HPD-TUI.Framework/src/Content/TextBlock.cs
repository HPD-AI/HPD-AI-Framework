using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.TUI.Content;

public sealed class TextBlock : Component, IContentBlock
{
    private readonly Text _component;

    public TextBlock(string text, Style? style = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Style = style;
        _component = new Text(Text, style);
    }

    public ContentBlockKind Kind => ContentBlockKind.Text;

    public string Text { get; private set; }

    public Style? Style { get; private set; }

    public void SetText(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _component.SetText(Text);
    }

    public void SetStyle(Style style)
    {
        Style = style;
        _component.SetStyle(style);
    }

    public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints) => _component.Measure(in context, constraints);

    public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.Render(_component, in context, output.MaxWidth);

    public override bool HandleInput(in TuiInputEvent key)
    {
        return false;
    }

    public static TextBlock Create(string text, Style? style = null) => new(text, style);
}
