using System.Buffers;
using System.Diagnostics;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

public sealed class TuiRenderer : IDisposable
{
    private static readonly char[] CursorHome = ['\x1b', '[', 'H'];
    private static readonly char[] CursorHide = ['\x1b', '[', '?', '2', '5', 'l'];
    private static readonly char[] CursorShow = ['\x1b', '[', '?', '2', '5', 'h'];
    private readonly ITerminal _terminal;
    private readonly ArrayPool<char> _charPool;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TerminalGrid? _currentGrid;
    private TerminalGrid? _previousGrid;
    private bool _hasPreviousFrame;
    private bool _disposed;

    public TuiRenderer(ITerminal terminal)
        : this(terminal, ArrayPool<char>.Shared)
    {
    }

    internal TuiRenderer(ITerminal terminal, ArrayPool<char> charPool)
    {
        _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
        _charPool = charPool;
    }

    public void Render(IComponent root, Theme? theme = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        var size = _terminal.GetSize();
        EnsureGrid(size);

        var context = new RenderContext(size.Width, size.Height, theme ?? Theme.Default, elapsed: _clock.Elapsed);
        _currentGrid!.Clear();

        var writer = new SegmentWriter(_currentGrid);
        root.Render(in context, size.Width, ref writer);

        var bufferLength = checked(size.Height * ((size.Width * 64) + 16));
        var buffer = _charPool.Rent(bufferLength);

        try
        {
            if (!_hasPreviousFrame)
            {
                _terminal.Write(CursorHome);
            }

            var written = _currentGrid.WriteDifferentialAnsi(
                _hasPreviousFrame ? _previousGrid : null,
                buffer);

            if (!AppendCursorState(_currentGrid, buffer, ref written))
            {
                written = buffer.Length;
            }

            _terminal.Write(buffer.AsSpan(0, written));
            (_currentGrid, _previousGrid) = (_previousGrid, _currentGrid);
            _hasPreviousFrame = true;
        }
        finally
        {
            _charPool.Return(buffer);
        }
    }

    private static bool AppendCursorState(TerminalGrid grid, Span<char> buffer, ref int written)
    {
        if (!TryAppend(buffer, ref written, grid.HasTerminalCursor ? CursorShow : CursorHide))
        {
            return false;
        }

        return !grid.HasTerminalCursor || TryAppendCursorMove(buffer, ref written, grid.TerminalCursorX, grid.TerminalCursorY);
    }

    private static bool TryAppendCursorMove(Span<char> destination, ref int written, int x, int y)
    {
        return TryAppend(destination, ref written, "\x1b[") &&
               TryAppendInt(destination, ref written, y + 1) &&
               TryAppend(destination, ref written, ";") &&
               TryAppendInt(destination, ref written, x + 1) &&
               TryAppend(destination, ref written, "H");
    }

    private static bool TryAppend(Span<char> destination, ref int written, ReadOnlySpan<char> value)
    {
        if (destination.Length - written < value.Length)
        {
            return false;
        }

        value.CopyTo(destination[written..]);
        written += value.Length;
        return true;
    }

    private static bool TryAppendInt(Span<char> destination, ref int written, int value)
    {
        if (!value.TryFormat(destination[written..], out var charsWritten))
        {
            return false;
        }

        written += charsWritten;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _currentGrid?.Dispose();
        _previousGrid?.Dispose();
        _terminal.Dispose();
    }

    private void EnsureGrid(TerminalSize size)
    {
        if (_currentGrid is not null &&
            _previousGrid is not null &&
            _currentGrid.Width == size.Width &&
            _currentGrid.Height == size.Height)
        {
            return;
        }

        _currentGrid?.Dispose();
        _previousGrid?.Dispose();
        _currentGrid = new TerminalGrid(size.Width, size.Height);
        _previousGrid = new TerminalGrid(size.Width, size.Height);
        _hasPreviousFrame = false;
    }
}
