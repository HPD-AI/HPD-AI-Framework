namespace HPD.TUI.Controllers;

public sealed class GridNavigationController
{
    public GridNavigationController(int rowCount, int columnCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(columnCount);
        RowCount = rowCount;
        ColumnCount = columnCount;
    }

    public int RowCount { get; private set; }

    public int ColumnCount { get; private set; }

    public int Row { get; private set; }

    public int Column { get; private set; }

    public void Resize(int rowCount, int columnCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfNegative(columnCount);
        RowCount = rowCount;
        ColumnCount = columnCount;
        Row = Clamp(Row, RowCount);
        Column = Clamp(Column, ColumnCount);
    }

    public bool MoveRows(int delta)
    {
        var next = Clamp(Row + delta, RowCount);
        if (next == Row)
        {
            return false;
        }

        Row = next;
        return true;
    }

    public bool MoveColumns(int delta)
    {
        var next = Clamp(Column + delta, ColumnCount);
        if (next == Column)
        {
            return false;
        }

        Column = next;
        return true;
    }

    public bool PageRows(int delta, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        return MoveRows(delta * pageSize);
    }

    public bool MoveToRowStart()
    {
        if (Column == 0)
        {
            return false;
        }

        Column = 0;
        return true;
    }

    public bool MoveToRowEnd()
    {
        var next = Math.Max(0, ColumnCount - 1);
        if (Column == next)
        {
            return false;
        }

        Column = next;
        return true;
    }

    private static int Clamp(int value, int count)
    {
        return count <= 0 ? 0 : Math.Clamp(value, 0, count - 1);
    }
}
