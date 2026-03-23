namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Adds a boolean indicator column that is true where the source column is missing.
/// </summary>
public sealed class MissingValueIndicateTransform : ITransform
{
    private readonly string _columnName;
    private readonly string _indicatorColumnName;

    public MissingValueIndicateTransform(string columnName, string? indicatorColumnName = null)
    {
        _columnName = columnName;
        _indicatorColumnName = indicatorColumnName ?? $"{columnName}_Missing";
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column(_indicatorColumnName, FieldType.Scalar<bool>()));
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_columnName).Distinct()),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);

                    var val = row.GetValue<object>(_columnName);
                    values[_indicatorColumnName] = MissingValueReplaceTransform.IsMissing(val);

                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }
}
