using HPD.TUI.Observability;

namespace HPD.TUI.Tests;

public sealed class TuiPerformanceCounterTests
{
    [Fact]
    public void Snapshot_CombinesSurfaceScrollbackAndMarkdownCounters()
    {
        var counters = new TuiPerformanceCounters();
        counters.RecordSurfaceAllocation(4096);
        counters.RecordSurfaceEviction(1024);
        counters.RecordScrollbackCommitted(7);
        counters.RecordScrollbackReplayed(3);
        counters.RecordMarkdownWork(5, 19);

        var snapshot = counters.Snapshot();

        Assert.Equal(3072, snapshot.SurfaceCacheBytes);
        Assert.Equal(1, snapshot.SurfaceCacheEvictions);
        Assert.Equal(7, snapshot.ScrollbackRowsCommitted);
        Assert.Equal(3, snapshot.ScrollbackRowsReplayed);
        Assert.Equal(5, snapshot.MarkdownStablePrefixNodesReused);
        Assert.Equal(19, snapshot.MarkdownCharactersReparsed);
    }

    [Fact]
    public void Recorders_RejectNegativeOperationCounts()
    {
        var counters = new TuiPerformanceCounters();

        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordSurfaceAllocation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordSurfaceEviction(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordScrollbackCommitted(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordScrollbackReplayed(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordMarkdownWork(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => counters.RecordMarkdownWork(0, -1));
    }

    [Fact]
    public void Snapshot_OnUnusedRecorder_IsAllZero()
    {
        Assert.Equal(default, new TuiPerformanceCounters().Snapshot());
    }
}
