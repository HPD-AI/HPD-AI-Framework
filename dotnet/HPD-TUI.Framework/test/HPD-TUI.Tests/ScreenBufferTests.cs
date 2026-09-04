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
        var firstWriter = new SegmentWriter(first.Grid);
        var secondWriter = new SegmentWriter(second.Grid);

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
        var firstWriter = new SegmentWriter(first.Grid);
        var secondWriter = new SegmentWriter(second.Grid);

        firstWriter.Write("hello", Style.Default);
        secondWriter.Write("hello", Style.Default);
        secondWriter.MoveTo(1, 0);
        secondWriter.Write("a", Style.Default);
        first.ComputeFinalRowFingerprints();
        second.ComputeFinalRowFingerprints();

        Assert.False(first.RowEquals(second, 0));
    }
}
