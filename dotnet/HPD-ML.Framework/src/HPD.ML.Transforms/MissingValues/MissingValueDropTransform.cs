namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Drops rows where specified columns have missing values.
/// </summary>
public sealed class MissingValueDropTransform : ITransform
{
    private readonly string[] _columnNames;

    public MissingValueDropTransform(params string[] columnNames)
        => _columnNames = columnNames;

    public TransformProperties Properties => new() { PreservesRowCount = false };

    public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;

    public IDataHandle Apply(IDataHandle input)
        => new FilteredDataHandle(input, row =>
        {
            foreach (var col in _columnNames)
            {
                var val = row.GetValue<object>(col);
                if (MissingValueReplaceTransform.IsMissing(val))
                    return false;
            }
            return true;
        });
}
