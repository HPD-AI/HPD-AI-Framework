namespace HPD.ML.Regression;

using HPD.ML.Abstractions;

/// <summary>
/// Shared helper that extracts regression training data into typed arrays.
/// Handles both float[] vector features and scalar float features.
/// Labels are always float (continuous target).
/// </summary>
internal static class RegressionDataLoader
{
    public static (List<double[]> Features, List<double> Labels, int FeatureCount) Load(
        IDataHandle data, string featureColumn, string labelColumn)
    {
        var features = new List<double[]>();
        var labels = new List<double>();
        int featureCount = 0;

        using var cursor = data.GetCursor([featureColumn, labelColumn]);
        while (cursor.MoveNext())
        {
            var row = cursor.Current;
            labels.Add(Convert.ToDouble(row.GetValue<object>(labelColumn)));

            if (row.TryGetValue<float[]>(featureColumn, out var vector))
            {
                featureCount = vector.Length;
                var d = new double[vector.Length];
                for (int i = 0; i < vector.Length; i++)
                    d[i] = vector[i];
                features.Add(d);
            }
            else
            {
                featureCount = 1;
                var scalar = row.GetValue<float>(featureColumn);
                features.Add([scalar]);
            }
        }

        return (features, labels, featureCount);
    }
}
