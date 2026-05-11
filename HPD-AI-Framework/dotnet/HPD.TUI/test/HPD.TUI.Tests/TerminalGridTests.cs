using HPD.TUI.Core;
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
    public void WriteAnsi_WritesIntoCallerBuffer()
    {
        using var grid = new TerminalGrid(2, 1);
        Span<char> buffer = stackalloc char[128];

        Assert.True(grid.Write("Hi", Style.Default));
        var written = grid.WriteAnsi(buffer);

        Assert.True(written > 0);
        Assert.Contains("Hi", new string(buffer[..written]));
    }

    [Fact]
    public void WriteDifferentialAnsi_WritesOnlyChangedCells()
    {
        using var previous = new TerminalGrid(4, 1);
        using var current = new TerminalGrid(4, 1);
        Span<char> buffer = stackalloc char[256];

        previous.Write("abcd", Style.Default);
        current.Write("abXd", Style.Default);

        var written = current.WriteDifferentialAnsi(previous, buffer);
        var output = new string(buffer[..written]);

        Assert.Contains("\x1b[1;3H", output);
        Assert.Contains("X", output);
        Assert.DoesNotContain("a", output);
        Assert.DoesNotContain("b", output);
        Assert.DoesNotContain("d", output);
    }
}
