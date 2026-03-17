namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Computes clustering metrics: NMI, Average Distance, Davies-Bouldin Index.
/// Expects columns with true labels, predicted labels, features, and score (distance array).
/// </summary>
public sealed class ClusteringMetricsTransform : ITransform
{
    private readonly string _labelColumn;
    private readonly string _predictedLabelColumn;
    private readonly string _scoreColumn;
    private readonly string _featuresColumn;

    public ClusteringMetricsTransform(
        string labelColumn = "Label",
        string predictedLabelColumn = "PredictedLabel",
        string scoreColumn = "Score",
        string featuresColumn = "Features")
    {
        _labelColumn = labelColumn;
        _predictedLabelColumn = predictedLabelColumn;
        _scoreColumn = scoreColumn;
        _featuresColumn = featuresColumn;
    }

    public TransformProperties Properties => new() { PreservesRowCount = false };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        return new SchemaBuilder()
            .AddColumn<double>("NormalizedMutualInformation")
            .AddColumn<double>("AverageDistance")
            .AddColumn<double>("DaviesBouldinIndex")
            .Build();
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var trueLabels = new List<int>();
        var predicted = new List<int>();
        var minDistances = new List<double>();
        var features = new List<float[]>();

        var columnsNeeded = new List<string> { _labelColumn, _predictedLabelColumn };
        // Score and features are optional for some use cases
        bool hasScore = input.Schema.FindByName(_scoreColumn) is not null;
        bool hasFeatures = input.Schema.FindByName(_featuresColumn) is not null;
        if (hasScore) columnsNeeded.Add(_scoreColumn);
        if (hasFeatures) columnsNeeded.Add(_featuresColumn);

        using var cursor = input.GetCursor(columnsNeeded);
        while (cursor.MoveNext())
        {
            trueLabels.Add(MetricHelpers.ToInt(cursor.Current, _labelColumn));
            predicted.Add(MetricHelpers.ToInt(cursor.Current, _predictedLabelColumn));

            if (hasScore && cursor.Current.TryGetValue<float[]>(_scoreColumn, out var scoreArr))
            {
                // Score is float[] of distances to each centroid; min = distance to assigned
                minDistances.Add(scoreArr.Min());
            }

            if (hasFeatures && cursor.Current.TryGetValue<float[]>(_featuresColumn, out var feat))
            {
                features.Add(feat);
            }
        }

        double nmi = ComputeNmi(trueLabels, predicted);
        double avgDist = minDistances.Count > 0 ? minDistances.Average() : 0;
        double dbi = ComputeDbi(predicted, features);

        return InMemoryDataHandle.FromColumns(
            ("NormalizedMutualInformation", new[] { nmi }),
            ("AverageDistance", new[] { avgDist }),
            ("DaviesBouldinIndex", new[] { dbi }));
    }

    internal static double ComputeNmi(List<int> trueLabels, List<int> predicted)
    {
        int n = trueLabels.Count;
        if (n == 0) return 0;

        var trueClasses = trueLabels.Distinct().ToList();
        var predClasses = predicted.Distinct().ToList();

        // Contingency counts
        var trueCounts = new Dictionary<int, int>();
        var predCounts = new Dictionary<int, int>();
        var joint = new Dictionary<(int, int), int>();

        for (int i = 0; i < n; i++)
        {
            trueCounts[trueLabels[i]] = trueCounts.GetValueOrDefault(trueLabels[i]) + 1;
            predCounts[predicted[i]] = predCounts.GetValueOrDefault(predicted[i]) + 1;
            var key = (trueLabels[i], predicted[i]);
            joint[key] = joint.GetValueOrDefault(key) + 1;
        }

        // Mutual information
        double mi = 0;
        foreach (var ((t, p), nij) in joint)
        {
            double pij = (double)nij / n;
            double pi = (double)trueCounts[t] / n;
            double pj = (double)predCounts[p] / n;
            mi += pij * Math.Log(pij / (pi * pj));
        }

        // Entropies
        double hTrue = 0, hPred = 0;
        foreach (var c in trueCounts.Values)
        {
            double p = (double)c / n;
            if (p > 0) hTrue -= p * Math.Log(p);
        }
        foreach (var c in predCounts.Values)
        {
            double p = (double)c / n;
            if (p > 0) hPred -= p * Math.Log(p);
        }

        double denom = (hTrue + hPred) / 2;
        return denom > 0 ? mi / denom : 0;
    }

    internal static double ComputeDbi(List<int> predicted, List<float[]> features)
    {
        if (features.Count == 0 || features.Count != predicted.Count)
            return 0;

        var clusters = predicted.Distinct().Order().ToList();
        if (clusters.Count < 2) return 0;

        int dim = features[0].Length;

        // Compute centroids and average intra-cluster distances
        var centroids = new Dictionary<int, float[]>();
        var avgDistances = new Dictionary<int, double>();

        foreach (var c in clusters)
        {
            var members = new List<float[]>();
            for (int i = 0; i < predicted.Count; i++)
                if (predicted[i] == c) members.Add(features[i]);

            var centroid = new float[dim];
            foreach (var m in members)
                for (int d = 0; d < dim; d++)
                    centroid[d] += m[d];
            for (int d = 0; d < dim; d++)
                centroid[d] /= members.Count;

            centroids[c] = centroid;

            double avgDist = 0;
            foreach (var m in members)
            {
                double dist = 0;
                for (int d = 0; d < dim; d++)
                    dist += (m[d] - centroid[d]) * (m[d] - centroid[d]);
                avgDist += Math.Sqrt(dist);
            }
            avgDistances[c] = members.Count > 0 ? avgDist / members.Count : 0;
        }

        // DBI = (1/K) * sum_i max_{j≠i} (S_i + S_j) / d(c_i, c_j)
        double dbi = 0;
        foreach (var i in clusters)
        {
            double maxRatio = 0;
            foreach (var j in clusters)
            {
                if (i == j) continue;
                double interDist = 0;
                for (int d = 0; d < dim; d++)
                    interDist += (centroids[i][d] - centroids[j][d]) * (centroids[i][d] - centroids[j][d]);
                interDist = Math.Sqrt(interDist);

                if (interDist > 0)
                {
                    double ratio = (avgDistances[i] + avgDistances[j]) / interDist;
                    if (ratio > maxRatio) maxRatio = ratio;
                }
            }
            dbi += maxRatio;
        }

        return dbi / clusters.Count;
    }
}
