namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Maps integer keys back to original string values.
/// The inverse of ValueToKeyTransform.
/// </summary>
public sealed class KeyToValueTransform : ITransform
{
    private readonly string _columnName;
    private readonly string? _outputColumnName;
    private readonly IReadOnlyList<string> _keyValues;

    public KeyToValueTransform(
        string columnName,
        IReadOnlyList<string> keyValues,
        string? outputColumnName = null)
    {
        _columnName = columnName;
        _outputColumnName = outputColumnName;
        _keyValues = keyValues;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var outName = _outputColumnName ?? _columnName;
        var columns = inputSchema.Columns.Select(c =>
            c.Name == _columnName && outName == _columnName
                ? new Column(outName, FieldType.Scalar<string>(), c.Annotations)
                : c).ToList();

        if (outName != _columnName)
            columns.Add(new Column(outName, FieldType.Scalar<string>()));

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
                            int key = row.GetValue<int>(_columnName);
                            values[outCol] = key >= 0 && key < _keyValues.Count
                                ? _keyValues[key] : "";
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
