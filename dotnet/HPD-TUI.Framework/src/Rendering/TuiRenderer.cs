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
    private ScreenBuffer? _currentScreen;
    private ScreenBuffer? _previousScreen;
    private bool _hasPreviousFrame;
    private bool _disposed;

    public TuiRenderer(ITerminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _transport = new SynchronousTerminalOutputTransport(terminal);
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

        var writer = new SegmentWriter(_currentScreen.Grid);
        root.Render(in context, size.Width, ref writer);
        _currentScreen.ComputeFinalRowFingerprints();
        var usedLines = TuiCapture.GetUsedLineCount(_currentScreen.Grid);

        _output.Clear();
        if (!_hasPreviousFrame)
        {
            _output.Write(ClearScreenAndCursorHome);
            AnsiGridRenderer.WriteFull(_currentScreen.Grid, _output);
        }
        else
        {
            AnsiGridRenderer.WriteDifferential(_previousScreen!, _currentScreen, _output);
        }

        AppendCursorState(_currentScreen.Grid, _output);
        PublishFrame();
        PublishRenderCompleted(sink, "terminal-grid", startTimestamp, usedLines, writer.Count);
        (_currentScreen, _previousScreen) = (_previousScreen, _currentScreen);
        _hasPreviousFrame = true;
    }

    private void PublishFrame()
    {
        using var lease = _output.CreateLease();
        var result = _transport.TryWriteFrameAsync(lease).GetAwaiter().GetResult();
        _output.Clear();
        if (result.Status == TerminalWriteStatus.Failed)
            throw new InvalidOperationException("Terminal frame publication failed; terminal state is uncertain.", result.Error);
        if (result.Status == TerminalWriteStatus.Backpressured)
            throw new InvalidOperationException("The synchronous terminal transport unexpectedly reported backpressure.");
    }

    private static void PublishRenderCompleted(
        IHpdTuiPerformanceEventSink? sink,
        string surface,
        long startTimestamp,
        int rowsRendered,
        int segmentsWritten)
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
            CacheHits: 0,
            CacheMisses: 0));
    }

    private static void AppendCursorState(TerminalGrid grid, AnsiFrameWriter output)
    {
        output.Write(grid.HasTerminalCursor ? CursorShow : CursorHide);
        if (grid.HasTerminalCursor)
        {
            AnsiGridRenderer.WriteCursorMove(grid.TerminalCursorX, grid.TerminalCursorY, output);
        }
    }

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
