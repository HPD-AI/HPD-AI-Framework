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
        var writer = new DisplayListBuilder(grid, grid.Width);

        overlay.Render(in context, ref writer);

        Assert.Equal(new Rune('X'), grid.GetLeadingRune(grid.GetCell(3, 1)));
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
        var writer = new DisplayListBuilder(grid, grid.Width);

        overlay.Render(in context, ref writer);

        Assert.Equal(new Rune('X'), grid.GetLeadingRune(grid.GetCell(2, 3)));
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
        var writer = new DisplayListBuilder(grid, grid.Width);

        overlay.Render(in context, ref writer);

        Assert.Equal(new Rune('4'), grid.GetLeadingRune(grid.GetCell(0, 0)));
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
        var writer = new DisplayListBuilder(grid, grid.Width);
        writer.MoveTo(0, 1);
        writer.Write("abcdefgh".AsSpan(), Theme.Default.Text);
        writer.MoveTo(0, 2);
        writer.Write("ABCDEFGH".AsSpan(), Theme.Default.Text);

        overlay.Render(in context, ref writer);

        Assert.Equal(new Rune('a'), grid.GetLeadingRune(grid.GetCell(0, 1)));
        Assert.Equal(new Rune('X'), grid.GetLeadingRune(grid.GetCell(1, 1)));
        Assert.Equal(new Rune(' '), grid.GetLeadingRune(grid.GetCell(2, 1)));
        Assert.Equal(new Rune(' '), grid.GetLeadingRune(grid.GetCell(4, 1)));
        Assert.Equal(new Rune('f'), grid.GetLeadingRune(grid.GetCell(5, 1)));
        Assert.Equal(new Rune('A'), grid.GetLeadingRune(grid.GetCell(0, 2)));
        Assert.Equal(new Rune(' '), grid.GetLeadingRune(grid.GetCell(1, 2)));
        Assert.Equal(new Rune(' '), grid.GetLeadingRune(grid.GetCell(4, 2)));
        Assert.Equal(new Rune('F'), grid.GetLeadingRune(grid.GetCell(5, 2)));
    }

    [Fact]
    public void OverlayHost_RendersOverlayAfterContent()
    {
        var host = new OverlayHost(new Text("abc"));
        host.Push(new Overlay(new Text("Z"), x: 1, y: 0, width: 2));
        var context = new RenderContext(8, 3, Theme.Default);
        using var grid = new TerminalGrid(8, 3);
        var writer = new DisplayListBuilder(grid, grid.Width);

        host.Render(in context, ref writer);

        Assert.Equal(new Rune('a'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune('Z'), grid.GetLeadingRune(grid.GetCell(1, 0)));
        Assert.Equal(new Rune('c'), grid.GetLeadingRune(grid.GetCell(2, 0)));
    }

    private sealed class ContextHeightComponent : Component
    {
        public override Measurement Measure(in RenderContext context, HPD.TUI.Layout.LayoutConstraints constraints)
            => new(1, 1);

        public override void Render(in RenderContext context, ref DisplayListBuilder output)
            => output.Write(context.Height.ToString().AsSpan(), context.Theme.Text);

        public override bool HandleInput(in TuiInputEvent key)
        {
            return false;
        }
    }
}
