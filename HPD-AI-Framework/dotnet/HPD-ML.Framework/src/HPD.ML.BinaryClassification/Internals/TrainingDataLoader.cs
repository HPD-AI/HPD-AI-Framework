namespace HPD.ML.BinaryClassification;

using HPD.ML.Abstractions;

/// <summary>
/// Shared helper for loading training data from IDataHandle into arrays
/// suitable for linear learner optimization loops.
/// </summary>
internal static class TrainingDataLoader
{
    public static (List<double[]> Features, List<bool> Labels, int FeatureCount) Load(
        IDataHandle data, string featureColumn, string labelColumn)
    {
        var features = new List<double[]>();
        var labels = new List<bool>();
        int featureCount = 0;

        using var cursor = data.GetCursor([featureColumn, labelColumn]);
        while (cursor.MoveNext())
        {
            var row = cursor.Current;
            labels.Add(Convert.ToBoolean(row.GetValue<object>(labelColumn)));

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
