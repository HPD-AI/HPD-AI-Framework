namespace HPD.ML.BinaryClassification;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

/// <summary>
/// Scores rows using w·x + b, then applies sigmoid for probability.
/// Adds "Score" (raw), "Probability" (sigmoid), and "PredictedLabel" columns.
/// </summary>
public sealed class LinearScoringTransform : ITransform
{
    private readonly LinearModelParameters _params;
    private readonly string _featureColumn;
    private readonly double _threshold;

    public LinearScoringTransform(
        LinearModelParameters parameters,
        string featureColumn = "Features",
        double threshold = 0.5)
    {
        _params = parameters;
        _featureColumn = featureColumn;
        _threshold = threshold;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var columns = inputSchema.Columns.ToList();
        columns.Add(new Column("Score", FieldType.Scalar<float>()));
        columns.Add(new Column("Probability", FieldType.Scalar<float>()));
        columns.Add(new Column("PredictedLabel", FieldType.Scalar<bool>()));
        return new Schema(columns, inputSchema.Level);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_featureColumn).Distinct()),
                row =>
                {
                    var features = ExtractFeatures(row);
                    int d = _params.FeatureCount;

                    // w·x + b
                    double score = (double)_params.Bias;
                    for (int i = 0; i < d; i++)
                        score += (double)_params.Weights[i] * (double)features[i];

                    // Sigmoid
                    double probability = 1.0 / (1.0 + Math.Exp(-score));

                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);
                    values["Score"] = (float)score;
                    values["Probability"] = (float)probability;
                    values["PredictedLabel"] = probability >= _threshold;

                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }

    private Double[] ExtractFeatures(IRow row)
    {
        if (row.TryGetValue<float[]>(_featureColumn, out var vector))
        {
            var values = new Double[vector.Length];
            for (int i = 0; i < vector.Length; i++)
                values[i] = new Double(vector[i]);
            return values;
        }

        var scalar = row.GetValue<float>(_featureColumn);
        return [new Double(scalar)];
    }
}
