namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Forward-only cursor over columnar arrays with column projection.
/// </summary>
public sealed class ArrayRowCursor : IRowCursor
{
    private readonly ISchema _schema;
    private readonly Dictionary<string, Array> _columns;
    private readonly long _rowCount;
    private long _position = -1;
    private Row? _currentRow;

    public ArrayRowCursor(
        ISchema schema,
        Dictionary<string, Array> columns,
        string[] activeColumns,
        long rowCount)
    {
        _schema = schema;
        _columns = columns;
        _rowCount = rowCount;
    }

    public IRow Current => _currentRow
        ?? throw new InvalidOperationException("Cursor not positioned. Call MoveNext().");

    public bool MoveNext()
    {
        _position++;
        if (_position >= _rowCount) return false;
        _currentRow = new Row(_schema, _columns, _position);
        return true;
    }

    public void Dispose() { }
}
