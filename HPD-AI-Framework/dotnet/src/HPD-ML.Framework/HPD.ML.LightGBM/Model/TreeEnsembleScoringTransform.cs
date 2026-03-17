namespace HPD.ML.LightGBM;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Scores new data using a trained tree ensemble.
/// All prediction happens in managed code — no native LightGBM dependency at inference time.
/// </summary>
public sealed class TreeEnsembleScoringTransform : ITransform
{
    public enum OutputMode { Regression, BinaryClassification, Multiclass }

    private readonly TreeEnsemble _ensemble;
    private readonly string _featureColumn;
    private readonly OutputMode _mode;
    private readonly int _numberOfClasses;

    public TreeEnsembleScoringTransform(
        TreeEnsemble ensemble,
        string featureColumn,
        OutputMode mode,
        int numberOfClasses = 2)
    {
        _ensemble = ensemble;
        _featureColumn = featureColumn;
        _mode = mode;
        _numberOfClasses = numberOfClasses;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        Column[] columns = _mode switch
        {
            OutputMode.BinaryClassification =>
            [
                new Column("Score", FieldType.Scalar<float>()),
                new Column("Probability", FieldType.Scalar<float>()),
                new Column("PredictedLabel", FieldType.Scalar<bool>())
            ],
            OutputMode.Multiclass =>
            [
                new Column("Score", FieldType.Vector<float>(_numberOfClasses)),
                new Column("PredictedLabel", FieldType.Scalar<uint>())
            ],
            _ => // Regression, Ranking
            [
                new Column("Score", FieldType.Scalar<float>())
            ]
        };

        return inputSchema.MergeHorizontal(new Schema(columns), ConflictPolicy.LastWriterWins);
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var outputSchema = GetOutputSchema(input.Schema);

        return new CursorDataHandle(
            outputSchema,
            columns => new MappedCursor(
                input.GetCursor(columns.Append(_featureColumn).Distinct()),
                row => ScoreRow(row, outputSchema)),
            input.RowCount,
            input.Ordering);
    }

    private DictionaryRow ScoreRow(IRow row, ISchema outputSchema)
    {
        var features = ExtractFeatures(row);
        var rawScores = _ensemble.Score(features);

        var values = new Dictionary<string, object>();
        foreach (var col in row.Schema.Columns)
            values[col.Name] = row.GetValue<object>(col.Name);

        switch (_mode)
        {
            case OutputMode.BinaryClassification:
            {
                float rawScore = (float)rawScores[0];
                float probability = Sigmoid(rawScore);
                values["Score"] = rawScore;
                values["Probability"] = probability;
                values["PredictedLabel"] = probability >= 0.5f;
                break;
            }
            case OutputMode.Multiclass:
            {
                float[] probs = Softmax(rawScores);
                values["Score"] = probs;
                // Find argmax
                uint predicted = 0;
                float max = probs[0];
                for (int c = 1; c < probs.Length; c++)
                {
                    if (probs[c] > max)
                    {
                        max = probs[c];
                        predicted = (uint)c;
                    }
                }
                values["PredictedLabel"] = predicted;
                break;
            }
            default: // Regression, Ranking
            {
                values["Score"] = (float)rawScores[0];
                break;
            }
        }

        return new DictionaryRow(outputSchema, values);
    }

    private ReadOnlySpan<float> ExtractFeatures(IRow row)
    {
        if (row.TryGetValue<float[]>(_featureColumn, out var vector))
            return vector;
        return new float[] { row.GetValue<float>(_featureColumn) };
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    private static float[] Softmax(double[] scores)
    {
        double max = double.MinValue;
        for (int i = 0; i < scores.Length; i++)
            if (scores[i] > max) max = scores[i];

        var result = new float[scores.Length];
        double sum = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            result[i] = (float)Math.Exp(scores[i] - max);
            sum += result[i];
        }
        for (int i = 0; i < result.Length; i++)
            result[i] /= (float)sum;

        return result;
    }
}
