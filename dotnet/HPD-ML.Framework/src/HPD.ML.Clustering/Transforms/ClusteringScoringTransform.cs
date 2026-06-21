namespace HPD.ML.Clustering;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Assigns each row a cluster ID and a distance vector to all centroids.
/// Output: PredictedLabel (uint, 1-indexed), Score (float[K] distances).
/// </summary>
public sealed class ClusteringScoringTransform : ITransform
{
    private readonly ClusteringModelParameters _params;
    private readonly string _featureColumn;

    public ClusteringScoringTransform(ClusteringModelParameters parameters, string featureColumn = "Features")
    {
        _params = parameters;
        _featureColumn = featureColumn;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var outputColumns = new Schema([
            new Column("PredictedLabel", FieldType.Scalar<uint>()),
            new Column("Score", FieldType.Vector<float>(_params.K))
        ]);
        return inputSchema.MergeHorizontal(outputColumns, ConflictPolicy.LastWriterWins);
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

                    var distances = new float[_params.K];
                    int bestCluster = 0;
                    float bestDist = float.MaxValue;

                    for (int k = 0; k < _params.K; k++)
                    {
                        distances[k] = _params.DistanceSquared(features, k);
                        if (distances[k] < bestDist)
                        {
                            bestDist = distances[k];
                            bestCluster = k;
                        }
                    }

                    uint label = (uint)(bestCluster + 1);

                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                        values[col.Name] = row.GetValue<object>(col.Name);
                    values["PredictedLabel"] = label;
                    values["Score"] = distances;

                    return new DictionaryRow(outputSchema, values);
                }),
            input.RowCount,
            input.Ordering);
    }

    private ReadOnlySpan<float> ExtractFeatures(IRow row)
    {
        if (row.TryGetValue<float[]>(_featureColumn, out var vector))
            return vector;

        // Single scalar feature
        var scalar = row.GetValue<float>(_featureColumn);
        return new float[] { scalar };
    }
}
