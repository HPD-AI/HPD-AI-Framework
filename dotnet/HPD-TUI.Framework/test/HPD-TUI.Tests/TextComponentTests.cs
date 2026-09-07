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

        var measurement = text.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(20, context.Height));

        Assert.Equal(8, measurement.MinWidth);
        Assert.Equal(14, measurement.MaxWidth);
    }

    [Fact]
    public void Render_WrapsAtMaxWidth()
    {
        var text = new Text("abcdef");
        var context = new RenderContext(3, 3, Theme.Default);
        using var grid = new TerminalGrid(3, 3);
        var writer = new DisplayListBuilder(grid, grid.Width);

        text.Render(in context, ref writer);

        Assert.Equal(new Rune('a'), grid.GetLeadingRune(grid.GetCell(0, 0)));
        Assert.Equal(new Rune('c'), grid.GetLeadingRune(grid.GetCell(2, 0)));
        Assert.Equal(new Rune('d'), grid.GetLeadingRune(grid.GetCell(0, 1)));
    }

    [Fact]
    public void Measure_LongUnbrokenTextInsideNarrowWidth_DoesNotReportMinGreaterThanMax()
    {
        var text = new Text("cmd ok find . -not -path './bin/*' -not -path './obj/*' -type f");
        var context = new RenderContext(24, 5, Theme.Default);

        var measurement = text.Measure(in context, HPD.TUI.Layout.LayoutConstraints.Loose(24, context.Height));

        Assert.True(measurement.MinWidth <= measurement.MaxWidth);
        Assert.InRange(measurement.MaxWidth, 0, 24);
    }

    [Fact]
    public void Frame_RendersMultilineChildWithSideBordersOnEveryRow()
    {
        var frame = Frame.Create(new Text("a\nb"));
        var context = new RenderContext(5, 4, Theme.Default);
        using var grid = new TerminalGrid(5, 4);
        var writer = new DisplayListBuilder(grid, grid.Width);

        frame.Render(in context, ref writer);

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
            buffer[x] = (char)grid.GetLeadingRune(grid.GetCell(x, y)).Value;
        }

        return new string(buffer);
    }
}
