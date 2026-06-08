using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class TerminalGridTests
{
    [Fact]
    public void Write_WritesAsciiCells()
    {
        using var grid = new TerminalGrid(8, 2);

        Assert.True(grid.Write("Hello", Style.Default));

        Assert.Equal(new Rune('H'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune('o'), grid.GetCell(4, 0).Rune);
    }

    [Fact]
    public void Write_MarksSecondCellOfWideRuneAsContinuation()
    {
        using var grid = new TerminalGrid(8, 2);

        Assert.True(grid.Write("A😀B", Style.Default));

        Assert.Equal(new Rune('A'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune(0x1F600), grid.GetCell(1, 0).Rune);
        Assert.True(grid.GetCell(2, 0).IsContinuation);
        Assert.Equal(new Rune('B'), grid.GetCell(3, 0).Rune);
    }

    [Fact]
    public void Write_WrapsAtGridWidth()
    {
        using var grid = new TerminalGrid(3, 2);

        Assert.True(grid.Write("abcd", Style.Default));

        Assert.Equal(new Rune('a'), grid.GetCell(0, 0).Rune);
        Assert.Equal(new Rune('c'), grid.GetCell(2, 0).Rune);
        Assert.Equal(new Rune('d'), grid.GetCell(0, 1).Rune);
    }

    [Fact]
    public void AnsiRenderer_WritesFullGrid()
    {
        using var grid = new TerminalGrid(2, 1);
        using var output = new AnsiFrameWriter();

        Assert.True(grid.Write("Hi", Style.Default));
        AnsiGridRenderer.WriteFull(grid, output);

        Assert.Contains("Hi", output.ToString());
    }

    [Fact]
    public void AnsiRenderer_DefaultForeground_DoesNotEmitBlackRgb()
    {
        using var grid = new TerminalGrid(2, 1);
        using var output = new AnsiFrameWriter();

        Assert.True(grid.Write("Hi", Style.Default));
        AnsiGridRenderer.WriteFull(grid, output);
        var rendered = output.ToString();

        Assert.DoesNotContain("\x1b[38;2;0;0;0m", rendered);
        Assert.Contains("\x1b[0m", rendered);
    }

    [Fact]
    public void AnsiRenderer_WritesOnlyChangedCells()
    {
        using var previous = new TerminalGrid(4, 1);
        using var current = new TerminalGrid(4, 1);
        using var output = new AnsiFrameWriter();

        previous.Write("abcd", Style.Default);
        current.Write("abXd", Style.Default);

        AnsiGridRenderer.WriteDifferential(previous, current, output);
        var rendered = output.ToString();

        Assert.Contains("\x1b[1;3H", rendered);
        Assert.Contains("X", rendered);
        Assert.DoesNotContain("a", rendered);
        Assert.DoesNotContain("b", rendered);
        Assert.DoesNotContain("d", rendered);
    }

    [Fact]
    public void AnsiRenderer_ResetsStyleBetweenChangedCells()
    {
        using var previous = new TerminalGrid(4, 1);
        using var current = new TerminalGrid(4, 1);
        using var output = new AnsiFrameWriter();

        previous.SetCell(2, 0, new Cell(new Rune('C'), Style.Default));
        current.SetCell(0, 0, new Cell(new Rune('A'), new Style(Color.Cyan, Color.Gray)));
        current.SetCell(2, 0, new Cell(new Rune('B'), Style.Default));

        AnsiGridRenderer.WriteDifferential(previous, current, output);
        var rendered = output.ToString();
        var firstReset = rendered.IndexOf("\x1b[0m", StringComparison.Ordinal);
        var defaultCell = rendered.LastIndexOf("B", StringComparison.Ordinal);

        Assert.True(firstReset >= 0);
        Assert.True(defaultCell > firstReset);
    }
}
