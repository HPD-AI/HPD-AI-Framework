using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class OverlayTests
{
    [Fact]
    public void Overlay_RendersChildAtAbsolutePosition()
    {
        var overlay = new Overlay(new Text("X"), x: 3, y: 1, width: 5);
        var context = new RenderContext(8, 3, Theme.Default);
        using var grid = new TerminalGrid(8, 3);
        var writer = new SegmentWriter(grid);

        overlay.Render(in context, 8, ref writer);

        Assert.Equal(new Rune('X'), grid.GetCell(3, 1).Rune);
    }

    [Fact]
    public void OverlayHost_RendersOverlayAfterContent()
    {
        var host = new OverlayHost(new Text("abc"));
        host.Push(new Overlay(new Text("Z"), x: 1, y: 0, width: 2));
        var context = new RenderContext(8, 3, Theme.Default);
        using var grid = new TerminalGrid(8, 3);
        var writer = new SegmentWriter(grid);

        host.Render(in context, 8, ref writer);

        Assert.Equal(new Rune('a'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune('Z'), grid.GetCell(1, 0).Rune);
        Assert.Equal(new Rune('c'), grid.GetCell(2, 0).Rune);
    }
}
