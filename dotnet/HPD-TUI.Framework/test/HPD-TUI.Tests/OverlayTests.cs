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
    public void Overlay_RendersBottomPlacedChild()
    {
        var overlay = new Overlay(
            new Text("X"),
            x: 2,
            y: 1,
            width: 5,
            height: 2,
            verticalPlacement: OverlayVerticalPlacement.Bottom);
        var context = new RenderContext(8, 6, Theme.Default);
        using var grid = new TerminalGrid(8, 6);
        var writer = new SegmentWriter(grid);

        overlay.Render(in context, 8, ref writer);

        Assert.Equal(new Rune('X'), grid.GetCell(2, 3).Rune);
    }

    [Fact]
    public void Overlay_PassesBoundedHeightToChild()
    {
        var overlay = new Overlay(
            new ContextHeightComponent(),
            x: 0,
            y: 0,
            width: 5,
            height: 4);
        var context = new RenderContext(8, 10, Theme.Default);
        using var grid = new TerminalGrid(8, 10);
        var writer = new SegmentWriter(grid);

        overlay.Render(in context, 8, ref writer);

        Assert.Equal(new Rune('4'), grid.GetCell(0, 0).Rune);
    }

    [Fact]
    public void Overlay_ClearsBackgroundWhenConfigured()
    {
        var overlay = new Overlay(
            new Text("X"),
            x: 1,
            y: 1,
            width: 4,
            height: 2,
            clearBackground: true);
        var context = new RenderContext(8, 4, Theme.Default);
        using var grid = new TerminalGrid(8, 4);
        var writer = new SegmentWriter(grid);
        writer.MoveTo(0, 1);
        writer.Write("abcdefgh".AsSpan(), Theme.Default.Text);
        writer.MoveTo(0, 2);
        writer.Write("ABCDEFGH".AsSpan(), Theme.Default.Text);

        overlay.Render(in context, 8, ref writer);

        Assert.Equal(new Rune('a'), grid.GetCell(0, 1).Rune);
        Assert.Equal(new Rune('X'), grid.GetCell(1, 1).Rune);
        Assert.Equal(new Rune(' '), grid.GetCell(2, 1).Rune);
        Assert.Equal(new Rune(' '), grid.GetCell(4, 1).Rune);
        Assert.Equal(new Rune('f'), grid.GetCell(5, 1).Rune);
        Assert.Equal(new Rune('A'), grid.GetCell(0, 2).Rune);
        Assert.Equal(new Rune(' '), grid.GetCell(1, 2).Rune);
        Assert.Equal(new Rune(' '), grid.GetCell(4, 2).Rune);
        Assert.Equal(new Rune('F'), grid.GetCell(5, 2).Rune);
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

    private sealed class ContextHeightComponent : IComponent
    {
        public Measurement Measure(in RenderContext context, int maxWidth)
            => new(1, 1);

        public void Render(in RenderContext context, int maxWidth, ref SegmentWriter output)
            => output.Write(context.Height.ToString().AsSpan(), context.Theme.Text);

        public void HandleInput(in KeyEvent key)
        {
        }

        public void Invalidate()
        {
        }
    }
}
