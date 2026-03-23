namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// User-defined row transform via lambda. Escape hatch for one-off transformations.
/// Not serializable. Not AOT-friendly (captures closure). Use sparingly.
/// </summary>
public sealed class LambdaTransform : ITransform
{
    private readonly Func<IRow, IRow> _rowMapper;
    private readonly Func<ISchema, ISchema> _schemaMapper;

    public LambdaTransform(
        Func<IRow, IRow> rowMapper,
        Func<ISchema, ISchema> schemaMapper)
    {
        _rowMapper = rowMapper;
        _schemaMapper = schemaMapper;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema) => _schemaMapper(inputSchema);

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(input.GetCursor(columns), _rowMapper),
            input.RowCount,
            input.Ordering);
    }
}

