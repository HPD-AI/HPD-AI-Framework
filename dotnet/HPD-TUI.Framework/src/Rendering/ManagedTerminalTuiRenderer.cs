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
    private static readonly char[] ClearScrollback = ['\x1b', '[', '3', 'J'];
    private readonly ITerminal _terminal;
    private readonly ITerminalOutputTransport _transport;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AnsiFrameWriter _output = new();
    private readonly RetainedDisplayList _displayList = new();
    private readonly ManagedScrollbackJournal _scrollbackJournal;
    private ScreenBuffer? _currentBuffer;
    private ScreenBuffer? _previousBuffer;
    private int _previousWidth;
    private int _previousHeight;
    private int _hardwareCursorRow;
    private bool _hasPreviousFrame;
    private bool _terminalCertain = true;
    private bool _scrollbackUncertain;
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
        _scrollbackJournal = new ManagedScrollbackJournal(PublishScrollbackAsync);
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

        var context = new RenderContext(size.Width, size.Height, theme ?? Theme.Default, elapsed: _clock.Elapsed);
        var cacheHit = _displayList.Prepare(root, in context, size.Width);
        if (cacheHit && hadPreviousFrame && !sizeChanged && _terminalCertain && scrollback is null)
        {
            PublishRenderCompleted(sink, startTimestamp, 0, _displayList.Count, true);
            return;
        }
        if (hadPreviousFrame && !sizeChanged && !_displayList.RequiresFullRaster)
        {
            _currentBuffer!.CopyFrom(_previousBuffer!);
            _currentBuffer.ClearDamagedRows(_displayList.DamagedRows);
            _displayList.ReplayDamaged(_currentBuffer.Grid);
            _currentBuffer.ComputeFinalRowFingerprints(_displayList.DamagedRows);
        }
        else
        {
            _currentBuffer!.Clear();
            _displayList.Replay(_currentBuffer.Grid);
            _currentBuffer.ComputeFinalRowFingerprints();
        }

        var usedLines = TuiCapture.GetUsedLineCount(_currentBuffer.Grid);

        if (!_terminalCertain)
        {
            FullRender(size, usedLines, FullRenderClearMode.Screen, scrollback, recovery: true);
            PublishRenderCompleted(sink, startTimestamp, usedLines, _displayList.Count, cacheHit);
            return;
        }

        if (scrollback is not null)
        {
            using var lease = new ScrollbackBatchLease(scrollback);
            var result = _scrollbackJournal.CommitAsync(lease, new ScrollbackCommitOptions())
                .GetAwaiter().GetResult();
            if (result.Status == ScrollbackCommitStatus.Backpressured)
                throw new TerminalBackpressureException();
            if (result.Status == ScrollbackCommitStatus.Failed)
                throw new InvalidOperationException("Managed scrollback publication failed; terminal state is uncertain.", result.Error);
            PublishRenderCompleted(sink, startTimestamp, usedLines, _displayList.Count, cacheHit);
            return;
        }

        if (!hadPreviousFrame && !sizeChanged)
        {
            FullRender(size, usedLines, FullRenderClearMode.Screen);
            PublishRenderCompleted(sink, startTimestamp, usedLines, _displayList.Count, cacheHit);
            return;
        }

        if (sizeChanged)
        {
            FullRender(size, usedLines, FullRenderClearMode.Screen);
            PublishRenderCompleted(sink, startTimestamp, usedLines, _displayList.Count, cacheHit);
            return;
        }

        var screenChanged = false;
        for (var row = 0; row < size.Height; row++)
            screenChanged |= !_currentBuffer.RowEquals(_previousBuffer!, row);
        if (!screenChanged)
        {
            PublishCursorOnlyIfChanged(_currentBuffer.Grid, usedLines);
            CommitFrame(size, usedLines);
            PublishRenderCompleted(sink, startTimestamp, usedLines, _displayList.Count, cacheHit);
            return;
        }

        PatchChangedRuns(size, usedLines);
        PublishRenderCompleted(sink, startTimestamp, usedLines, _displayList.Count, cacheHit);
    }

    private void PatchChangedRuns(TerminalSize size, int usedLines)
    {
        var acceptedHardwareCursorRow = _hardwareCursorRow;
        WriteFrame(output =>
        {
            output.Write(BeginSynchronizedOutput);
            AnsiGridRenderer.WriteDifferential(_previousBuffer!, _currentBuffer!, output);
            WriteCursorState(output, _currentBuffer!.Grid, usedLines, 0, ref acceptedHardwareCursorRow);
            output.Write(EndSynchronizedOutput);
        });
        _hardwareCursorRow = acceptedHardwareCursorRow;
        CommitFrame(size, usedLines);
    }

    private static void PublishRenderCompleted(
        IHpdTuiPerformanceEventSink? sink,
        long startTimestamp,
        int rowsRendered,
        int segmentsWritten,
        bool cacheHit)
    {
        if (sink is null)
        {
            return;
        }

        var duration = Stopwatch.GetElapsedTime(startTimestamp);
        sink.Publish(new TuiFrameDiagnostics(
            SchedulingDelay: TimeSpan.Zero,
            LayoutDuration: TimeSpan.Zero,
            DisplayListDuration: duration,
            RasterDuration: TimeSpan.Zero,
            DiffDuration: TimeSpan.Zero,
            EncodeDuration: TimeSpan.Zero,
            OutputDuration: TimeSpan.Zero,
            ComponentsMeasured: cacheHit ? 0 : 1,
            ComponentsPainted: cacheHit ? 0 : 1,
            DisplayCommandsReused: cacheHit ? segmentsWritten : 0,
            DisplayCommandsBuilt: cacheHit ? 0 : segmentsWritten,
            RowsDamaged: rowsRendered,
            RowsFingerprintRejected: 0,
            RowsSemanticallyCompared: rowsRendered,
            ChangedRuns: rowsRendered,
            CellsChanged: 0,
            OutputCharacters: 0,
            FullRepaint: true,
            Backpressured: false));
    }

    private void FullRender(
        TerminalSize size,
        int usedLines,
        FullRenderClearMode clearMode,
        ScrollbackBatch? scrollback = null,
        bool recovery = false)
    {
        const int viewportTop = 0;
        var acceptedHardwareCursorRow = Math.Max(0, usedLines - 1);
        WriteFrame(BuildFullFrame, recovery, containsScrollback: scrollback is not null);

        _hardwareCursorRow = acceptedHardwareCursorRow;
        CommitFrame(size, usedLines);

        void BuildFullFrame(AnsiFrameWriter output)
        {
            output.Write(BeginSynchronizedOutput);
            if (recovery && _scrollbackUncertain)
                output.Write(ClearScrollback);
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

    private ValueTask<ScrollbackCommitResult> PublishScrollbackAsync(
        ScrollbackBatch batch,
        ScrollbackCommitOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!_terminalCertain && !options.ClearAndReplayWhenUncertain)
                return ValueTask.FromResult(new ScrollbackCommitResult(
                    ScrollbackCommitStatus.Failed, batch.FirstSequence,
                    new InvalidOperationException("Terminal state is uncertain and clear-and-replay recovery is disabled.")));
            FullRender(_terminal.GetSize(), TuiCapture.GetUsedLineCount(_currentBuffer!.Grid),
                FullRenderClearMode.Screen, batch, recovery: !_terminalCertain);
            return ValueTask.FromResult(new ScrollbackCommitResult(ScrollbackCommitStatus.Written,
                checked(batch.FirstSequence + batch.Rows.Count)));
        }
        catch (TerminalBackpressureException)
        {
            return ValueTask.FromResult(new ScrollbackCommitResult(ScrollbackCommitStatus.Backpressured, batch.FirstSequence));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(new ScrollbackCommitResult(ScrollbackCommitStatus.Failed, batch.FirstSequence, exception));
        }
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
            WriteCursorState(output, grid, lineCount, 0, ref hardwareRow);
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
        _hasPreviousFrame = true;
    }

    private void WriteFrame(FrameBuilder builder, bool recovery = false, bool containsScrollback = false)
    {
        if (!_terminalCertain && !recovery)
            throw new InvalidOperationException("Terminal state is uncertain; this renderer cannot safely publish another frame.");
        _output.Clear();
        builder(_output);
        using var lease = _output.CreateLease();
        var result = _transport.TryWriteFrameAsync(lease).GetAwaiter().GetResult();
        _output.Clear();
        if (result.Status == TerminalWriteStatus.Failed)
        {
            _terminalCertain = false;
            _scrollbackUncertain |= containsScrollback;
            throw new InvalidOperationException("Managed terminal publication failed; terminal state is uncertain.", result.Error);
        }
        if (result.Status == TerminalWriteStatus.Backpressured)
            throw new TerminalBackpressureException();
        if (recovery)
        {
            _terminalCertain = true;
            _scrollbackUncertain = false;
        }
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
