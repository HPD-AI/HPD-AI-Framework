using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class AllocationTests
{
    [Fact]
    public void TerminalGrid_WriteAndAnsiOutput_DoNotAllocateAfterWarmup()
    {
        using var grid = new TerminalGrid(20, 4);
        using var output = new AnsiFrameWriter();

        grid.Write("warmup", Style.Default);
        AnsiGridRenderer.WriteFull(grid, output);
        output.Clear();
        grid.Clear();

        var before = GC.GetAllocatedBytesForCurrentThread();

        grid.Write("Hello 😀", Style.Default);
        grid.WriteLineBreak();
        grid.Write("World", Style.Default);
        AnsiGridRenderer.WriteFull(grid, output);

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }
}
