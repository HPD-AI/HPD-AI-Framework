namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Converts a column from one CLR type to another using Convert.ChangeType.
/// </summary>
public sealed class TypeConvertTransform : ITransform
{
    private readonly string _columnName;
    private readonly Type _targetType;

    public TypeConvertTransform(string columnName, Type targetType)
    {
        _columnName = columnName;
        _targetType = targetType;
    }

    public static TypeConvertTransform To<T>(string columnName) => new(columnName, typeof(T));

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.Select(c =>
            c.Name == _columnName
                ? new Column(c.Name, new FieldType(_targetType, c.Type.IsVector, c.Type.Dimensions), c.Annotations)
                : c).ToList();
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in outputSchema.Columns)
                    {
                        if (col.Name == _columnName)
                        {
                            var raw = row.GetValue<object>(_columnName);
                            values[_columnName] = Convert.ChangeType(raw!, _targetType);
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
