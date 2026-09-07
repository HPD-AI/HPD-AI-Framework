using System.Diagnostics;

namespace HPD.TUI.Observability;

internal sealed class TuiFrameInstrumentation
{
    private long _layoutStopwatchTicks;

    public int ComponentsMeasured { get; private set; }
    public int LayoutCacheHits { get; private set; }
    public int LayoutCacheMisses { get; private set; }
    public TimeSpan LayoutDuration => TimeSpan.FromSeconds(
        (double)_layoutStopwatchTicks / Stopwatch.Frequency);

    public void RecordLayout(bool cacheHit, long elapsedStopwatchTicks)
    {
        _layoutStopwatchTicks += elapsedStopwatchTicks;
        if (cacheHit) LayoutCacheHits++;
        else
        {
            LayoutCacheMisses++;
            ComponentsMeasured++;
        }
    }
}

internal static class TuiInstrumentationContext
{
    [ThreadStatic]
    private static TuiFrameInstrumentation? _frame;

    [ThreadStatic]
    private static TuiPerformanceCounters? _counters;

    public static Scope Enter(TuiFrameInstrumentation? frame, TuiPerformanceCounters? counters)
    {
        var scope = new Scope(_frame, _counters);
        _frame = frame;
        _counters = counters;
        return scope;
    }

    public static bool IsEnabled => _frame is not null || _counters is not null;

    public static void RecordLayout(bool cacheHit, long elapsedStopwatchTicks)
    {
        _frame?.RecordLayout(cacheHit, elapsedStopwatchTicks);
        _counters?.RecordLayoutMeasurement(cacheHit);
    }

    internal readonly struct Scope(TuiFrameInstrumentation? previousFrame, TuiPerformanceCounters? previousCounters)
        : IDisposable
    {
        public void Dispose()
        {
            _frame = previousFrame;
            _counters = previousCounters;
        }
    }
}
