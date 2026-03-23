namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Computes ranking metrics: NDCG and DCG at various K values.
/// </summary>
public sealed class RankingMetricsTransform : ITransform
{
    private readonly string _labelColumn;
    private readonly string _scoreColumn;
    private readonly string _groupColumn;
    private readonly int[] _truncationLevels;

    public RankingMetricsTransform(
        string labelColumn = "Label",
        string scoreColumn = "Score",
        string groupColumn = "GroupId",
        int[]? truncationLevels = null)
    {
        _labelColumn = labelColumn;
        _scoreColumn = scoreColumn;
        _groupColumn = groupColumn;
        _truncationLevels = truncationLevels ?? [1, 3, 5, 10];
    }

    public TransformProperties Properties => new() { PreservesRowCount = false };

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        var builder = new SchemaBuilder();
        foreach (var k in _truncationLevels)
        {
            builder.AddColumn<double>($"NDCG@{k}");
            builder.AddColumn<double>($"DCG@{k}");
        }
        return builder.Build();
    }

    public IDataHandle Apply(IDataHandle input)
    {
        var groups = new Dictionary<string, List<(double Label, double Score)>>();

        using var cursor = input.GetCursor([_labelColumn, _scoreColumn, _groupColumn]);
        while (cursor.MoveNext())
        {
            string group;
            if (cursor.Current.TryGetValue<string>(_groupColumn, out var gs))
                group = gs;
            else
                group = MetricHelpers.ToInt(cursor.Current, _groupColumn).ToString();

            var label = MetricHelpers.ToDouble(cursor.Current, _labelColumn);
            var score = MetricHelpers.ToDouble(cursor.Current, _scoreColumn);

            if (!groups.ContainsKey(group)) groups[group] = [];
            groups[group].Add((label, score));
        }

        var columnData = new List<(string Name, Array Values)>();
        foreach (var k in _truncationLevels)
        {
            double avgDcg = 0, avgNdcg = 0;
            foreach (var group in groups.Values)
            {
                var ranked = group.OrderByDescending(x => x.Score).Take(k).ToList();
                var ideal = group.OrderByDescending(x => x.Label).Take(k).ToList();

                double dcg = ComputeDcg(ranked.Select(x => x.Label));
                double idealDcg = ComputeDcg(ideal.Select(x => x.Label));
                double ndcg = idealDcg > 0 ? dcg / idealDcg : 0;

                avgDcg += dcg;
                avgNdcg += ndcg;
            }
            int numGroups = groups.Count;
            columnData.Add(($"NDCG@{k}", new double[] { numGroups > 0 ? avgNdcg / numGroups : 0 }));
            columnData.Add(($"DCG@{k}", new double[] { numGroups > 0 ? avgDcg / numGroups : 0 }));
        }

        return InMemoryDataHandle.FromColumns(columnData.ToArray());
    }

    internal static double ComputeDcg(IEnumerable<double> relevances)
    {
        double dcg = 0;
        int i = 0;
        foreach (var rel in relevances)
        {
            dcg += (Math.Pow(2, rel) - 1) / Math.Log2(i + 2);
            i++;
        }
        return dcg;
    }
}
