namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Computes binary classification metrics from a DataHandle with label and score columns.
/// Output is a single-row DataHandle with metric columns.
/// </summary>
public sealed class BinaryClassificationMetricsTransform : ITransform
{
    private readonly string _labelColumn;
    private readonly string _scoreColumn;
    private readonly double _threshold;

    public BinaryClassificationMetricsTransform(
        string labelColumn = "Label",
        string scoreColumn = "Score",
        double threshold = 0.5)
    {
        _labelColumn = labelColumn;
        _scoreColumn = scoreColumn;
        _threshold = threshold;
    }

    public TransformProperties Properties => new() { PreservesRowCount = false };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        return new SchemaBuilder()
            .AddColumn<double>("AUC")
            .AddColumn<double>("Accuracy")
            .AddColumn<double>("F1Score")
            .AddColumn<double>("Precision")
            .AddColumn<double>("Recall")
            .AddColumn<double>("LogLoss")
            .AddColumn<double>("LogLossReduction")
            .AddColumn<double>("PositivePrecision")
            .AddColumn<double>("PositiveRecall")
            .AddColumn<double>("NegativePrecision")
            .AddColumn<double>("NegativeRecall")
            .Build();
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var labels = new List<bool>();
        var scores = new List<double>();

        using var cursor = input.GetCursor([_labelColumn, _scoreColumn]);
        while (cursor.MoveNext())
        {
            labels.Add(MetricHelpers.ToBool(cursor.Current, _labelColumn));
            scores.Add(MetricHelpers.ToDouble(cursor.Current, _scoreColumn));
        }

        int n = labels.Count;
        if (n == 0)
            return EmptyResult();

        int tp = 0, fp = 0, tn = 0, fn = 0;
        double logLoss = 0;

        for (int i = 0; i < n; i++)
        {
            bool predicted = scores[i] >= _threshold;
            bool actual = labels[i];

            if (predicted && actual) tp++;
            else if (predicted && !actual) fp++;
            else if (!predicted && actual) fn++;
            else tn++;

            double p = Math.Clamp(scores[i], 1e-15, 1 - 1e-15);
            logLoss += actual ? -Math.Log(p) : -Math.Log(1 - p);
        }
        logLoss /= n;

        double accuracy = (double)(tp + tn) / n;
        double precision = tp + fp > 0 ? (double)tp / (tp + fp) : 0;
        double recall = tp + fn > 0 ? (double)tp / (tp + fn) : 0;
        double f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;
        double negPrecision = tn + fn > 0 ? (double)tn / (tn + fn) : 0;
        double negRecall = tn + fp > 0 ? (double)tn / (tn + fp) : 0;
        double auc = ComputeAuc(labels, scores);
        double baselineLogLoss = ComputeBaselineLogLoss(labels);
        double logLossReduction = baselineLogLoss > 0 ? 1.0 - logLoss / baselineLogLoss : 0;

        return InMemoryDataHandle.FromColumns(
            ("AUC", new[] { auc }),
            ("Accuracy", new[] { accuracy }),
            ("F1Score", new[] { f1 }),
            ("Precision", new[] { precision }),
            ("Recall", new[] { recall }),
            ("LogLoss", new[] { logLoss }),
            ("LogLossReduction", new[] { logLossReduction }),
            ("PositivePrecision", new[] { precision }),
            ("PositiveRecall", new[] { recall }),
            ("NegativePrecision", new[] { negPrecision }),
            ("NegativeRecall", new[] { negRecall }));
    }

    private IDataHandle EmptyResult()
    {
        return InMemoryDataHandle.FromColumns(
            ("AUC", new double[] { 0 }),
            ("Accuracy", new double[] { 0 }),
            ("F1Score", new double[] { 0 }),
            ("Precision", new double[] { 0 }),
            ("Recall", new double[] { 0 }),
            ("LogLoss", new double[] { 0 }),
            ("LogLossReduction", new double[] { 0 }),
            ("PositivePrecision", new double[] { 0 }),
            ("PositiveRecall", new double[] { 0 }),
            ("NegativePrecision", new double[] { 0 }),
            ("NegativeRecall", new double[] { 0 }));
    }

    internal static double ComputeAuc(List<bool> labels, List<double> scores)
    {
        int positives = labels.Count(l => l);
        int negatives = labels.Count - positives;

        if (positives == 0 || negatives == 0) return 0.5;

        // Wilcoxon-Mann-Whitney U statistic:
        // AUC = P(score_pos > score_neg) + 0.5 * P(score_pos == score_neg)
        double sum = 0;
        for (int i = 0; i < labels.Count; i++)
        {
            if (!labels[i]) continue;
            for (int j = 0; j < labels.Count; j++)
            {
                if (labels[j]) continue;
                if (scores[i] > scores[j]) sum += 1.0;
                else if (scores[i] == scores[j]) sum += 0.5;
            }
        }

        return sum / ((double)positives * negatives);
    }

    private static double ComputeBaselineLogLoss(List<bool> labels)
    {
        double posRate = (double)labels.Count(l => l) / labels.Count;
        posRate = Math.Clamp(posRate, 1e-15, 1 - 1e-15);
        return -(posRate * Math.Log(posRate) + (1 - posRate) * Math.Log(1 - posRate));
    }
}
