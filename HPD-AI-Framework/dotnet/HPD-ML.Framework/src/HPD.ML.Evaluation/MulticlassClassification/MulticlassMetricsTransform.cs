namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Computes multiclass classification metrics including micro/macro accuracy and log-loss.
/// </summary>
public sealed class MulticlassMetricsTransform : ITransform
{
    private readonly string _labelColumn;
    private readonly string _predictedLabelColumn;
    private readonly string _scoreColumn;

    public MulticlassMetricsTransform(
        string labelColumn = "Label",
        string predictedLabelColumn = "PredictedLabel",
        string scoreColumn = "Score")
    {
        _labelColumn = labelColumn;
        _predictedLabelColumn = predictedLabelColumn;
        _scoreColumn = scoreColumn;
    }

    public TransformProperties Properties => new() { PreservesRowCount = false };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        return new SchemaBuilder()
            .AddColumn<double>("MicroAccuracy")
            .AddColumn<double>("MacroAccuracy")
            .AddColumn<double>("LogLoss")
            .AddColumn<double>("LogLossReduction")
            .Build();
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var labels = new List<int>();
        var predictions = new List<int>();
        var scoreVectors = new List<float[]?>();

        using var cursor = input.GetCursor([_labelColumn, _predictedLabelColumn, _scoreColumn]);
        while (cursor.MoveNext())
        {
            labels.Add(MetricHelpers.ToInt(cursor.Current, _labelColumn));
            predictions.Add(MetricHelpers.ToInt(cursor.Current, _predictedLabelColumn));

            try
            {
                if (cursor.Current.TryGetValue<float[]>(_scoreColumn, out var sv))
                    scoreVectors.Add(sv);
                else
                    scoreVectors.Add(null);
            }
            catch
            {
                scoreVectors.Add(null);
            }
        }

        int n = labels.Count;
        if (n == 0)
            return InMemoryDataHandle.FromColumns(
                ("MicroAccuracy", new double[] { 0 }), ("MacroAccuracy", new double[] { 0 }),
                ("LogLoss", new double[] { 0 }), ("LogLossReduction", new double[] { 0 }));

        int correct = 0;
        var classLabels = labels.Union(predictions).Distinct().Order().ToList();
        var perClassCorrect = new Dictionary<int, int>();
        var perClassTotal = new Dictionary<int, int>();

        foreach (var c in classLabels)
        {
            perClassCorrect[c] = 0;
            perClassTotal[c] = 0;
        }

        for (int i = 0; i < n; i++)
        {
            perClassTotal[labels[i]]++;
            if (labels[i] == predictions[i])
            {
                correct++;
                perClassCorrect[labels[i]]++;
            }
        }

        double microAccuracy = (double)correct / n;
        double macroAccuracy = classLabels.Count > 0
            ? classLabels.Average(c => perClassTotal[c] > 0
                ? (double)perClassCorrect[c] / perClassTotal[c]
                : 0)
            : 0;

        // Compute log-loss from score vectors if available
        double logLoss = ComputeLogLoss(labels, scoreVectors, classLabels);
        double baselineLogLoss = ComputeBaselineLogLoss(labels, classLabels);
        double logLossReduction = baselineLogLoss > 0 ? 1.0 - logLoss / baselineLogLoss : 0;

        return InMemoryDataHandle.FromColumns(
            ("MicroAccuracy", new[] { microAccuracy }),
            ("MacroAccuracy", new[] { macroAccuracy }),
            ("LogLoss", new[] { logLoss }),
            ("LogLossReduction", new[] { logLossReduction }));
    }

    private static double ComputeLogLoss(List<int> labels, List<float[]?> scoreVectors, List<int> classLabels)
    {
        if (scoreVectors.All(s => s is null))
            return 0;

        var classIndex = new Dictionary<int, int>();
        for (int i = 0; i < classLabels.Count; i++)
            classIndex[classLabels[i]] = i;

        double total = 0;
        int count = 0;
        for (int i = 0; i < labels.Count; i++)
        {
            var sv = scoreVectors[i];
            if (sv is null || !classIndex.TryGetValue(labels[i], out var idx) || idx >= sv.Length)
                continue;

            double p = Math.Clamp(sv[idx], 1e-15f, 1 - 1e-15f);
            total += -Math.Log(p);
            count++;
        }

        return count > 0 ? total / count : 0;
    }

    private static double ComputeBaselineLogLoss(List<int> labels, List<int> classLabels)
    {
        int n = labels.Count;
        double loss = 0;
        foreach (var c in classLabels)
        {
            double p = (double)labels.Count(l => l == c) / n;
            if (p > 0) loss -= p * Math.Log(p);
        }
        return loss;
    }
}
