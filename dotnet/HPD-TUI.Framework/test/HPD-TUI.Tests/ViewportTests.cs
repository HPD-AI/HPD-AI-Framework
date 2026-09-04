using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class ViewportTests
{
    [Fact]
    public void Render_ShowsScrolledWindow()
    {
        var viewport = new Viewport(height: 2);
        viewport.AddLine("one");
        viewport.AddLine("two");
        viewport.AddLine("three");
        viewport.ScrollBy(1);
        var context = new RenderContext(10, 3, Theme.Default);
        using var grid = new TerminalGrid(10, 3);
        var writer = new DisplayListBuilder(grid, grid.Width);

        viewport.Render(in context, ref writer);

        Assert.Equal(new Rune('t'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune('t'), grid.GetLeadingRune(grid.GetCell(0, 1)));
        Assert.Equal(1, viewport.ScrollOffset);
    }

    [Fact]
    public void HandleInput_PageDownScrolls()
    {
        var viewport = new Viewport(height: 2);
        viewport.AddLine("one");
        viewport.AddLine("two");
        viewport.AddLine("three");
        viewport.AddLine("four");

        viewport.HandleInput(new KeyEvent(KeyCode.PageDown));

        Assert.Equal(2, viewport.ScrollOffset);
    }
}
