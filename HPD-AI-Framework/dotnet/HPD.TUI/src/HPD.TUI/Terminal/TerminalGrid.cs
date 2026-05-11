using System.Buffers;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Terminal;

public sealed class TerminalGrid : ISegmentSink, IDisposable
{
    private static readonly char[] ResetSequence = ['\x1b', '[', '0', 'm'];
    private readonly ArrayPool<Cell> _pool;
    private Cell[]? _cells;
    private int _cursorX;
    private int _cursorY;

    public TerminalGrid(int width, int height)
        : this(width, height, ArrayPool<Cell>.Shared)
    {
    }

    internal TerminalGrid(int width, int height, ArrayPool<Cell> pool)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _pool = pool;
        Width = width;
        Height = height;
        _cells = pool.Rent(width * height);
        Clear();
    }

    public int Width { get; }

    public int Height { get; }

    public int CursorX => _cursorX;

    public int CursorY => _cursorY;

    public bool HasTerminalCursor { get; private set; }

    public int TerminalCursorX { get; private set; }

    public int TerminalCursorY { get; private set; }

    public void Clear()
    {
        ThrowIfDisposed();

        _cursorX = 0;
        _cursorY = 0;
        HasTerminalCursor = false;
        TerminalCursorX = 0;
        TerminalCursorY = 0;

        var count = Width * Height;
        for (var i = 0; i < count; i++)
        {
            _cells![i] = Cell.Blank;
        }
    }

    public Cell GetCell(int x, int y)
    {
        ThrowIfDisposed();

        if (!Contains(x, y))
        {
            return Cell.Blank;
        }

        return _cells![GetIndex(x, y)];
    }

    public void SetCell(int x, int y, Cell cell)
    {
        ThrowIfDisposed();

        if (!Contains(x, y))
        {
            return;
        }

        _cells![GetIndex(x, y)] = cell;
    }

    public bool Write(scoped ReadOnlySpan<char> text, Style style)
    {
        ThrowIfDisposed();

        var enumerator = new RuneEnumerator(text);
        while (enumerator.MoveNext())
        {
            if (!WriteRune(enumerator.Current, style))
            {
                return false;
            }
        }

        return true;
    }

    public bool WriteLineBreak()
    {
        ThrowIfDisposed();

        _cursorX = 0;
        _cursorY++;
        return _cursorY < Height;
    }

    public void MoveTo(int x, int y)
    {
        ThrowIfDisposed();

        _cursorX = Math.Clamp(x, 0, Width - 1);
        _cursorY = Math.Clamp(y, 0, Height - 1);
    }

    public void SetTerminalCursor(int x, int y)
    {
        ThrowIfDisposed();

        HasTerminalCursor = Contains(x, y);
        if (!HasTerminalCursor)
        {
            return;
        }

        TerminalCursorX = x;
        TerminalCursorY = y;
    }

    public int WriteAnsi(Span<char> destination)
    {
        ThrowIfDisposed();

        var written = 0;
        Style? currentStyle = null;
        Span<char> styleBuffer = stackalloc char[64];
        Span<char> runeBuffer = stackalloc char[2];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = GetCell(x, y);
                if (cell.IsContinuation)
                {
                    continue;
                }

                if (currentStyle != cell.Style)
                {
                    if (currentStyle is not null && !TryAppend(destination, ref written, ResetSequence))
                    {
                        return written;
                    }

                    var styleLength = cell.Style.WriteAnsiPrefix(styleBuffer);
                    if (styleLength == 0 || !TryAppend(destination, ref written, styleBuffer[..styleLength]))
                    {
                        return written;
                    }

                    currentStyle = cell.Style;
                }

                if (!cell.Rune.TryEncodeToUtf16(runeBuffer, out var charsWritten) ||
                    !TryAppend(destination, ref written, runeBuffer[..charsWritten]))
                {
                    return written;
                }
            }

            if (currentStyle is not null && !TryAppend(destination, ref written, ResetSequence))
            {
                return written;
            }

            currentStyle = null;

            if (y < Height - 1 && !TryAppend(destination, ref written, "\r\n"))
            {
                return written;
            }
        }

        return written;
    }

    public int WriteDifferentialAnsi(TerminalGrid? previous, Span<char> destination)
    {
        ThrowIfDisposed();

        if (previous is null || previous.Width != Width || previous.Height != Height)
        {
            return WriteAnsi(destination);
        }

        previous.ThrowIfDisposed();

        var written = 0;
        Style? currentStyle = null;
        Span<char> styleBuffer = stackalloc char[64];
        Span<char> runeBuffer = stackalloc char[2];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var cell = GetCell(x, y);
                if (cell.IsContinuation || cell == previous.GetCell(x, y))
                {
                    continue;
                }

                if (!TryAppendCursorMove(destination, ref written, x, y))
                {
                    return written;
                }

                if (currentStyle != cell.Style)
                {
                    var styleLength = cell.Style.WriteAnsiPrefix(styleBuffer);
                    if (styleLength == 0 || !TryAppend(destination, ref written, styleBuffer[..styleLength]))
                    {
                        return written;
                    }

                    currentStyle = cell.Style;
                }

                if (!cell.Rune.TryEncodeToUtf16(runeBuffer, out var charsWritten) ||
                    !TryAppend(destination, ref written, runeBuffer[..charsWritten]))
                {
                    return written;
                }
            }
        }

        if (currentStyle is not null)
        {
            TryAppend(destination, ref written, ResetSequence);
        }

        return written;
    }

    public void Dispose()
    {
        var cells = _cells;
        if (cells is null)
        {
            return;
        }

        _cells = null;
        _pool.Return(cells, clearArray: true);
    }

    private bool WriteRune(Rune rune, Style style)
    {
        if (rune.Value is '\r')
        {
            return true;
        }

        if (rune.Value is '\n')
        {
            return WriteLineBreak();
        }

        var width = UnicodeWidth.GetWidth(rune);
        if (width == 0)
        {
            return true;
        }

        if (_cursorX + width > Width)
        {
            _cursorX = 0;
            _cursorY++;
        }

        if (_cursorY >= Height)
        {
            return false;
        }

        SetCell(_cursorX, _cursorY, new Cell(rune, style));

        if (width == 2 && _cursorX + 1 < Width)
        {
            SetCell(_cursorX + 1, _cursorY, new Cell(default, style, true));
        }

        _cursorX += width;
        return true;
    }

    private bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    private int GetIndex(int x, int y) => y * Width + x;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_cells is null, this);
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

    private static bool TryAppendCursorMove(Span<char> destination, ref int written, int x, int y)
    {
        if (!TryAppend(destination, ref written, "\x1b["))
        {
            return false;
        }

        if (!TryAppendInt(destination, ref written, y + 1) ||
            !TryAppend(destination, ref written, ";") ||
            !TryAppendInt(destination, ref written, x + 1) ||
            !TryAppend(destination, ref written, "H"))
        {
            return false;
        }

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
}
