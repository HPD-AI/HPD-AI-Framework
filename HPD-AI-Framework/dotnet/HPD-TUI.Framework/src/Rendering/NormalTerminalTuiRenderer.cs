using System.Diagnostics;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public sealed class NormalTerminalTuiRenderer : IDisposable
{
    private static readonly char[] BeginSynchronizedOutput = ['\x1b', '[', '?', '2', '0', '2', '6', 'h'];
    private static readonly char[] EndSynchronizedOutput = ['\x1b', '[', '?', '2', '0', '2', '6', 'l'];
    private static readonly char[] ClearScreenAndCursorHome = ['\x1b', '[', '2', 'J', '\x1b', '[', 'H'];
    private static readonly char[] ClearScrollbackScreenAndCursorHome = ['\x1b', '[', '3', 'J', '\x1b', '[', '2', 'J', '\x1b', '[', 'H'];

    private readonly ITerminal _terminal;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AnsiFrameWriter _output = new();
    private TerminalGrid? _currentGrid;
    private TerminalGrid? _previousGrid;
    private int _previousWidth;
    private int _previousHeight;
    private int _previousVirtualHeight;
    private int _previousUsedLineCount;
    private int _previousViewportTop;
    private int _hardwareCursorRow;
    private bool _hasPreviousFrame;
    private bool _disposed;

    public NormalTerminalTuiRenderer(ITerminal terminal)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
    }

    public bool TrackHardwareCursor { get; set; }

    public IHpdTuiPerformanceEventSink? PerformanceSink { get; set; }

    public void Render(IComponent root, Theme? theme, int virtualHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(virtualHeight);

        var sink = PerformanceSink;
        var startTimestamp = sink is null ? 0 : Stopwatch.GetTimestamp();
        var size = _terminal.GetSize();
        EnsureGrid(size.Width, virtualHeight);

        _currentGrid!.Clear();
        var context = new RenderContext(size.Width, virtualHeight, theme ?? Theme.Default, elapsed: _clock.Elapsed);
        var writer = new SegmentWriter(_currentGrid);
        root.Render(in context, size.Width, ref writer);

        var usedLines = TuiCapture.GetUsedLineCount(_currentGrid);

        var sizeChanged = _previousWidth != 0 && (
            _previousWidth != size.Width ||
            _previousHeight != size.Height ||
            _previousVirtualHeight != virtualHeight);
        if (!_hasPreviousFrame || sizeChanged)
        {
            FullRender(size, usedLines, clearScrollback: sizeChanged);
            PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
            return;
        }

        var changed = FindChangedLineRange(_previousGrid!, _currentGrid, _previousUsedLineCount, usedLines);
        if (changed.First < 0)
        {
            PositionHardwareCursor(_currentGrid, usedLines);
            CommitFrame(size, virtualHeight, usedLines);
            PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
            return;
        }

        if (usedLines < _previousUsedLineCount || changed.First < _previousViewportTop)
        {
            FullRender(size, usedLines);
            PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
            return;
        }

        PatchChangedLines(size, changed.First, changed.Last, usedLines);
        PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
    }

    private static void PublishRenderCompleted(
        IHpdTuiPerformanceEventSink? sink,
        long startTimestamp,
        int rowsRendered,
        int segmentsWritten)
    {
        if (sink is null)
        {
            return;
        }

        sink.Publish(new TuiRenderCompleted(
            "normal-terminal",
            Stopwatch.GetElapsedTime(startTimestamp),
            rowsRendered,
            segmentsWritten,
            CacheHits: 0,
            CacheMisses: 0));
    }

    private void FullRender(TerminalSize size, int usedLines, bool clearScrollback = false)
    {
        WriteFrame(BuildFullFrame);

        _previousViewportTop = GetViewportTop(usedLines, size.Height);
        _hardwareCursorRow = Math.Max(0, usedLines - 1);
        CommitFrame(size, _currentGrid!.Height, usedLines);
        PositionHardwareCursor(_previousGrid!, usedLines);

        void BuildFullFrame(AnsiFrameWriter output)
        {
            output.Write(BeginSynchronizedOutput);
            output.Write(clearScrollback ? ClearScrollbackScreenAndCursorHome : ClearScreenAndCursorHome);
            WriteLines(output, _currentGrid!, 0, usedLines - 1);
            output.Write(EndSynchronizedOutput);
        }
    }

    private void PatchChangedLines(
        TerminalSize size,
        int firstChanged,
        int lastChanged,
        int usedLines)
    {
        if (firstChanged >= usedLines)
        {
            FullRender(size, usedLines);
            return;
        }

        var viewportTop = _previousViewportTop;
        var appendStart = firstChanged == _previousUsedLineCount && firstChanged > 0;
        var targetRow = appendStart ? firstChanged - 1 : firstChanged;
        var viewportBottom = viewportTop + size.Height - 1;
        var renderEnd = Math.Min(lastChanged, usedLines - 1);
        var viewportTopAfterBuild = viewportTop;
        WriteFrame(BuildPatchFrame);

        _hardwareCursorRow = renderEnd;
        _previousViewportTop = Math.Max(viewportTopAfterBuild, GetViewportTop(usedLines, size.Height));
        CommitFrame(size, _currentGrid!.Height, usedLines);
        PositionHardwareCursor(_previousGrid!, usedLines);

        void BuildPatchFrame(AnsiFrameWriter output)
        {
            var frameViewportTop = viewportTop;
            var frameHardwareCursorRow = _hardwareCursorRow;
            output.Write(BeginSynchronizedOutput);

            if (targetRow > viewportBottom)
            {
                MoveToRow(output, viewportBottom, frameViewportTop, ref frameHardwareCursorRow);

                var scrollRows = targetRow - viewportBottom;
                for (var i = 0; i < scrollRows; i++)
                {
                    output.Write("\r\n");
                }

                frameViewportTop += scrollRows;
                frameHardwareCursorRow = targetRow;
            }
            else
            {
                MoveToRow(output, targetRow, frameViewportTop, ref frameHardwareCursorRow);
            }

            if (appendStart)
            {
                output.Write("\r\n");

                frameViewportTop = Math.Max(frameViewportTop, GetViewportTop(usedLines, size.Height));
                frameHardwareCursorRow = firstChanged;
            }
            else
            {
                output.Write('\r');
            }

            WriteLines(output, _currentGrid!, firstChanged, renderEnd);
            output.Write(EndSynchronizedOutput);

            viewportTopAfterBuild = frameViewportTop;
        }
    }

    private void MoveToRow(AnsiFrameWriter output, int targetRow, int viewportTop)
    {
        var hardwareCursorRow = _hardwareCursorRow;
        MoveToRow(output, targetRow, viewportTop, ref hardwareCursorRow);
        _hardwareCursorRow = hardwareCursorRow;
    }

    private static void MoveToRow(AnsiFrameWriter output, int targetRow, int viewportTop, ref int hardwareCursorRow)
    {
        var currentScreenRow = hardwareCursorRow - viewportTop;
        var targetScreenRow = targetRow - viewportTop;
        var rowDelta = targetScreenRow - currentScreenRow;
        if (rowDelta > 0)
        {
            output.Write("\x1b[");
            output.WriteInt(rowDelta);
            output.Write('B');
        }
        else if (rowDelta < 0)
        {
            output.Write("\x1b[");
            output.WriteInt(-rowDelta);
            output.Write('A');
        }

        hardwareCursorRow = targetRow;
    }

    private void PositionHardwareCursor(TerminalGrid grid, int lineCount)
    {
        if (!TrackHardwareCursor || !grid.HasTerminalCursor || lineCount <= 0)
        {
            _terminal.HideCursor();
            return;
        }

        var targetRow = Math.Clamp(grid.TerminalCursorY, 0, Math.Max(0, lineCount - 1));
        var currentScreenRow = _hardwareCursorRow - _previousViewportTop;
        var targetScreenRow = targetRow - _previousViewportTop;
        var rowDelta = targetScreenRow - currentScreenRow;
        _output.Clear();
        if (rowDelta > 0)
        {
            _output.Write("\x1b[");
            _output.WriteInt(rowDelta);
            _output.Write('B');
        }
        else if (rowDelta < 0)
        {
            _output.Write("\x1b[");
            _output.WriteInt(-rowDelta);
            _output.Write('A');
        }

        _output.Write("\x1b[");
        _output.WriteInt(grid.TerminalCursorX + 1);
        _output.Write('G');
        _output.FlushTo(_terminal);
        _hardwareCursorRow = targetRow;
        _terminal.ShowCursor();
    }

    private void CommitFrame(TerminalSize size, int virtualHeight, int usedLines)
    {
        (_currentGrid, _previousGrid) = (_previousGrid, _currentGrid);
        _previousWidth = size.Width;
        _previousHeight = size.Height;
        _previousVirtualHeight = virtualHeight;
        _previousUsedLineCount = usedLines;
        _hasPreviousFrame = true;
    }

    private static int GetViewportTop(int lineCount, int height)
    {
        return Math.Max(0, lineCount - height);
    }

    private void WriteFrame(FrameBuilder builder)
    {
        _output.Clear();
        builder(_output);
        _output.FlushTo(_terminal);
    }

    private delegate void FrameBuilder(AnsiFrameWriter output);

    private static void WriteLines(
        AnsiFrameWriter output,
        TerminalGrid grid,
        int start,
        int endInclusive)
    {
        if (start > endInclusive)
        {
            return;
        }

        for (var y = start; y <= endInclusive; y++)
        {
            if (y > start)
            {
                output.Write("\r\n");
            }

            output.Write("\x1b[2K");
            AnsiGridRenderer.WriteLine(grid, y, output);
        }
    }

    private static (int First, int Last) FindChangedLineRange(
        TerminalGrid previous,
        TerminalGrid current,
        int previousUsedLines,
        int currentUsedLines)
    {
        var count = Math.Max(previousUsedLines, currentUsedLines);
        var first = -1;
        var last = -1;
        for (var y = 0; y < count; y++)
        {
            if (y < previousUsedLines &&
                y < currentUsedLines &&
                RowsEqual(previous, current, y))
            {
                continue;
            }

            first = first < 0 ? y : first;
            last = y;
        }

        return (first, last);
    }

    private static bool RowsEqual(TerminalGrid previous, TerminalGrid current, int y)
    {
        for (var x = 0; x < current.Width; x++)
        {
            if (current.GetCell(x, y) != previous.GetCell(x, y))
            {
                return false;
            }
        }

        return true;
    }

    private void EnsureGrid(int width, int virtualHeight)
    {
        if (_currentGrid is not null &&
            _previousGrid is not null &&
            _currentGrid.Width == width &&
            _currentGrid.Height == virtualHeight)
        {
            return;
        }

        _currentGrid?.Dispose();
        _previousGrid?.Dispose();
        _currentGrid = new TerminalGrid(width, virtualHeight);
        _previousGrid = new TerminalGrid(width, virtualHeight);
        _hasPreviousFrame = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _output.Dispose();
        _currentGrid?.Dispose();
        _previousGrid?.Dispose();
    }
}
