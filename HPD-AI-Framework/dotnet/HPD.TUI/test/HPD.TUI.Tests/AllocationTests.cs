using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Tests;

public sealed class AllocationTests
{
    [Fact]
    public void TerminalGrid_WriteAndAnsiOutput_DoNotAllocateAfterWarmup()
    {
        using var grid = new TerminalGrid(20, 4);
        Span<char> output = stackalloc char[4096];

        grid.Write("warmup", Style.Default);
        grid.WriteAnsi(output);
        grid.Clear();

        var before = GC.GetAllocatedBytesForCurrentThread();

        grid.Write("Hello 😀", Style.Default);
        grid.WriteLineBreak();
        grid.Write("World", Style.Default);
        grid.WriteAnsi(output);

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }
}
