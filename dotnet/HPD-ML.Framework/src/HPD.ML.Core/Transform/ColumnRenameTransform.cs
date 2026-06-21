namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Renames columns. Schema-only operation — no data movement.
/// </summary>
public sealed class ColumnRenameTransform : ITransform
{
    private readonly IReadOnlyDictionary<string, string> _renames; // old -> new

    public ColumnRenameTransform(IReadOnlyDictionary<string, string> renames)
        => _renames = renames;

    public ColumnRenameTransform(string oldName, string newName)
        : this(new Dictionary<string, string> { [oldName] = newName }) { }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.Select(c =>
            _renames.TryGetValue(c.Name, out var newName)
                ? new Column(newName, c.Type, c.Annotations, c.IsHidden)
                : c).ToList();
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var reverse = _renames.ToDictionary(kv => kv.Value, kv => kv.Key);
        return new CursorDataHandle(
            outputSchema,
            columns =>
            {
                var sourceColumns = columns.Select(c =>
                    reverse.TryGetValue(c, out var old) ? old : c);
                return new RenamingCursor(input.GetCursor(sourceColumns), _renames, outputSchema);
            },
            input.RowCount,
            input.Ordering);
    }
}

internal sealed class RenamingCursor : IRowCursor
{
    private readonly IRowCursor _inner;
    private readonly IReadOnlyDictionary<string, string> _renames;
    private readonly ISchema _outputSchema;

    public RenamingCursor(
        IRowCursor inner,
        IReadOnlyDictionary<string, string> renames,
        ISchema outputSchema)
    {
        _inner = inner;
        _renames = renames;
        _outputSchema = outputSchema;
    }

    public IRow Current => new RenamedRow(_inner.Current, _renames, _outputSchema);
    public bool MoveNext() => _inner.MoveNext();
    public void Dispose() => _inner.Dispose();
}

internal sealed class RenamedRow : IRow
{
    private readonly IRow _inner;
    private readonly IReadOnlyDictionary<string, string> _newToOld;

    public RenamedRow(IRow inner, IReadOnlyDictionary<string, string> oldToNew, ISchema schema)
    {
        _inner = inner;
        Schema = schema;
        _newToOld = oldToNew.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    public ISchema Schema { get; }

    public T GetValue<T>(string columnName) where T : allows ref struct
        => _inner.GetValue<T>(_newToOld.TryGetValue(columnName, out var old) ? old : columnName);

    public bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct
        => _inner.TryGetValue(_newToOld.TryGetValue(columnName, out var old) ? old : columnName, out value);
}
