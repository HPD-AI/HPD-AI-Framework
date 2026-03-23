namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Computes a confusion matrix from predictions and labels.
/// Returns a DataHandle where each row is a (TrueLabel, PredictedLabel, Count) triple.
/// </summary>
public sealed class ConfusionMatrixTransform : ITransform
{
    private readonly string _labelColumn;
    private readonly string _predictedLabelColumn;

    public ConfusionMatrixTransform(
        string labelColumn = "Label",
        string predictedLabelColumn = "PredictedLabel")
    {
        _labelColumn = labelColumn;
        _predictedLabelColumn = predictedLabelColumn;
    }

    public TransformProperties Properties => new() { PreservesRowCount = false };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        return new SchemaBuilder()
            .AddColumn("TrueLabel", new FieldType(typeof(string)))
            .AddColumn("PredictedLabel", new FieldType(typeof(string)))
            .AddColumn<int>("Count")
            .Build();
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var counts = new Dictionary<(string True, string Predicted), int>();

        using var cursor = input.GetCursor([_labelColumn, _predictedLabelColumn]);
        while (cursor.MoveNext())
        {
            var trueLabel = cursor.Current.GetValue<object>(_labelColumn)?.ToString() ?? "";
            var predicted = cursor.Current.GetValue<object>(_predictedLabelColumn)?.ToString() ?? "";
            var key = (trueLabel, predicted);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var trueLabels = counts.Keys.Select(k => k.True).ToArray();
        var predictedLabels = counts.Keys.Select(k => k.Predicted).ToArray();
        var countValues = counts.Values.ToArray();

        return InMemoryDataHandle.FromColumns(
            ("TrueLabel", trueLabels),
            ("PredictedLabel", predictedLabels),
            ("Count", countValues));
    }
}

/// <summary>
/// Convenience methods for confusion matrix display.
/// </summary>
public static class ConfusionMatrixFormatter
{
    public static string Format(IDataHandle confusionMatrix)
    {
        var rows = new List<(string True, string Predicted, int Count)>();

        using var cursor = confusionMatrix.GetCursor(["TrueLabel", "PredictedLabel", "Count"]);
        while (cursor.MoveNext())
        {
            rows.Add((
                cursor.Current.GetValue<string>("TrueLabel"),
                cursor.Current.GetValue<string>("PredictedLabel"),
                cursor.Current.GetValue<int>("Count")));
        }

        var labels = rows.Select(r => r.True)
            .Union(rows.Select(r => r.Predicted))
            .Distinct().Order().ToList();

        var sb = new System.Text.StringBuilder();

        // Header
        sb.Append("".PadRight(15));
        foreach (var l in labels) sb.Append(l.PadRight(12));
        sb.AppendLine();

        // Rows
        foreach (var trueLabel in labels)
        {
            sb.Append(trueLabel.PadRight(15));
            foreach (var predLabel in labels)
            {
                int count = rows.FirstOrDefault(r => r.True == trueLabel && r.Predicted == predLabel).Count;
                sb.Append(count.ToString().PadRight(12));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
