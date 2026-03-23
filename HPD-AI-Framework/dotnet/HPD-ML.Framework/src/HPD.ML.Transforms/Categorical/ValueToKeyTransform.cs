namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Maps string/value columns to integer keys.
/// The inverse of KeyToValueTransform.
/// </summary>
public sealed class ValueToKeyTransform : ITransform
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly IReadOnlyDictionary<string, int> _mapping;

    public ValueToKeyTransform(
        string columnName,
        IReadOnlyDictionary<string, int> mapping,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName;
        _mapping = mapping;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var outName = _outputColumnName ?? _columnName;
        var columns = inputSchema.Columns.Select(c =>
            c.Name == _columnName && outName == _columnName
                ? new Column(outName, FieldType.Scalar<int>(), c.Annotations)
                : c).ToList();

        if (outName != _columnName)
            columns.Add(new Column(outName, FieldType.Scalar<int>()));

        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var outCol = _outputColumnName ?? _columnName;

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_columnName).Distinct()),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in outputSchema.Columns)
                    {
                        if (col.Name == outCol)
                        {
                            var raw = row.GetValue<object>(_columnName)?.ToString() ?? "";
                            values[outCol] = _mapping.TryGetValue(raw, out int key) ? key : -1;
                        }
                        else
                        {
                            values[col.Name] = row.GetValue<object>(col.Name);
                        }
                    }
                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }
}
