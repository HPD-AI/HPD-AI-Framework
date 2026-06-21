using System.Buffers;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Terminal;

public sealed class TerminalGrid : ISegmentSink, IDisposable
{
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

}
