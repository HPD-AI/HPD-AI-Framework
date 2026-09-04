using System.Diagnostics;
using HPD.TUI.Core;
using HPD.TUI.Observability;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public sealed class ManagedTerminalTuiRenderer : IDisposable
{
    private static readonly char[] BeginSynchronizedOutput = ['\x1b', '[', '?', '2', '0', '2', '6', 'h'];
    private static readonly char[] EndSynchronizedOutput = ['\x1b', '[', '?', '2', '0', '2', '6', 'l'];
    private static readonly char[] DisableAutowrap = ['\x1b', '[', '?', '7', 'l'];
    private static readonly char[] EnableAutowrap = ['\x1b', '[', '?', '7', 'h'];
    private static readonly char[] HideHardwareCursor = ['\x1b', '[', '?', '2', '5', 'l'];
    private static readonly char[] ShowHardwareCursor = ['\x1b', '[', '?', '2', '5', 'h'];
    private static readonly char[] ClearScreenAndCursorHome = ['\x1b', '[', '2', 'J', '\x1b', '[', 'H'];
    private readonly ITerminal _terminal;
    private readonly ITerminalOutputTransport _transport;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AnsiFrameWriter _output = new();
    private ScreenBuffer? _currentBuffer;
    private ScreenBuffer? _previousBuffer;
    private int _previousWidth;
    private int _previousHeight;
    private int _previousUsedLineCount;
    private int _previousViewportTop;
    private int _hardwareCursorRow;
    private bool _hasPreviousFrame;
    private bool _terminalCertain = true;
    private bool _disposed;

    public ManagedTerminalTuiRenderer(ITerminal terminal)
        : this(terminal, new SynchronousTerminalOutputTransport(terminal))
    {
    }

    /// <summary>Creates a renderer that publishes through the supplied output transport.</summary>
    /// <param name="terminal">The terminal used for sizing and cursor visibility.</param>
    /// <param name="transport">The single-writer frame transport.</param>
    public ManagedTerminalTuiRenderer(ITerminal terminal, ITerminalOutputTransport transport)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    internal ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken)
        => _transport.WaitUntilWritableAsync(cancellationToken);

    public bool TrackHardwareCursor { get; set; }

    public IHpdTuiPerformanceEventSink? PerformanceSink { get; set; }

    public void Render(IComponent root, Theme? theme = null, ScrollbackBatch? scrollback = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        var sink = PerformanceSink;
        var startTimestamp = sink is null ? 0 : Stopwatch.GetTimestamp();
        var size = _terminal.GetSize();
        var hadPreviousFrame = _hasPreviousFrame;
        var sizeChanged = _previousWidth != 0 && (
            _previousWidth != size.Width ||
            _previousHeight != size.Height);
        EnsureBuffer(size.Width, size.Height);

        _currentBuffer!.Clear();
        var context = new RenderContext(size.Width, size.Height, theme ?? Theme.Default, elapsed: _clock.Elapsed);
        var writer = new SegmentWriter(_currentBuffer.Grid);
        root.Render(in context, size.Width, ref writer);
        _currentBuffer.ComputeFinalRowFingerprints();

        var usedLines = TuiCapture.GetUsedLineCount(_currentBuffer.Grid);

        if (scrollback is not null)
        {
            FullRender(size, usedLines, FullRenderClearMode.Screen, scrollback);
            PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
            return;
        }

        if (!hadPreviousFrame && !sizeChanged)
        {
            FullRender(size, usedLines, FullRenderClearMode.Screen);
            PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
            return;
        }

        if (sizeChanged)
        {
            FullRender(size, usedLines, FullRenderClearMode.Screen);
            PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
            return;
        }

        var changed = FindChangedLineRange(_previousBuffer!, _currentBuffer, _previousUsedLineCount, usedLines);
        if (changed.First < 0)
        {
            PublishCursorOnlyIfChanged(_currentBuffer.Grid, usedLines);
            CommitFrame(size, usedLines);
            PublishRenderCompleted(sink, startTimestamp, usedLines, writer.Count);
            return;
        }

        if (usedLines < _previousUsedLineCount || changed.First < _previousViewportTop)
        {
            FullRender(size, usedLines, FullRenderClearMode.Screen);
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
            "managed-terminal",
            Stopwatch.GetElapsedTime(startTimestamp),
            rowsRendered,
            segmentsWritten,
            CacheHits: 0,
            CacheMisses: 0));
    }

    private void FullRender(TerminalSize size, int usedLines, FullRenderClearMode clearMode, ScrollbackBatch? scrollback = null)
    {
        var viewportTop = GetViewportTop(usedLines, size.Height);
        var acceptedHardwareCursorRow = Math.Max(0, usedLines - 1);
        WriteFrame(BuildFullFrame);

        _previousViewportTop = viewportTop;
        _hardwareCursorRow = acceptedHardwareCursorRow;
        CommitFrame(size, usedLines);

        void BuildFullFrame(AnsiFrameWriter output)
        {
            output.Write(BeginSynchronizedOutput);
            if (scrollback is not null)
            {
                output.Write(HideHardwareCursor);
                output.Write(DisableAutowrap);
                output.Write(ClearScreenAndCursorHome);
                output.Write("\x1b[");
                output.WriteInt(size.Height);
                output.Write('H');
                foreach (var row in scrollback.Rows)
                {
                    output.Write('\r');
                    AnsiGridRenderer.WriteScrollbackRow(row, output);
                    output.Write("\x1b[K\r\n");
                }
                output.Write(EnableAutowrap);
            }
            if (clearMode == FullRenderClearMode.Screen)
            {
                output.Write(ClearScreenAndCursorHome);
            }
            WriteLines(output, _currentBuffer!.Grid, 0, usedLines - 1);
            WriteCursorState(output, _currentBuffer.Grid, usedLines, viewportTop, ref acceptedHardwareCursorRow);
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
            FullRender(size, usedLines, FullRenderClearMode.Screen);
            return;
        }

        var viewportTop = _previousViewportTop;
        var appendStart = firstChanged == _previousUsedLineCount && firstChanged > 0;
        var targetRow = appendStart ? firstChanged - 1 : firstChanged;
        var viewportBottom = viewportTop + size.Height - 1;
        var renderEnd = Math.Min(lastChanged, usedLines - 1);
        var viewportTopAfterBuild = viewportTop;
        var acceptedHardwareCursorRow = _hardwareCursorRow;
        WriteFrame(BuildPatchFrame);

        _hardwareCursorRow = acceptedHardwareCursorRow;
        _previousViewportTop = Math.Max(viewportTopAfterBuild, GetViewportTop(usedLines, size.Height));
        CommitFrame(size, usedLines);

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

            WriteLines(output, _currentBuffer!.Grid, firstChanged, renderEnd);
            WriteCursorState(output, _currentBuffer.Grid, usedLines, frameViewportTop, ref frameHardwareCursorRow);
            output.Write(EndSynchronizedOutput);

            viewportTopAfterBuild = frameViewportTop;
            acceptedHardwareCursorRow = frameHardwareCursorRow;
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

    private void PublishCursorOnlyIfChanged(TerminalGrid grid, int lineCount)
    {
        var previous = _previousBuffer!.Grid;
        var previousVisible = TrackHardwareCursor && previous.HasTerminalCursor;
        var currentVisible = TrackHardwareCursor && grid.HasTerminalCursor;
        if (previousVisible == currentVisible &&
            (!currentVisible ||
             (grid.TerminalCursorX == previous.TerminalCursorX && grid.TerminalCursorY == previous.TerminalCursorY)))
            return;

        var hardwareRow = _hardwareCursorRow;
        WriteFrame(output =>
        {
            output.Write(BeginSynchronizedOutput);
            WriteCursorState(output, grid, lineCount, _previousViewportTop, ref hardwareRow);
            output.Write(EndSynchronizedOutput);
        });
        _hardwareCursorRow = hardwareRow;
    }

    private void WriteCursorState(
        AnsiFrameWriter output,
        TerminalGrid grid,
        int lineCount,
        int viewportTop,
        ref int hardwareCursorRow)
    {
        if (!TrackHardwareCursor || !grid.HasTerminalCursor || lineCount <= 0)
        {
            output.Write(HideHardwareCursor);
            return;
        }

        var targetRow = Math.Clamp(grid.TerminalCursorY, 0, Math.Max(0, lineCount - 1));
        AnsiGridRenderer.WriteCursorMove(grid.TerminalCursorX, targetRow - viewportTop, output);
        output.Write(ShowHardwareCursor);
        hardwareCursorRow = targetRow;
    }

    private void CommitFrame(TerminalSize size, int usedLines)
    {
        (_currentBuffer, _previousBuffer) = (_previousBuffer, _currentBuffer);
        _previousWidth = size.Width;
        _previousHeight = size.Height;
        _previousUsedLineCount = usedLines;
        _hasPreviousFrame = true;
    }

    private static int GetViewportTop(int lineCount, int height)
    {
        return Math.Max(0, lineCount - height);
    }

    private void WriteFrame(FrameBuilder builder)
    {
        if (!_terminalCertain)
            throw new InvalidOperationException("Terminal state is uncertain; this renderer cannot safely publish another frame.");
        _output.Clear();
        builder(_output);
        using var lease = _output.CreateLease();
        var result = _transport.TryWriteFrameAsync(lease).GetAwaiter().GetResult();
        _output.Clear();
        if (result.Status == TerminalWriteStatus.Failed)
        {
            _terminalCertain = false;
            throw new InvalidOperationException("Managed terminal publication failed; terminal state is uncertain.", result.Error);
        }
        if (result.Status == TerminalWriteStatus.Backpressured)
            throw new TerminalBackpressureException();
    }

    private delegate void FrameBuilder(AnsiFrameWriter output);

    private enum FullRenderClearMode
    {
        None,
        Screen
    }

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
        ScreenBuffer previous,
        ScreenBuffer current,
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
                current.RowEquals(previous, y))
            {
                continue;
            }

            first = first < 0 ? y : first;
            last = y;
        }

        return (first, last);
    }

    private void EnsureBuffer(int width, int height)
    {
        if (_currentBuffer is not null &&
            _previousBuffer is not null &&
            _currentBuffer.Width == width &&
            _currentBuffer.Height == height)
        {
            return;
        }

        _currentBuffer?.Dispose();
        _previousBuffer?.Dispose();
        _currentBuffer = new ScreenBuffer(width, height);
        _previousBuffer = new ScreenBuffer(width, height);
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
        _currentBuffer?.Dispose();
        _previousBuffer?.Dispose();
    }
}

internal sealed class TerminalBackpressureException : Exception;
