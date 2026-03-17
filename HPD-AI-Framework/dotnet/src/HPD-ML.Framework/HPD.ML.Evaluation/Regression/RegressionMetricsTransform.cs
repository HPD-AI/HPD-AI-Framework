namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Computes regression metrics: MAE, MSE, RMSE, R², Adjusted R².
/// </summary>
public sealed class RegressionMetricsTransform : ITransform
{
    private readonly string _labelColumn;
    private readonly string _scoreColumn;
    private readonly int? _featureCount;

    public RegressionMetricsTransform(
        string labelColumn = "Label",
        string scoreColumn = "Score",
        int? featureCount = null)
    {
        _labelColumn = labelColumn;
        _scoreColumn = scoreColumn;
        _featureCount = featureCount;
    }

    public TransformProperties Properties => new() { PreservesRowCount = false };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        return new SchemaBuilder()
            .AddColumn<double>("MAE")
            .AddColumn<double>("MSE")
            .AddColumn<double>("RMSE")
            .AddColumn<double>("RSquared")
            .AddColumn<double>("AdjustedRSquared")
            .Build();
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var labels = new List<double>();
        var scores = new List<double>();

        using var cursor = input.GetCursor([_labelColumn, _scoreColumn]);
        while (cursor.MoveNext())
        {
            labels.Add(MetricHelpers.ToDouble(cursor.Current, _labelColumn));
            scores.Add(MetricHelpers.ToDouble(cursor.Current, _scoreColumn));
        }

        int n = labels.Count;
        if (n == 0)
            return InMemoryDataHandle.FromColumns(
                ("MAE", new double[] { 0 }), ("MSE", new double[] { 0 }),
                ("RMSE", new double[] { 0 }), ("RSquared", new double[] { 0 }),
                ("AdjustedRSquared", new double[] { 0 }));

        double sumAbsError = 0, sumSqError = 0;
        double meanLabel = labels.Average();
        double sumSqTotal = 0;

        for (int i = 0; i < n; i++)
        {
            double error = labels[i] - scores[i];
            sumAbsError += Math.Abs(error);
            sumSqError += error * error;
            sumSqTotal += (labels[i] - meanLabel) * (labels[i] - meanLabel);
        }

        double mae = sumAbsError / n;
        double mse = sumSqError / n;
        double rmse = Math.Sqrt(mse);
        double rSquared = sumSqTotal > 0 ? 1.0 - sumSqError / sumSqTotal : 0;
        double adjustedRSquared = _featureCount.HasValue && n > _featureCount.Value + 1
            ? 1.0 - (1.0 - rSquared) * (n - 1.0) / (n - _featureCount.Value - 1.0)
            : rSquared;

        return InMemoryDataHandle.FromColumns(
            ("MAE", new[] { mae }),
            ("MSE", new[] { mse }),
            ("RMSE", new[] { rmse }),
            ("RSquared", new[] { rSquared }),
            ("AdjustedRSquared", new[] { adjustedRSquared }));
    }
}
