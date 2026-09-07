using HPD.TUI.Terminal;

namespace HPD.TUI.Rendering;

internal sealed class ScreenBuffer : IDisposable
{
    private ulong[]? _rowFingerprints;

    public ScreenBuffer(int width, int height)
    {
        Grid = new TerminalGrid(width, height);
        _rowFingerprints = new ulong[height];
    }

    public TerminalGrid Grid { get; }

    public int Width => Grid.Width;

    public int Height => Grid.Height;

    public ulong GetRowFingerprint(int row)
    {
        ObjectDisposedException.ThrowIf(_rowFingerprints is null, this);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        if (row >= Height)
            throw new ArgumentOutOfRangeException(nameof(row));
        return _rowFingerprints[row];
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_rowFingerprints is null, this);
        Grid.Clear();
        Array.Clear(_rowFingerprints);
    }

    public void CopyFrom(ScreenBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_rowFingerprints is null, this);
        Grid.CopyFrom(source.Grid);
        source._rowFingerprints!.AsSpan(0, Height).CopyTo(_rowFingerprints);
    }

    public void CopyRowsFrom(ScreenBuffer source, ReadOnlySpan<int> rows)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_rowFingerprints is null, this);
        Grid.CopyRowsFrom(source.Grid, rows);
        foreach (var row in rows)
            _rowFingerprints[row] = source._rowFingerprints![row];
    }

    public void ClearDamagedRows(ReadOnlySpan<bool> damagedRows)
    {
        ObjectDisposedException.ThrowIf(_rowFingerprints is null, this);
        if (damagedRows.Length != Height) throw new ArgumentException("Damage must describe every physical row.", nameof(damagedRows));
        for (var row = 0; row < Height; row++)
            if (damagedRows[row]) { Grid.ClearRow(row); _rowFingerprints[row] = 0; }
    }

    public void ComputeFinalRowFingerprints()
    {
        ObjectDisposedException.ThrowIf(_rowFingerprints is null, this);
        for (var row = 0; row < Height; row++)
            _rowFingerprints[row] = ComputeRowFingerprint(Grid, row);
    }

    public void ComputeFinalRowFingerprints(ReadOnlySpan<bool> damagedRows)
    {
        ObjectDisposedException.ThrowIf(_rowFingerprints is null, this);
        if (damagedRows.Length != Height) throw new ArgumentException("Damage must describe every physical row.", nameof(damagedRows));
        for (var row = 0; row < Height; row++)
            if (damagedRows[row]) _rowFingerprints[row] = ComputeRowFingerprint(Grid, row);
    }

    public bool RowEquals(ScreenBuffer other, int row)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Width != other.Width || Height != other.Height)
            return false;
        if (GetRowFingerprint(row) != other.GetRowFingerprint(row))
            return false;

        for (var column = 0; column < Width; column++)
        {
            if (!Grid.CellEquals(other.Grid, column, row))
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_rowFingerprints is null)
            return;
        _rowFingerprints = null;
        Grid.Dispose();
    }

    private static ulong ComputeRowFingerprint(TerminalGrid grid, int row)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        for (var column = 0; column < grid.Width; column++)
        {
            var cell = grid.GetCell(column, row);
            Add(cell.DisplayWidth);
            Add(cell.IsContinuation ? 1 : 0);
            Add(cell.Style.GetHashCode());
            Add(grid.GetHyperlink(cell)?.GetHashCode() ?? 0);
            foreach (var character in grid.GetGrapheme(cell))
                Add(character);
        }
        return hash;

        void Add(int value)
        {
            hash ^= unchecked((uint)value);
            hash *= prime;
        }
    }
}
