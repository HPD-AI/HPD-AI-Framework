namespace HPD.ML.Clustering;

using HPD.ML.Abstractions;

/// <summary>
/// Materializes feature vectors from an IDataHandle for clustering.
/// </summary>
internal static class ClusteringDataLoader
{
    public static (float[][] Data, int Dimensionality) Load(IDataHandle data, string featureColumn)
    {
        var rows = new List<float[]>();

        using var cursor = data.GetCursor([featureColumn]);
        while (cursor.MoveNext())
        {
            var row = cursor.Current;

            if (row.TryGetValue<float[]>(featureColumn, out var vector))
            {
                rows.Add((float[])vector.Clone());
            }
            else
            {
                var scalar = row.GetValue<float>(featureColumn);
                rows.Add([scalar]);
            }
        }

        int dim = rows.Count > 0 ? rows[0].Length : 0;
        return (rows.ToArray(), dim);
    }
}
