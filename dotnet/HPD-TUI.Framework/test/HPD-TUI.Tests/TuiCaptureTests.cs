using HPD.TUI.Components;
using HPD.TUI.Content;
using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class TuiCaptureTests
{
    [Fact]
    public void RenderToLines_CapturesFixedSizePlainText()
    {
        var lines = TuiCapture.RenderToLines(new Text("hi"), width: 4, height: 2);

        Assert.Equal(["hi  ", "    "], lines);
    }

    [Fact]
    public void RenderToString_CanTrimTrailingBlankLines()
    {
        var text = TuiCapture.RenderToString(new Text("hi"), width: 4, height: 3, trimTrailingBlankLines: true);

        Assert.Equal("hi  ", text);
    }

    [Fact]
    public void RenderToGrid_PreservesStyles()
    {
        var style = new Style(Color.Red, Color.Black, TextAttributes.Bold);
        using var grid = TuiCapture.RenderToGrid(TextBlock.Create("x", style), width: 2, height: 1);

        Assert.Equal(style, grid.GetCell(0, 0).Style);
    }

    [Fact]
    public void GetUsedLineCount_TrimsUnusedBlankRowsButKeepsAtLeastOne()
    {
        using var grid = TuiCapture.RenderToGrid(new Text("a\nb"), width: 4, height: 5);

        Assert.Equal(2, TuiCapture.GetUsedLineCount(grid));
    }

    [Fact]
    public void GetUsedLineCount_PreservesStyledBlankRows()
    {
        var background = new Color(10, 20, 30);
        using var grid = TuiCapture.RenderToGrid(
            TextBlock.Create("x\n    ", new Style(Color.Default, background)),
            width: 4,
            height: 4);

        Assert.Equal(2, TuiCapture.GetUsedLineCount(grid));
    }

    [Fact]
    public void GetUsedLineCount_UsesSemanticCellsAndTerminalCursorInsteadOfRasterWriteHead()
    {
        using var grid = new Terminal.TerminalGrid(8, 6);
        grid.MoveTo(0, 4);
        grid.Write("x", Style.Default);
        grid.MoveTo(0, 1); // Simulates the final operation in a damage-only replay.

        Assert.Equal(5, TuiCapture.GetUsedLineCount(grid));

        grid.Clear();
        grid.MoveTo(0, 1); // A transient write head must not extend an otherwise blank screen.
        grid.SetTerminalCursor(3, 5);

        Assert.Equal(6, TuiCapture.GetUsedLineCount(grid));
    }

    [Fact]
    public void RenderToAnsi_CapturesAnsiOutput()
    {
        var ansi = TuiCapture.RenderToAnsi(new Text("x"), width: 1, height: 1);

        Assert.Contains("\x1b[0m", ansi);
        Assert.Contains("x", ansi);
    }
}
