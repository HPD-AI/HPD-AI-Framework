using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using System.Text;

namespace HPD.TUI.Tests;

public sealed class FixedViewportTests
{
    [Fact]
    public void Render_ClaimsAndClearsEveryOwnedRow()
    {
        var viewport = new FixedViewport(new Text("content"), height: 4);

        using var grid = TuiCapture.RenderToGrid(viewport, width: 20, height: 8);

        Assert.Equal(3, grid.CursorY);
        Assert.Equal(new Rune('c'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune(' '), grid.GetLeadingRune(grid.GetCell(0, 3)));
    }

    [Fact]
    public void Measure_ReportsExactConfiguredHeight()
    {
        var viewport = new FixedViewport(new Text("content"), height: 4);
        var context = new RenderContext(20, 8, Theme.Default);

        var measurement = viewport.Measure(in context,
            HPD.TUI.Layout.LayoutConstraints.Loose(20, 8));

        Assert.Equal(4, measurement.Height);
    }
}
