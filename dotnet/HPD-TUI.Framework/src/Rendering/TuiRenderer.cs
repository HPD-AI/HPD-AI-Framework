using System.Diagnostics;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public sealed class TuiRenderer : IDisposable
{
    private static readonly char[] ClearScreenAndCursorHome = ['\x1b', '[', '2', 'J', '\x1b', '[', 'H'];
    private static readonly char[] CursorHide = ['\x1b', '[', '?', '2', '5', 'l'];
    private static readonly char[] CursorShow = ['\x1b', '[', '?', '2', '5', 'h'];
    private readonly ITerminal _terminal;
    private readonly TerminalPublicationCoordinator _publisher;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AnsiFrameWriter _output = new();
    private readonly RetainedDisplayList _displayList = new();
    private ScreenBuffer? _currentScreen;
    private ScreenBuffer? _previousScreen;
    private bool _hasPreviousFrame;
    private bool _terminalCertain = true;
    private bool _disposed;

    public TuiRenderer(ITerminal terminal)
        : this(terminal, new SynchronousTerminalOutputTransport(terminal))
    {
    }

    /// <summary>Creates an alternate-screen renderer with an explicit output transport.</summary>
    /// <param name="terminal">The terminal used for sizing.</param>
    /// <param name="transport">The single-writer output transport.</param>
    public TuiRenderer(ITerminal terminal, ITerminalOutputTransport transport)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _publisher = new TerminalPublicationCoordinator(transport);
    }

    internal ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken)
        => _publisher.WaitUntilWritableAsync(cancellationToken);

    internal void PublishControl(ReadOnlySpan<char> controlSequence)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _output.Clear();
        _output.Write(controlSequence);
        PublishFrame(recovery: !_terminalCertain);
    }

    public IHpdTuiPerformanceEventSink? PerformanceSink { get; set; }

    public void Render(IComponent root, Theme? theme = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        var size = _terminal.GetSize();
        var sink = PerformanceSink;
        var startTimestamp = sink is null ? 0 : Stopwatch.GetTimestamp();
        EnsureGrid(size);

        var context = new RenderContext(size.Width, size.Height, theme ?? Theme.Default, elapsed: _clock.Elapsed);
        var displayStart = Stopwatch.GetTimestamp();
        var cacheHit = _displayList.Prepare(root, in context, size.Width);
        var displayDuration = Stopwatch.GetElapsedTime(displayStart);
        if (cacheHit && _hasPreviousFrame && _terminalCertain)
        {
            PublishDiagnostics(sink, startTimestamp, displayDuration, TimeSpan.Zero, TimeSpan.Zero,
                default, 0, false, true, TimeSpan.Zero);
            return;
        }
        var rasterStart = Stopwatch.GetTimestamp();
        if (_hasPreviousFrame && !_displayList.RequiresFullRaster)
        {
            _currentScreen!.CopyFrom(_previousScreen!);
            _currentScreen.ClearDamagedRows(_displayList.DamagedRows);
            _displayList.ReplayDamaged(_currentScreen.Grid);
            _currentScreen.ComputeFinalRowFingerprints(_displayList.DamagedRows);
        }
        else
        {
            _currentScreen!.Clear();
            _displayList.Replay(_currentScreen.Grid);
            _currentScreen.ComputeFinalRowFingerprints();
        }
        var rasterDuration = Stopwatch.GetElapsedTime(rasterStart);
        var usedLines = TuiCapture.GetUsedLineCount(_currentScreen.Grid);

        _output.Clear();
        var recovery = !_terminalCertain;
        var fullRepaint = !_hasPreviousFrame || recovery;
        var diffStart = Stopwatch.GetTimestamp();
        ScreenDiffMetrics metrics;
        if (fullRepaint)
        {
            _output.Write(ClearScreenAndCursorHome);
            AnsiGridRenderer.WriteFull(_currentScreen.Grid, _output);
            metrics = new(size.Height, 0, 0, size.Height, size.Width * size.Height);
        }
        else
        {
            metrics = AnsiGridRenderer.WriteDifferential(_previousScreen!, _currentScreen, _output);
        }
        var diffDuration = Stopwatch.GetElapsedTime(diffStart);

        if (!_hasPreviousFrame || CursorStateChanged(_previousScreen!.Grid, _currentScreen.Grid))
            AppendCursorState(_currentScreen.Grid, _output);
        if (_output.Length == 0)
        {
            PublishDiagnostics(sink, startTimestamp, displayDuration, rasterDuration, diffDuration, metrics, 0, fullRepaint, cacheHit, TimeSpan.Zero);
            (_currentScreen, _previousScreen) = (_previousScreen, _currentScreen);
            return;
        }
        var outputCharacters = _output.Length;
        var outputStart = Stopwatch.GetTimestamp();
        PublishFrame(recovery);
        var outputDuration = Stopwatch.GetElapsedTime(outputStart);
        PublishDiagnostics(sink, startTimestamp, displayDuration, rasterDuration, diffDuration, metrics, outputCharacters, fullRepaint, cacheHit, outputDuration);
        (_currentScreen, _previousScreen) = (_previousScreen, _currentScreen);
        _hasPreviousFrame = true;
    }

    private void PublishFrame(bool recovery)
    {
        var result = _publisher.TryPublish(_output.WrittenSpan);
        _output.Clear();
        if (result.Status == TerminalWriteStatus.Failed)
        {
            _terminalCertain = false;
            throw new InvalidOperationException("Terminal frame publication failed; terminal state is uncertain.", result.Error);
        }
        if (result.Status == TerminalWriteStatus.Backpressured)
            throw new TerminalBackpressureException();
        if (recovery) _terminalCertain = true;
    }

    private void PublishDiagnostics(
        IHpdTuiPerformanceEventSink? sink,
        long startTimestamp,
        TimeSpan displayDuration,
        TimeSpan rasterDuration,
        TimeSpan diffDuration,
        ScreenDiffMetrics metrics,
        int outputCharacters,
        bool fullRepaint,
        bool cacheHit,
        TimeSpan outputDuration)
    {
        if (sink is null)
        {
            return;
        }

        sink.Publish(new TuiFrameDiagnostics(
            SchedulingDelay: TimeSpan.Zero,
            LayoutDuration: TimeSpan.Zero,
            DisplayListDuration: displayDuration,
            RasterDuration: rasterDuration,
            DiffDuration: diffDuration,
            EncodeDuration: TimeSpan.Zero,
            OutputDuration: outputDuration,
            ComponentsMeasured: 0,
            ComponentsPainted: _displayList.ComponentsPainted,
            DisplayCommandsReused: _displayList.CommandsReused,
            DisplayCommandsBuilt: _displayList.CommandsBuilt,
            RowsDamaged: _displayList.DamagedRowCount,
            RowsFingerprintRejected: metrics.RowsFingerprintRejected,
            RowsSemanticallyCompared: metrics.RowsSemanticallyCompared,
            ChangedRuns: metrics.ChangedRuns,
            CellsChanged: metrics.CellsChanged,
            OutputCharacters: outputCharacters,
            FullRepaint: fullRepaint,
            Backpressured: false));
    }

    private static void AppendCursorState(TerminalGrid grid, AnsiFrameWriter output)
    {
        output.Write(grid.HasTerminalCursor ? CursorShow : CursorHide);
        if (grid.HasTerminalCursor)
        {
            AnsiGridRenderer.WriteCursorMove(grid.TerminalCursorX, grid.TerminalCursorY, output);
        }
    }

    private static bool CursorStateChanged(TerminalGrid previous, TerminalGrid current)
        => previous.HasTerminalCursor != current.HasTerminalCursor ||
           (current.HasTerminalCursor &&
            (previous.TerminalCursorX != current.TerminalCursorX ||
             previous.TerminalCursorY != current.TerminalCursorY));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _output.Dispose();
        _currentScreen?.Dispose();
        _previousScreen?.Dispose();
        _terminal.Dispose();
    }

    private void EnsureGrid(TerminalSize size)
    {
        if (_currentScreen is not null &&
            _previousScreen is not null &&
            _currentScreen.Width == size.Width &&
            _currentScreen.Height == size.Height)
        {
            return;
        }

        _currentScreen?.Dispose();
        _previousScreen?.Dispose();
        _currentScreen = new ScreenBuffer(size.Width, size.Height);
        _previousScreen = new ScreenBuffer(size.Width, size.Height);
        _hasPreviousFrame = false;
    }
}
