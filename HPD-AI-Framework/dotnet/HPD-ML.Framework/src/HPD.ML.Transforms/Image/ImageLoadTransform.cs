namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Loads image files referenced by a path column into byte arrays.
/// </summary>
public sealed class ImageLoadTransform : ITransform
{
    private readonly string _pathColumn;
    private readonly string _outputColumn;

    public ImageLoadTransform(string pathColumn, string outputColumn = "Image")
    {
        _pathColumn = pathColumn;
        _outputColumn = outputColumn;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column(_outputColumn, new FieldType(typeof(byte[]))));
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_pathColumn).Distinct()),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);

                    var path = row.GetValue<string>(_pathColumn);
                    values[_outputColumn] = File.ReadAllBytes(path);

                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }
}
