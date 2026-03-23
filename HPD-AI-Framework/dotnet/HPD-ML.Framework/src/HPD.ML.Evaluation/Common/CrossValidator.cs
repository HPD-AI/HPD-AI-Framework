namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Cross-validation as a composition: split → fit → evaluate → aggregate.
/// Not a special API — just a helper that uses DataHandleSplitter + ILearner + metrics transforms.
/// </summary>
public static class CrossValidator
{
    public static CrossValidationResult Evaluate(
        IDataHandle data,
        ILearner learner,
        ITransform metricsTransform,
        int folds = 5,
        int? seed = null,
        ITransform? featurePipeline = null)
    {
        var splits = DataHandleSplitter.CrossValidationSplit(data, folds, seed);
        var foldResults = new List<CrossValidationFold>(folds);

        for (int f = 0; f < folds; f++)
        {
            var (trainFold, testFold) = splits[f];

            if (featurePipeline is not null)
            {
                trainFold = featurePipeline.Apply(trainFold);
                testFold = featurePipeline.Apply(testFold);
            }

            var model = learner.Fit(new LearnerInput(trainFold));
            var predictions = model.Transform.Apply(testFold);
            var metrics = metricsTransform.Apply(predictions);

            foldResults.Add(new CrossValidationFold(f, model, metrics));
        }

        var aggregate = AggregateFoldMetrics(foldResults);
        return new CrossValidationResult(foldResults, aggregate);
    }

    public static async Task<CrossValidationResult> EvaluateAsync(
        IDataHandle data,
        ILearner learner,
        ITransform metricsTransform,
        int folds = 5,
        int? seed = null,
        ITransform? featurePipeline = null,
        CancellationToken ct = default)
    {
        var splits = DataHandleSplitter.CrossValidationSplit(data, folds, seed);
        var foldResults = new List<CrossValidationFold>(folds);

        for (int f = 0; f < folds; f++)
        {
            ct.ThrowIfCancellationRequested();
            var (trainFold, testFold) = splits[f];

            if (featurePipeline is not null)
            {
                trainFold = featurePipeline.Apply(trainFold);
                testFold = featurePipeline.Apply(testFold);
            }

            var model = await learner.FitAsync(new LearnerInput(trainFold), ct);
            var predictions = model.Transform.Apply(testFold);
            var metrics = metricsTransform.Apply(predictions);

            foldResults.Add(new CrossValidationFold(f, model, metrics));
        }

        var aggregate = AggregateFoldMetrics(foldResults);
        return new CrossValidationResult(foldResults, aggregate);
    }

    private static IDataHandle AggregateFoldMetrics(List<CrossValidationFold> folds)
    {
        if (folds.Count == 0)
            return InMemoryDataHandle.FromColumns(
                ("Metric", Array.Empty<string>()),
                ("Mean", Array.Empty<double>()),
                ("StdDev", Array.Empty<double>()));

        var firstMetrics = folds[0].Metrics;
        var metricNames = firstMetrics.Schema.Columns.Select(c => c.Name).ToList();

        var metricValues = new Dictionary<string, List<double>>();
        foreach (var name in metricNames)
            metricValues[name] = [];

        foreach (var fold in folds)
        {
            using var cursor = fold.Metrics.GetCursor(metricNames);
            if (cursor.MoveNext())
            {
                foreach (var name in metricNames)
                {
                    if (cursor.Current.TryGetValue<double>(name, out var val))
                        metricValues[name].Add(val);
                }
            }
        }

        var aggMetricNames = new List<string>();
        var aggMeans = new List<double>();
        var aggStdDevs = new List<double>();

        foreach (var name in metricNames)
        {
            var vals = metricValues[name];
            if (vals.Count == 0) continue;

            double mean = vals.Average();
            double variance = vals.Average(v => (v - mean) * (v - mean));
            double stdDev = Math.Sqrt(variance);

            aggMetricNames.Add(name);
            aggMeans.Add(mean);
            aggStdDevs.Add(stdDev);
        }

        return InMemoryDataHandle.FromColumns(
            ("Metric", aggMetricNames.ToArray()),
            ("Mean", aggMeans.ToArray()),
            ("StdDev", aggStdDevs.ToArray()));
    }
}

public sealed record CrossValidationFold(int FoldIndex, IModel Model, IDataHandle Metrics);

public sealed record CrossValidationResult(
    IReadOnlyList<CrossValidationFold> Folds,
    IDataHandle AggregateMetrics)
{
    public IModel BestModel(string metricName = "Accuracy")
    {
        double bestScore = double.MinValue;
        IModel? best = null;

        foreach (var fold in Folds)
        {
            using var cursor = fold.Metrics.GetCursor([metricName]);
            if (cursor.MoveNext() && cursor.Current.TryGetValue<double>(metricName, out var score))
            {
                if (score > bestScore) { bestScore = score; best = fold.Model; }
            }
        }

        return best ?? Folds[0].Model;
    }
}
