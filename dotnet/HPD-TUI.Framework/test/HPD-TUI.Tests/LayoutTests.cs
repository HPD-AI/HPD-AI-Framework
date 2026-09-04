using HPD.TUI.Components;
using HPD.TUI.Core;
using HPD.TUI.Layout;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class LayoutTests
{
    [Fact]
    public void Separator_RendersRuleWithoutTitle()
    {
        var separator = new Separator();
        var context = new RenderContext(6, 1, Theme.Default);
        using var grid = new TerminalGrid(6, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        separator.Render(in context, ref writer);

        Assert.Equal("──────", ReadLine(grid, 0));
    }

    [Fact]
    public void Separator_RendersCenteredTitle()
    {
        var separator = new Separator("A");
        var context = new RenderContext(7, 1, Theme.Default);
        using var grid = new TerminalGrid(7, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        separator.Render(in context, ref writer);

        Assert.Equal("── A ──", ReadLine(grid, 0));
    }

    [Fact]
    public void Stack_RendersChildrenVertically()
    {
        var stack = new Stack()
            .Add(new Text("one"))
            .Add(new Text("two"));
        var context = new RenderContext(5, 2, Theme.Default);
        using var grid = new TerminalGrid(5, 2);
        var writer = new DisplayListBuilder(grid, grid.Width);

        stack.Render(in context, ref writer);

        Assert.Equal("one  ", ReadLine(grid, 0));
        Assert.Equal("two  ", ReadLine(grid, 1));
    }

    [Fact]
    public void Stack_HorizontalAllocatesRemainingWidthToChildren()
    {
        var stack = new Stack(Orientation.Horizontal) { Gap = 1 }
            .Add(new Text("abcdef"))
            .Add(new Text("xy"));
        var context = new RenderContext(8, 1, Theme.Default);
        using var grid = new TerminalGrid(8, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        stack.Render(in context, ref writer);

        Assert.Equal("abcdef x", ReadLine(grid, 0));
    }

    [Fact]
    public void Grid_RendersFixedAndFillColumns()
    {
        var gridComponent = new Grid { ColumnGap = 1 }
            .AddColumn(SizePolicy.Fixed(3))
            .AddColumn(SizePolicy.Fill())
            .AddRow(new Text("abc"), new Text("de"));
        var context = new RenderContext(8, 1, Theme.Default);
        using var grid = new TerminalGrid(8, 1);
        var writer = new DisplayListBuilder(grid, grid.Width);

        gridComponent.Render(in context, ref writer);

        Assert.Equal("abc de  ", ReadLine(grid, 0));
    }

    [Fact]
    public void Grid_AdvancesRowsAfterTallCells()
    {
        var gridComponent = new Grid { ColumnGap = 1 }
            .AddColumn(SizePolicy.Fixed(3))
            .AddColumn(SizePolicy.Fill())
            .AddRow(new Text("a\nb"), new Text("x"))
            .AddRow(new Text("c"), new Text("y"));
        var context = new RenderContext(8, 3, Theme.Default);
        using var grid = new TerminalGrid(8, 3);
        var writer = new DisplayListBuilder(grid, grid.Width);

        gridComponent.Render(in context, ref writer);

        Assert.Equal("a   x   ", ReadLine(grid, 0));
        Assert.Equal("b       ", ReadLine(grid, 1));
        Assert.Equal("c   y   ", ReadLine(grid, 2));
    }

    [Fact]
    public void Grid_AppliesCellPaddingAndAlignment()
    {
        var gridComponent = new Grid()
            .AddColumn(new GridColumn(SizePolicy.Fixed(6))
            {
                Padding = new Thickness(1),
                Alignment = Alignment.End
            })
            .AddRow(new Text("x"));

        var lines = TuiCapture.RenderToLines(gridComponent, 6, 3);

        Assert.Equal("      ", lines[0]);
        Assert.Equal("    x ", lines[1]);
        Assert.Equal("      ", lines[2]);
    }

    [Fact]
    public void Grid_FixedRowHeightClipsTallCells()
    {
        var gridComponent = new Grid()
            .AddColumn(SizePolicy.Fixed(3))
            .AddRow(new GridRow([new Text("a\nb\nc")]) { Height = SizePolicy.Fixed(2) })
            .AddRow(new Text("z"));

        var lines = TuiCapture.RenderToLines(gridComponent, 3, 4);

        Assert.Equal("a  ", lines[0]);
        Assert.Equal("b  ", lines[1]);
        Assert.Equal("z  ", lines[2]);
        Assert.Equal("   ", lines[3]);
    }

    [Fact]
    public void LayoutRect_InsetClampsAtZero()
    {
        var rect = new LayoutRect(1, 2, 3, 4).Inset(new Thickness(2));

        Assert.Equal(new LayoutRect(3, 4, 0, 0), rect);
    }

    [Fact]
    public void LayoutConstraints_ClampValues()
    {
        var constraints = new LayoutConstraints(2, 8, 1, 4);

        Assert.Equal(2, constraints.ClampWidth(0));
        Assert.Equal(8, constraints.ClampWidth(20));
        Assert.Equal(3, constraints.ClampHeight(3));
    }

    [Fact]
    public void SizePolicy_FillRejectsZeroWeight()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SizePolicy.Fill(0));
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
