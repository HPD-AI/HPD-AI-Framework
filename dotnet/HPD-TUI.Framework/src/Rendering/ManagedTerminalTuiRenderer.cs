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
    private static readonly char[] EnterAlternateScreen = ['\x1b', '[', '?', '1', '0', '4', '9', 'h'];
    private static readonly char[] LeaveAlternateScreen = ['\x1b', '[', '?', '1', '0', '4', '9', 'l'];
    private static readonly char[] VisibleEpochBoundary = ['\r', '\n', '-', '-', '-', ' ', 'n', 'e', 'w', ' ', 'p', 'r', 'e', 's', 'e', 'n', 't', 'a', 't', 'i', 'o', 'n', ' ', 'e', 'p', 'o', 'c', 'h', ' ', '-', '-', '-', '\r', '\n'];
    private readonly ITerminal _terminal;
    private readonly TerminalPublicationCoordinator _publisher;
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
    private bool _scrollbackUncertain;
    private bool _disposed;
    private TimeSpan _lastEncodeDuration;
    private TimeSpan _lastDiffDuration;
    private TimeSpan _lastOutputDuration;
    private ScreenDiffMetrics _lastDiffMetrics;
    private int _lastOutputCharacters;
    private bool _lastFullRepaint;
    private readonly ManagedTerminalCapabilityProfile _capabilities;
    private readonly bool _splitFooterEnabled;
    private readonly ManagedTerminalRecoveryPolicy _recoveryPolicy;
    private long _presentationEpoch;
    private bool _hasPresentationEpoch;
    private bool _alternateScreen;
    private bool _aborted;
    private bool _shutdown;
    private bool TerminalCertain => _publisher.State.Certainty == TerminalCertainty.Known;

    /// <summary>Gets the active presentation epoch. It advances when terminal-visible history cannot be retracted.</summary>
    public long PresentationEpoch => _presentationEpoch;

    /// <summary>Gets whether verified capabilities permit append-only history with a pinned footer.</summary>
    public bool SupportsManagedScrollback => _splitFooterEnabled;

    public ManagedTerminalTuiRenderer(ITerminal terminal)
        : this(terminal, new SynchronousTerminalOutputTransport(terminal), ManagedTerminalCapabilityProfile.Detect(terminal))
    {
    }

    /// <summary>Creates a renderer that publishes through the supplied output transport.</summary>
    /// <param name="terminal">The terminal used for sizing and cursor visibility.</param>
    /// <param name="transport">The single-writer frame transport.</param>
    public ManagedTerminalTuiRenderer(ITerminal terminal, ITerminalOutputTransport transport)
        : this(terminal, transport, ManagedTerminalCapabilityProfile.Detect(terminal))
    {
    }

    /// <summary>Creates a renderer with an explicit, immutable capability profile.</summary>
    public ManagedTerminalTuiRenderer(
        ITerminal terminal,
        ITerminalOutputTransport transport,
        ManagedTerminalCapabilityProfile capabilities,
        ManagedTerminalFallbackPolicy fallbackPolicy = ManagedTerminalFallbackPolicy.BoundedScreen,
        ManagedTerminalRecoveryPolicy recoveryPolicy = ManagedTerminalRecoveryPolicy.VisibleEpochBoundary)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _publisher = new TerminalPublicationCoordinator(transport);
        _scrollbackJournal = new ManagedScrollbackJournal(PublishScrollbackAsync);
        _capabilities = capabilities;
        _recoveryPolicy = recoveryPolicy;
        _splitFooterEnabled = capabilities.SupportsSplitFooter;
        if (!_splitFooterEnabled && fallbackPolicy == ManagedTerminalFallbackPolicy.Reject)
            throw new NotSupportedException("Managed split-footer publication requires absolute cursor addressing, erase-in-line, controllable autowrap, and synchronized output.");
    }

    internal ValueTask WaitUntilWritableAsync(CancellationToken cancellationToken)
        => _publisher.WaitUntilWritableAsync(cancellationToken);

    public bool TrackHardwareCursor { get; set; }

    public IHpdTuiPerformanceEventSink? PerformanceSink { get; set; }

    public void Render(IComponent root, Theme? theme = null, ScrollbackBatch? scrollback = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aborted) throw new InvalidOperationException("The managed terminal presentation was aborted by its recovery policy.");
        ArgumentNullException.ThrowIfNull(root);
        if (scrollback is not null && !_splitFooterEnabled)
            throw new NotSupportedException("Scrollback publication is disabled because the terminal capability profile does not satisfy the split-footer protocol.");
        var sink = PerformanceSink;
        var startTimestamp = sink is null ? 0 : Stopwatch.GetTimestamp();
        var size = _terminal.GetSize();
        var hadPreviousFrame = _hasPreviousFrame;
        var sizeChanged = _previousWidth != 0 && (
            _previousWidth != size.Width ||
            _previousHeight != size.Height);
        if (sizeChanged) StartPresentationEpoch();
        if (scrollback is not null && !_hasPresentationEpoch)
        {
            _presentationEpoch = scrollback.PresentationEpoch;
            _hasPresentationEpoch = true;
            _scrollbackJournal.StartEpoch(_presentationEpoch);
        }
        else if (scrollback is not null && scrollback.PresentationEpoch != _presentationEpoch)
            throw new InvalidOperationException($"Scrollback batch epoch {scrollback.PresentationEpoch} does not match renderer epoch {_presentationEpoch}.");
        EnsureBuffer(size.Width, size.Height);

        var context = new RenderContext(size.Width, size.Height, theme ?? Theme.Default, elapsed: _clock.Elapsed);
        var displayStart = sink is null ? 0 : Stopwatch.GetTimestamp();
        var cacheHit = _displayList.Prepare(root, in context, size.Width);
        var displayDuration = sink is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(displayStart);
        if (cacheHit && hadPreviousFrame && !sizeChanged && TerminalCertain && scrollback is null)
        {
            PublishRenderCompleted(sink, startTimestamp, displayDuration, TimeSpan.Zero, false);
            return;
        }
        var rasterStart = sink is null ? 0 : Stopwatch.GetTimestamp();
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
        var rasterDuration = sink is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(rasterStart);

        var usedLines = TuiCapture.GetUsedLineCount(_currentBuffer.Grid);
        ResetPublicationMetrics();

        try
        {
            if (!TerminalCertain)
            {
                Recover(size, usedLines);
                if (scrollback is not null)
                    throw new InvalidOperationException(
                        "A batch involved in uncertain output cannot be retried; prepare it in the new presentation epoch.");
                PublishRenderCompleted(sink, startTimestamp, displayDuration, rasterDuration, false);
                return;
            }

            if (!_splitFooterEnabled)
            {
                BoundedRender(size, usedLines);
                PublishRenderCompleted(sink, startTimestamp, displayDuration, rasterDuration, false);
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
                PublishRenderCompleted(sink, startTimestamp, displayDuration, rasterDuration, false);
                return;
            }

            if (!hadPreviousFrame || sizeChanged)
            {
                FullRender(size, usedLines, FullRenderClearMode.Screen);
                PublishRenderCompleted(sink, startTimestamp, displayDuration, rasterDuration, false);
                return;
            }

            var screenChanged = false;
            var compared = 0;
            var rejected = 0;
            for (var row = 0; row < size.Height; row++)
            {
                compared++;
                if (_currentBuffer.RowEquals(_previousBuffer!, row)) rejected++;
                else screenChanged = true;
            }
            if (!screenChanged)
            {
                _lastDiffMetrics = new(0, rejected, compared, 0, 0);
                PublishCursorOnlyIfChanged(_currentBuffer.Grid, usedLines);
                CommitFrame(size, usedLines);
                PublishRenderCompleted(sink, startTimestamp, displayDuration, rasterDuration, false);
                return;
            }

            PatchChangedRuns(size, usedLines);
            PublishRenderCompleted(sink, startTimestamp, displayDuration, rasterDuration, false);
        }
        catch (TerminalBackpressureException)
        {
            PublishRenderCompleted(sink, startTimestamp, displayDuration, rasterDuration, true);
            throw;
        }
    }

    /// <summary>Starts a new visible history epoch after a resize, model rebase, or uncertain-output recovery.</summary>
    /// <returns>The new nonnegative epoch.</returns>
    public long StartPresentationEpoch()
    {
        _presentationEpoch = _hasPresentationEpoch ? checked(_presentationEpoch + 1) : 0;
        _hasPresentationEpoch = true;
        _scrollbackJournal.StartEpoch(_presentationEpoch);
        _hasPreviousFrame = false;
        return _presentationEpoch;
    }

    /// <summary>Publishes external-process output through the same ordered lease queue and invalidates the live anchor.</summary>
    public void PublishExternalOutput(ReadOnlySpan<char> output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aborted) throw new InvalidOperationException("The managed terminal presentation is aborted.");
        var state = _publisher.State with { Certainty = TerminalCertainty.Uncertain };
        var result = _publisher.TryPublish(output, acceptedState: state);
        if (result.Status != TerminalWriteStatus.Written)
            throw new InvalidOperationException("External terminal output was not completely accepted.", result.Error);
    }

    /// <summary>Applies the explicit policy for a model edit that targets terminal-visible committed history.</summary>
    public void RebaseCommittedHistory(ManagedTerminalRecoveryPolicy policy)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_currentBuffer is null)
        {
            StartPresentationEpoch();
            return;
        }
        MarkUncertain();
        Recover(_terminal.GetSize(), TuiCapture.GetUsedLineCount(_currentBuffer.Grid), policy);
    }

    /// <summary>Publishes terminal shutdown controls through the ordered publisher.</summary>
    public void Shutdown()
    {
        if (_disposed || _shutdown) return;
        _shutdown = true;
        _output.Clear();
        if (_alternateScreen) _output.Write(LeaveAlternateScreen);
        if ((_capabilities.Features & ManagedTerminalFeatures.ControllableAutowrap) != 0) _output.Write(EnableAutowrap);
        _output.Write(ShowHardwareCursor);
        var result = _publisher.TryPublish(_output.WrittenSpan,
            acceptedState: _publisher.State with { CursorVisible = true });
        _output.Clear();
    }

    private void PatchChangedRuns(TerminalSize size, int usedLines)
    {
        var acceptedHardwareCursorRow = _hardwareCursorRow;
        WriteFrame(output =>
        {
            output.Write(BeginSynchronizedOutput);
            var diffStart = PerformanceSink is null ? 0 : Stopwatch.GetTimestamp();
            _lastDiffMetrics = AnsiGridRenderer.WriteDifferential(_previousBuffer!, _currentBuffer!, output);
            _lastDiffDuration = PerformanceSink is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(diffStart);
            WriteCursorState(output, _currentBuffer!.Grid, usedLines, 0, ref acceptedHardwareCursorRow);
            output.Write(EndSynchronizedOutput);
        }, acceptedState: AcceptedState(size,
            TrackHardwareCursor && _currentBuffer!.Grid.HasTerminalCursor
                ? Math.Clamp(_currentBuffer.Grid.TerminalCursorY, 0, Math.Max(0, usedLines - 1))
                : acceptedHardwareCursorRow,
            TrackHardwareCursor && _currentBuffer!.Grid.HasTerminalCursor));
        _hardwareCursorRow = acceptedHardwareCursorRow;
        CommitFrame(size, usedLines);
    }

    private void BoundedRender(TerminalSize size, int usedLines)
    {
        _lastFullRepaint = true;
        var cursorRow = Math.Max(0, usedLines - 1);
        WriteFrame(output =>
        {
            output.Write('\r');
            WriteBoundedLines(output, _currentBuffer!.Grid, usedLines);
        }, acceptedState: AcceptedState(size, cursorRow, false));
        _hardwareCursorRow = cursorRow;
        CommitFrame(size, usedLines);
    }

    private void Recover(TerminalSize size, int usedLines) => Recover(size, usedLines, _recoveryPolicy);

    private void Recover(TerminalSize size, int usedLines, ManagedTerminalRecoveryPolicy policy)
    {
        switch (policy)
        {
            case ManagedTerminalRecoveryPolicy.ClearAndReplay:
                if ((_capabilities.Features & ManagedTerminalFeatures.ClearScrollback) == 0)
                    throw new InvalidOperationException("Clear-and-replay recovery requires explicit CSI 3 J capability.");
                StartPresentationEpoch();
                FullRender(size, usedLines, FullRenderClearMode.Screen, recovery: true);
                break;
            case ManagedTerminalRecoveryPolicy.VisibleEpochBoundary:
                StartPresentationEpoch();
                WriteFrame(output =>
                {
                    output.Write(VisibleEpochBoundary);
                    if (_splitFooterEnabled) output.Write(ClearScreenAndCursorHome);
                    WriteLines(output, _currentBuffer!.Grid, 0, usedLines - 1);
                }, recovery: true, acceptedState: AcceptedState(size, Math.Max(0, usedLines - 1), false));
                CommitFrame(size, usedLines);
                break;
            case ManagedTerminalRecoveryPolicy.SwitchToAlternateScreen:
                StartPresentationEpoch();
                WriteFrame(output =>
                {
                    output.Write(EnterAlternateScreen);
                    output.Write(ClearScreenAndCursorHome);
                    WriteLines(output, _currentBuffer!.Grid, 0, usedLines - 1);
                }, recovery: true, acceptedState: AcceptedState(size, Math.Max(0, usedLines - 1), false));
                _alternateScreen = true;
                CommitFrame(size, usedLines);
                break;
            case ManagedTerminalRecoveryPolicy.Abort:
                _aborted = true;
                throw new InvalidOperationException("Managed terminal output aborted after terminal state became uncertain.");
        }
    }

    private void PublishRenderCompleted(
        IHpdTuiPerformanceEventSink? sink,
        long startTimestamp,
        TimeSpan displayDuration,
        TimeSpan rasterDuration,
        bool backpressured)
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
            DiffDuration: _lastDiffDuration,
            EncodeDuration: _lastEncodeDuration - _lastDiffDuration,
            OutputDuration: _lastOutputDuration,
            ComponentsMeasured: 0,
            ComponentsPainted: _displayList.ComponentsPainted,
            DisplayCommandsReused: _displayList.CommandsReused,
            DisplayCommandsBuilt: _displayList.CommandsBuilt,
            RowsDamaged: _displayList.DamagedRowCount,
            RowsFingerprintRejected: _lastDiffMetrics.RowsFingerprintRejected,
            RowsSemanticallyCompared: _lastDiffMetrics.RowsSemanticallyCompared,
            ChangedRuns: _lastDiffMetrics.ChangedRuns,
            CellsChanged: _lastDiffMetrics.CellsChanged,
            OutputCharacters: _lastOutputCharacters,
            FullRepaint: _lastFullRepaint,
            Backpressured: backpressured));
    }

    private void FullRender(
        TerminalSize size,
        int usedLines,
        FullRenderClearMode clearMode,
        ScrollbackBatch? scrollback = null,
        bool recovery = false)
    {
        const int viewportTop = 0;
        _lastFullRepaint = true;
        _lastDiffMetrics = new(size.Height, 0, 0, size.Height, size.Width * size.Height);
        var acceptedHardwareCursorRow = Math.Max(0, usedLines - 1);
        var watermark = scrollback is null
            ? _publisher.State.CommittedWatermark
            : checked(scrollback.FirstSequence + scrollback.Rows.Count);
        WriteFrame(BuildFullFrame, recovery, containsScrollback: scrollback is not null,
            acceptedState: AcceptedState(size, acceptedHardwareCursorRow,
                TrackHardwareCursor && _currentBuffer!.Grid.HasTerminalCursor, watermark));

        _hardwareCursorRow = acceptedHardwareCursorRow;
        CommitFrame(size, usedLines);

        void BuildFullFrame(AnsiFrameWriter output)
        {
            output.Write(BeginSynchronizedOutput);
            if (recovery && _scrollbackUncertain && (_capabilities.Features & ManagedTerminalFeatures.ClearScrollback) != 0)
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
            if (!TerminalCertain && options.RecoveryPolicy == ManagedTerminalRecoveryPolicy.Abort)
                return ValueTask.FromResult(new ScrollbackCommitResult(
                    ScrollbackCommitStatus.Failed, batch.FirstSequence,
                    new InvalidOperationException("Terminal state is uncertain and recovery policy is Abort.")));
            FullRender(_terminal.GetSize(), TuiCapture.GetUsedLineCount(_currentBuffer!.Grid),
                FullRenderClearMode.Screen, batch, recovery: !TerminalCertain);
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
        }, acceptedState: _publisher.State with
        {
            CursorRow = hardwareRow,
            CursorVisible = currentVisible,
            Certainty = TerminalCertainty.Known
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

    private void WriteFrame(
        FrameBuilder builder,
        bool recovery = false,
        bool containsScrollback = false,
        TerminalPresentationState? acceptedState = null)
    {
        if (!TerminalCertain && !recovery)
            throw new InvalidOperationException("Terminal state is uncertain; this renderer cannot safely publish another frame.");
        _output.Clear();
        var encodeStart = PerformanceSink is null ? 0 : Stopwatch.GetTimestamp();
        builder(_output);
        _lastEncodeDuration += PerformanceSink is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(encodeStart);
        _lastOutputCharacters += _output.Length;
        var outputStart = PerformanceSink is null ? 0 : Stopwatch.GetTimestamp();
        var result = _publisher.TryPublish(_output.WrittenSpan, acceptedState: acceptedState);
        _lastOutputDuration += PerformanceSink is null ? TimeSpan.Zero : Stopwatch.GetElapsedTime(outputStart);
        _output.Clear();
        if (result.Status == TerminalWriteStatus.Failed)
        {
            _scrollbackUncertain |= containsScrollback;
            throw new InvalidOperationException("Managed terminal publication failed; terminal state is uncertain.", result.Error);
        }
        if (result.Status == TerminalWriteStatus.Backpressured)
            throw new TerminalBackpressureException();
        if (recovery)
        {
            _scrollbackUncertain = false;
        }
    }

    private void ResetPublicationMetrics()
    {
        _lastEncodeDuration = TimeSpan.Zero;
        _lastDiffDuration = TimeSpan.Zero;
        _lastOutputDuration = TimeSpan.Zero;
        _lastDiffMetrics = default;
        _lastOutputCharacters = 0;
        _lastFullRepaint = false;
    }

    private void MarkUncertain()
    {
        var result = _publisher.TryPublish(ReadOnlySpan<char>.Empty,
            acceptedState: _publisher.State with { Certainty = TerminalCertainty.Uncertain });
        if (result.Status != TerminalWriteStatus.Written)
            throw new InvalidOperationException("Could not serialize the terminal uncertainty transition.", result.Error);
    }

    private delegate void FrameBuilder(AnsiFrameWriter output);

    private TerminalPresentationState AcceptedState(
        TerminalSize size,
        int cursorRow,
        bool cursorVisible,
        long? watermark = null) => new(
            _presentationEpoch,
            watermark ?? _publisher.State.CommittedWatermark,
            0,
            size.Height,
            cursorRow,
            cursorVisible,
            TerminalCertainty.Known);

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

    private static void WriteBoundedLines(AnsiFrameWriter output, TerminalGrid grid, int lineCount)
    {
        for (var y = 0; y < lineCount; y++)
        {
            if (y > 0) output.Write("\r\n");
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

        Shutdown();
        _disposed = true;
        _displayList.Dispose();
        _output.Dispose();
        _currentBuffer?.Dispose();
        _previousBuffer?.Dispose();
    }
}

internal sealed class TerminalBackpressureException : Exception;
