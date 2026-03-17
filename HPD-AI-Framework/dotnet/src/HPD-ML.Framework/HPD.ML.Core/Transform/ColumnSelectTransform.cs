namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Keeps or drops columns by name.
/// </summary>
public sealed class ColumnSelectTransform : ITransform
{
    private readonly string[] _columns;
    private readonly bool _keep;

    private ColumnSelectTransform(string[] columns, bool keep)
    {
        _columns = columns;
        _keep = keep;
    }

    public static ColumnSelectTransform Keep(params string[] columns)
        => new(columns, true);

    public static ColumnSelectTransform Drop(params string[] columns)
        => new(columns, false);

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var filtered = _keep
            ? inputSchema.Columns.Where(c => _columns.Contains(c.Name)).ToList()
            : inputSchema.Columns.Where(c => !_columns.Contains(c.Name)).ToList();
        return new Schema(filtered, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var selectedNames = outputSchema.Columns.Select(c => c.Name).ToArray();
        return new CursorDataHandle(
            outputSchema,
            _ => input.GetCursor(selectedNames),
            input.RowCount,
            input.Ordering);
    }
}
