namespace HPD.ML.DeepLearning;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public sealed class NeuralNetworkScoringTransform : ITransform
{
    private readonly NeuralNetworkParameters _parameters;

    public NeuralNetworkScoringTransform(NeuralNetworkParameters parameters)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column("Score", FieldType.Scalar<float>()));
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);
        var featureColumn = _parameters.Definition.FeatureColumn;

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(featureColumn).Distinct()),
                row =>
                {
                    var score = Predict(ExtractFeatures(row, featureColumn));
                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);
                    values["Score"] = score[0];
                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }

    internal float[] Predict(float[] features)
        => NeuralNetworkMath.Forward(_parameters.Definition, _parameters.Weights, _parameters.Biases, features);

    internal static float[] ExtractFeatures(IRow row, string featureColumn)
    {
        var column = row.Schema.Columns.First(column => column.Name == featureColumn);
        if (column.Type.ClrType == typeof(float[]))
        {
            var vector = row.GetValue<float[]>(featureColumn);
            return [.. vector];
        }

        return [row.GetValue<float>(featureColumn)];
    }
}
