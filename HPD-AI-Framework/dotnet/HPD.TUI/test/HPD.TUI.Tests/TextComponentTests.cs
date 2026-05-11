using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class TextComponentTests
{
    [Fact]
    public void Measure_UsesLongestWordAsMinimum()
    {
        var text = new Text("small enormous");
        var context = new RenderContext(20, 5, Theme.Default);

        var measurement = text.Measure(in context, 20);

        Assert.Equal(8, measurement.MinWidth);
        Assert.Equal(14, measurement.MaxWidth);
    }

    [Fact]
    public void Render_WrapsAtMaxWidth()
    {
        var text = new Text("abcdef");
        var context = new RenderContext(3, 3, Theme.Default);
        using var grid = new TerminalGrid(3, 3);
        var writer = new SegmentWriter(grid);

        text.Render(in context, 3, ref writer);

        Assert.Equal(new Rune('a'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune('c'), grid.GetCell(2, 0).Rune);
        Assert.Equal(new Rune('d'), grid.GetCell(0, 1).Rune);
    }

    [Fact]
    public void Frame_RendersMultilineChildWithSideBordersOnEveryRow()
    {
        var frame = Frame.Create(new Text("a\nb"));
        var context = new RenderContext(5, 4, Theme.Default);
        using var grid = new TerminalGrid(5, 4);
        var writer = new SegmentWriter(grid);

        frame.Render(in context, 5, ref writer);

        Assert.Equal("┌───┐", ReadLine(grid, 0));
        Assert.Equal("│a  │", ReadLine(grid, 1));
        Assert.Equal("│b  │", ReadLine(grid, 2));
        Assert.Equal("└───┘", ReadLine(grid, 3));
    }

    private static string ReadLine(TerminalGrid grid, int y)
    {
        Span<char> buffer = stackalloc char[grid.Width];
        for (var x = 0; x < grid.Width; x++)
        {
            buffer[x] = (char)grid.GetCell(x, y).Rune.Value;
        }

        return new string(buffer);
    }
}
