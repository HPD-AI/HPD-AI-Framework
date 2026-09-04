using HPD.TUI.Observability;
using HPD.TUI.Components;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;

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

    [Fact]
    public void Snapshot_ReportsSchedulerAndLayoutDecisionsSeparately()
    {
        var counters = new TuiPerformanceCounters();
        counters.RecordRenderRequest(coalesced: false);
        counters.RecordRenderRequest(coalesced: true);
        counters.RecordFrameAdmitted();
        counters.RecordFrameSuppressed();
        counters.RecordPacingDeferral();
        counters.RecordBackpressureDeferral();
        counters.RecordLayoutMeasurement(cacheHit: true);
        counters.RecordLayoutMeasurement(cacheHit: false);

        var snapshot = counters.Snapshot();

        Assert.Equal(2, snapshot.RenderRequestsReceived);
        Assert.Equal(1, snapshot.RenderRequestsCoalesced);
        Assert.Equal(1, snapshot.FramesAdmitted);
        Assert.Equal(1, snapshot.FramesSuppressedAsNoOp);
        Assert.Equal(1, snapshot.FramesDeferredByPacing);
        Assert.Equal(1, snapshot.FramesDeferredByBackpressure);
        Assert.Equal(1, snapshot.ComponentsMeasured);
        Assert.Equal(1, snapshot.LayoutCacheHits);
        Assert.Equal(1, snapshot.LayoutCacheMisses);
    }

    [Fact]
    public void DisabledInstrumentationScope_DoesNotAllocateAfterWarmup()
    {
        using (TuiInstrumentationContext.Enter(null, null)) { }
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1000; index++)
            using (TuiInstrumentationContext.Enter(null, null)) { }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void DisabledDiagnostics_WarmedRequestedFrameAllocatesNothing()
    {
        using var terminal = new NullTerminal();
        using var renderer = new TuiRenderer(terminal);
        var root = new Text("stable");
        renderer.Render(root);
        renderer.Render(root);
        var before = GC.GetAllocatedBytesForCurrentThread();

        renderer.Render(root);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class NullTerminal : ITerminal, ITerminalInput
    {
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => new(80, 24);
        public void Write(ReadOnlySpan<char> text) { }
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(TerminalInputEvent.Stop);
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
