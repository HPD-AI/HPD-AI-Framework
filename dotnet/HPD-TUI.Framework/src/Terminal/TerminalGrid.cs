using System.Buffers;
using System.Globalization;
using HPD.TUI.Core;
using HPD.TUI.Utilities;

namespace HPD.TUI.Terminal;

/// <summary>Stores a terminal frame in pooled cell and grapheme buffers.</summary>
public sealed class TerminalGrid : ISegmentSink, IDisposable
{
    private static readonly char[] BlankGrapheme = [' '];
    private readonly ArrayPool<Cell> _cellPool;
    private readonly ArrayPool<char> _characterPool;
    private readonly Dictionary<TerminalHyperlink, TerminalHyperlinkId> _hyperlinkIds = [];
    private readonly List<TerminalHyperlink> _hyperlinks = [];
    private Cell[]? _cells;
    private char[]? _graphemes;
    private int _graphemeLength;
    private int _cursorX;
    private int _cursorY;

    /// <summary>Creates a terminal grid.</summary>
    public TerminalGrid(int width, int height)
        : this(width, height, ArrayPool<Cell>.Shared, ArrayPool<char>.Shared) { }

    internal TerminalGrid(int width, int height, ArrayPool<Cell> cellPool, ArrayPool<char> characterPool)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _cellPool = cellPool;
        _characterPool = characterPool;
        Width = width;
        Height = height;
        _cells = cellPool.Rent(checked(width * height));
        _graphemes = characterPool.Rent(Math.Max(256, checked(width * height * 2)));
        Clear();
    }

    /// <summary>Gets the width in terminal columns.</summary>
    public int Width { get; }
    /// <summary>Gets the height in terminal rows.</summary>
    public int Height { get; }
    /// <inheritdoc />
    public int CursorX => _cursorX;
    /// <inheritdoc />
    public int CursorY => _cursorY;
    /// <summary>Gets whether a visible cursor was requested.</summary>
    public bool HasTerminalCursor { get; private set; }
    /// <summary>Gets the requested cursor column.</summary>
    public int TerminalCursorX { get; private set; }
    /// <summary>Gets the requested cursor row.</summary>
    public int TerminalCursorY { get; private set; }

    /// <summary>Clears the frame while retaining pooled storage.</summary>
    public void Clear()
    {
        ThrowIfDisposed();
        _cursorX = _cursorY = _graphemeLength = 0;
        _hyperlinkIds.Clear();
        _hyperlinks.Clear();
        HasTerminalCursor = false;
        TerminalCursorX = TerminalCursorY = 0;
        _cells.AsSpan(0, Width * Height).Fill(Cell.Blank);
    }

    /// <summary>Gets a cell descriptor.</summary>
    public Cell GetCell(int x, int y)
    {
        ThrowIfDisposed();
        return Contains(x, y) ? _cells![GetIndex(x, y)] : Cell.Blank;
    }

    /// <summary>Gets the grapheme represented by a leading cell.</summary>
    public ReadOnlySpan<char> GetGrapheme(Cell cell)
    {
        ThrowIfDisposed();
        if (cell.IsContinuation) return [];
        return cell.GraphemeLength == 0
            ? BlankGrapheme
            : _graphemes.AsSpan(cell.GraphemeOffset, cell.GraphemeLength);
    }

    /// <summary>Gets the first Unicode scalar of a leading cell for semantic inspection.</summary>
    public Rune GetLeadingRune(Cell cell)
    {
        var grapheme = GetGrapheme(cell);
        return Rune.DecodeFromUtf16(grapheme, out var rune, out _) == OperationStatus.Done
            ? rune
            : Rune.ReplacementChar;
    }

    /// <summary>Gets the logical hyperlink represented by a cell.</summary>
    public TerminalHyperlink? GetHyperlink(Cell cell)
    {
        if (cell.HyperlinkId.IsNone) return null;
        var index = cell.HyperlinkId.Value - 1;
        return (uint)index < (uint)_hyperlinks.Count ? _hyperlinks[index] : null;
    }

    /// <summary>Compares the visual content of one cell across grids.</summary>
    public bool CellEquals(TerminalGrid other, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(other);
        var left = GetCell(x, y);
        var right = other.GetCell(x, y);
        return left.Style == right.Style && left.DisplayWidth == right.DisplayWidth &&
               left.IsContinuation == right.IsContinuation &&
               Equals(GetHyperlink(left), other.GetHyperlink(right)) &&
               GetGrapheme(left).SequenceEqual(other.GetGrapheme(right));
    }

    /// <inheritdoc />
    public bool Write(scoped ReadOnlySpan<char> text, Style style, TerminalRunMetadata metadata = default)
    {
        ThrowIfDisposed();
        var link = ResolveOrRegister(metadata.Hyperlink);
        while (!text.IsEmpty)
        {
            var length = StringInfo.GetNextTextElementLength(text);
            if (!WriteGrapheme(text[..length], style, link)) return false;
            text = text[length..];
        }
        return true;
    }

    /// <inheritdoc />
    public bool WriteLineBreak()
    {
        ThrowIfDisposed();
        _cursorX = 0;
        return ++_cursorY < Height;
    }

    /// <inheritdoc />
    public void MoveTo(int x, int y)
    {
        ThrowIfDisposed();
        _cursorX = Math.Clamp(x, 0, Width - 1);
        _cursorY = Math.Clamp(y, 0, Height - 1);
    }

    /// <inheritdoc />
    public void SetTerminalCursor(int x, int y)
    {
        ThrowIfDisposed();
        HasTerminalCursor = Contains(x, y);
        if (HasTerminalCursor) { TerminalCursorX = x; TerminalCursorY = y; }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_cells is null || _graphemes is null) return;
        _cellPool.Return(_cells, clearArray: true);
        _characterPool.Return(_graphemes, clearArray: true);
        _cells = null;
        _graphemes = null;
    }

    private bool WriteGrapheme(ReadOnlySpan<char> grapheme, Style style, TerminalHyperlinkId link)
    {
        if (grapheme.SequenceEqual("\r")) return true;
        if (grapheme.SequenceEqual("\n") || grapheme.SequenceEqual("\r\n")) return WriteLineBreak();
        if (grapheme.SequenceEqual("\t"))
        {
            var spaces = 4 - (_cursorX & 3);
            for (var index = 0; index < spaces; index++)
                if (!WriteGrapheme(" ", style, link)) return false;
            return true;
        }
        foreach (var ch in grapheme)
            if (TerminalTextSafety.IsUnsafe(ch)) return WriteGrapheme("�", style, link);
        var width = GetWidth(grapheme);
        if (width == 0) return AppendToPrevious(grapheme);
        if (width > Width) return WriteGrapheme("�", style, link);
        if (_cursorX + width > Width) { _cursorX = 0; _cursorY++; }
        if (_cursorY >= Height) return false;
        var offset = Append(grapheme);
        var cell = new Cell(offset, checked((ushort)grapheme.Length), checked((byte)width), style, link);
        _cells![GetIndex(_cursorX, _cursorY)] = cell;
        for (var i = 1; i < width; i++) _cells[GetIndex(_cursorX + i, _cursorY)] = cell with { IsContinuation = true };
        _cursorX += width;
        return true;
    }

    private bool AppendToPrevious(ReadOnlySpan<char> suffix)
    {
        if (_cursorX == 0 || _cursorY >= Height) return true;
        var rowStart = GetIndex(0, _cursorY);
        var index = GetIndex(_cursorX - 1, _cursorY);
        while (index > rowStart && _cells![index].IsContinuation) index--;
        var existing = _cells![index];
        if (existing.GraphemeLength == 0) return true;
        var combinedLength = checked(existing.GraphemeLength + suffix.Length);
        EnsureCapacity(combinedLength);
        var offset = _graphemeLength;
        GetGrapheme(existing).CopyTo(_graphemes.AsSpan(offset));
        suffix.CopyTo(_graphemes.AsSpan(offset + existing.GraphemeLength));
        _graphemeLength += combinedLength;
        var replacement = existing with { GraphemeOffset = offset, GraphemeLength = checked((ushort)combinedLength) };
        _cells[index] = replacement;
        for (var i = 1; i < replacement.DisplayWidth; i++) _cells[index + i] = replacement with { IsContinuation = true };
        return true;
    }

    private int Append(ReadOnlySpan<char> grapheme)
    {
        EnsureCapacity(grapheme.Length);
        var offset = _graphemeLength;
        grapheme.CopyTo(_graphemes.AsSpan(offset));
        _graphemeLength += grapheme.Length;
        return offset;
    }

    private void EnsureCapacity(int additional)
    {
        if (additional <= _graphemes!.Length - _graphemeLength) return;
        var next = _characterPool.Rent(Math.Max(checked(_graphemeLength + additional), checked(_graphemes.Length * 2)));
        _graphemes.AsSpan(0, _graphemeLength).CopyTo(next);
        _characterPool.Return(_graphemes, clearArray: true);
        _graphemes = next;
    }

    private TerminalHyperlinkId ResolveOrRegister(TerminalHyperlink? hyperlink)
    {
        if (hyperlink is null) return TerminalHyperlinkId.None;
        if (_hyperlinkIds.TryGetValue(hyperlink, out var id)) return id;
        id = new TerminalHyperlinkId(_hyperlinks.Count + 1);
        _hyperlinks.Add(hyperlink);
        _hyperlinkIds.Add(hyperlink, id);
        return id;
    }

    private static int GetWidth(ReadOnlySpan<char> grapheme)
    {
        var width = 0;
        var regionalIndicators = 0;
        var emojiPresentation = false;
        var runes = new RuneEnumerator(grapheme);
        while (runes.MoveNext())
        {
            var rune = runes.Current;
            width = Math.Max(width, UnicodeWidth.GetWidth(rune));
            emojiPresentation |= rune.Value == 0xFE0F;
            if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF) regionalIndicators++;
        }
        if (emojiPresentation || regionalIndicators >= 2) width = Math.Max(width, 2);
        return width;
    }

    private bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
    private int GetIndex(int x, int y) => y * Width + x;
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_cells is null || _graphemes is null, this);
}
