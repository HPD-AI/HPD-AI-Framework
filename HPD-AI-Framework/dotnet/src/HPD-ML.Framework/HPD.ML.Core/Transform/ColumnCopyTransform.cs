namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Copies a column under a new name. Useful for preserving original values
/// before a destructive transform.
/// </summary>
public sealed class ColumnCopyTransform : ITransform
{
    private readonly string _sourceName;
    private readonly string _destinationName;

    public ColumnCopyTransform(string sourceName, string destinationName)
    {
        _sourceName = sourceName;
        _destinationName = destinationName;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var sourceCol = inputSchema.FindByName(_sourceName)
            ?? throw new InvalidOperationException($"Column '{_sourceName}' not found.");

        var newCol = new Column(_destinationName, sourceCol.Type, sourceCol.Annotations);
        var columns = inputSchema.Columns.Append(newCol).ToList();
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        return new CursorDataHandle(
            outputSchema,
            columns => new CopyingCursor(
                input.GetCursor(columns.Append(_sourceName).Distinct()),
                _sourceName, _destinationName, outputSchema),
            input.RowCount,
            input.Ordering);
    }
}

internal sealed class CopyingCursor : IRowCursor
{
    private readonly IRowCursor _inner;
    private readonly string _src;
    private readonly string _dst;
    private readonly ISchema _schema;

    public CopyingCursor(IRowCursor inner, string src, string dst, ISchema schema)
    {
        _inner = inner;
        _src = src;
        _dst = dst;
        _schema = schema;
    }

    public IRow Current => new CopiedRow(_inner.Current, _src, _dst, _schema);
    public bool MoveNext() => _inner.MoveNext();
    public void Dispose() => _inner.Dispose();
}

internal sealed class CopiedRow : IRow
{
    private readonly IRow _inner;
    private readonly string _src;
    private readonly string _dst;

    public CopiedRow(IRow inner, string src, string dst, ISchema schema)
    {
        _inner = inner;
        _src = src;
        _dst = dst;
        Schema = schema;
    }

    public ISchema Schema { get; }

    public T GetValue<T>(string columnName) where T : allows ref struct
        => _inner.GetValue<T>(columnName == _dst ? _src : columnName);

    public bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct
        => _inner.TryGetValue(columnName == _dst ? _src : columnName, out value);
}
