using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class DisplayCommandTests
{
    [Fact]
    public void FillBorderAndClip_RasterizeThroughRetainedCommands()
    {
        using var grid = TuiCapture.RenderToGrid(new StructuredComponent(), 8, 4);

        Assert.Equal('x', (char)grid.GetLeadingRune(grid.GetCell(1, 2)).Value);
        Assert.Equal('#', (char)grid.GetLeadingRune(grid.GetCell(2, 1)).Value);
        Assert.Equal(' ', (char)grid.GetLeadingRune(grid.GetCell(6, 1)).Value);
    }

    [Fact]
    public void ReplaySurface_PreservesSemanticCells()
    {
        var context = new RenderContext(4, 2, Theme.Default);
        using var surface = new TuiSurface(4, 2);
        surface.Capture(new Text("ok"), in context);
        using var grid = TuiCapture.RenderToGrid(new SurfaceComponent(surface), 8, 4);

        Assert.Equal('o', (char)grid.GetLeadingRune(grid.GetCell(2, 1)).Value);
        Assert.Equal('k', (char)grid.GetLeadingRune(grid.GetCell(3, 1)).Value);
    }

    private sealed class StructuredComponent : Component
    {
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(8, 8, 4);
        public override void Render(in RenderContext context, ref DisplayListBuilder output)
        {
            output.Fill(new LayoutRect(0, 0, 5, 3), 'x', context.Theme.Text);
            output.PushClip(new LayoutRect(1, 1, 2, 1));
            output.Border(new LayoutRect(1, 1, 5, 2), context.Theme.Accent, '#');
            output.PopClip();
        }
    }

    private sealed class SurfaceComponent(TuiSurface surface) : Component
    {
        public override ComponentDependencies Dependencies => new(RenderContextFields.None, RenderContextFields.None);
        public override Measurement Measure(in RenderContext context, LayoutConstraints constraints) => new(8, 8, 4);
        public override void Render(in RenderContext context, ref DisplayListBuilder output) => output.ReplaySurface(surface, 2, 1);
    }
}
