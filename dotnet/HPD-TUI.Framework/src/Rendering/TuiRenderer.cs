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
    private readonly ITerminalOutputTransport _transport;
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
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
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
        _currentScreen!.Clear();

        var cacheHit = _displayList.Prepare(root, in context, size.Width);
        _displayList.Replay(_currentScreen.Grid);
        _currentScreen.ComputeFinalRowFingerprints();
        var usedLines = TuiCapture.GetUsedLineCount(_currentScreen.Grid);

        _output.Clear();
        var recovery = !_terminalCertain;
        if (!_hasPreviousFrame || recovery)
        {
            _output.Write(ClearScreenAndCursorHome);
            AnsiGridRenderer.WriteFull(_currentScreen.Grid, _output);
        }
        else
        {
            AnsiGridRenderer.WriteDifferential(_previousScreen!, _currentScreen, _output);
        }

        if (!_hasPreviousFrame || CursorStateChanged(_previousScreen!.Grid, _currentScreen.Grid))
            AppendCursorState(_currentScreen.Grid, _output);
        if (_output.Length == 0)
        {
            PublishRenderCompleted(sink, "terminal-grid", startTimestamp, usedLines, _displayList.Count, cacheHit);
            (_currentScreen, _previousScreen) = (_previousScreen, _currentScreen);
            return;
        }
        PublishFrame(recovery);
        PublishRenderCompleted(sink, "terminal-grid", startTimestamp, usedLines, _displayList.Count, cacheHit);
        (_currentScreen, _previousScreen) = (_previousScreen, _currentScreen);
        _hasPreviousFrame = true;
    }

    private void PublishFrame(bool recovery)
    {
        using var lease = _output.CreateLease();
        var result = _transport.TryWriteFrameAsync(lease).GetAwaiter().GetResult();
        _output.Clear();
        if (result.Status == TerminalWriteStatus.Failed)
        {
            _terminalCertain = false;
            throw new InvalidOperationException("Terminal frame publication failed; terminal state is uncertain.", result.Error);
        }
        if (result.Status == TerminalWriteStatus.Backpressured)
            throw new InvalidOperationException("The synchronous terminal transport unexpectedly reported backpressure.");
        if (recovery) _terminalCertain = true;
    }

    private static void PublishRenderCompleted(
        IHpdTuiPerformanceEventSink? sink,
        string surface,
        long startTimestamp,
        int rowsRendered,
        int segmentsWritten,
        bool cacheHit)
    {
        if (sink is null)
        {
            return;
        }

        sink.Publish(new TuiRenderCompleted(
            surface,
            Stopwatch.GetElapsedTime(startTimestamp),
            rowsRendered,
            segmentsWritten,
            CacheHits: cacheHit ? 1 : 0,
            CacheMisses: cacheHit ? 0 : 1));
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
