using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.TUI.Content;

public sealed class TextBlock : IContentBlock
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

    public Measurement Measure(in RenderContext context, int maxWidth) => _component.Measure(in context, maxWidth);

    public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output) => _component.Render(in context, maxWidth, ref output);

    public void HandleInput(in KeyEvent key)
    {
    }

    public void Invalidate() => _component.Invalidate();

    public static TextBlock Create(string text, Style? style = null) => new(text, style);
}
