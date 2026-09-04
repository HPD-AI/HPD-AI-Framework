using HPD.TUI.Core;
using HPD.TUI.Rendering;

namespace HPD.TUI.Tests;

public sealed class ScreenBufferTests
{
    [Fact]
    public void RowEquals_UsesFinalSemanticCellsAcrossDifferentArenas()
    {
        using var first = new ScreenBuffer(12, 2);
        using var second = new ScreenBuffer(12, 2);
        var firstWriter = new DisplayListBuilder(first.Grid, first.Grid.Width);
        var secondWriter = new DisplayListBuilder(second.Grid, second.Grid.Width);

        firstWriter.MoveTo(0, 1);
        firstWriter.Write("discarded", Style.Default);
        firstWriter.MoveTo(0, 0);
        firstWriter.Write("hello", Style.Default);
        secondWriter.Write("hello", Style.Default);
        first.ComputeFinalRowFingerprints();
        second.ComputeFinalRowFingerprints();

        Assert.True(first.RowEquals(second, 0));
    }

    [Fact]
    public void RowEquals_DetectsFinalOverwriteDifference()
    {
        using var first = new ScreenBuffer(12, 2);
        using var second = new ScreenBuffer(12, 2);
        var firstWriter = new DisplayListBuilder(first.Grid, first.Grid.Width);
        var secondWriter = new DisplayListBuilder(second.Grid, second.Grid.Width);

        firstWriter.Write("hello", Style.Default);
        secondWriter.Write("hello", Style.Default);
        secondWriter.MoveTo(1, 0);
        secondWriter.Write("a", Style.Default);
        first.ComputeFinalRowFingerprints();
        second.ComputeFinalRowFingerprints();

        Assert.False(first.RowEquals(second, 0));
    }
}
