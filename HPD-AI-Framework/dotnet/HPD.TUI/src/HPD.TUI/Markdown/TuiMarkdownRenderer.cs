using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.TUI.Markdown;

public sealed class TuiMarkdownRenderer : IMarkdownRenderer<IComponent>
{
    private readonly Theme? _theme;

    public TuiMarkdownRenderer(Theme? theme = null)
    {
        _theme = theme;
    }

    public IComponent Render(string markdown) => new Components.Markdown(markdown, _theme);
}
