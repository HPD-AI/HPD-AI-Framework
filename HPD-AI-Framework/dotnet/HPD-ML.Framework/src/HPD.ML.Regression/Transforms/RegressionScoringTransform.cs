namespace HPD.ML.Regression;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

/// <summary>
/// Applies a linear model to produce regression predictions.
/// Output column: "Score" (float).
/// For Poisson: applies exp() to the linear score.
/// </summary>
public sealed class RegressionScoringTransform : ITransform
{
    private readonly LinearModelParameters _params;
    private readonly string _featureColumn;
    private readonly bool _applyExp;

    public RegressionScoringTransform(
        LinearModelParameters parameters,
        string featureColumn = "Features",
        bool applyExp = false)
    {
        _params = parameters;
        _featureColumn = featureColumn;
        _applyExp = applyExp;
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

                    float output = _applyExp ? (float)Math.Exp(score) : (float)score;

                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);
                    values["Score"] = output;

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
