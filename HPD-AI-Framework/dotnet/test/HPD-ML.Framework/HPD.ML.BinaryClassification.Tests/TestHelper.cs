namespace HPD.ML.BinaryClassification.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public static class TestHelper
{
    /// <summary>
    /// Creates an InMemoryDataHandle from column tuples.
    /// </summary>
    public static IDataHandle Data(params (string Name, Array Values)[] columns)
        => InMemoryDataHandle.FromColumns(columns);

    /// <summary>
    /// Creates linearly separable 2D binary classification data.
    /// Points where x1 + x2 > threshold are positive.
    /// Returns data with "Features" (float[]) and "Label" (bool) columns.
    /// </summary>
    public static IDataHandle LinearSeparableData(int n = 20, double threshold = 1.0, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        var labels = new bool[n];

        for (int i = 0; i < n; i++)
        {
            float x1 = (float)(rng.NextDouble() * 4 - 2); // [-2, 2]
            float x2 = (float)(rng.NextDouble() * 4 - 2);
            features[i] = [x1, x2];
            labels[i] = x1 + x2 > threshold;
        }

        return Data(("Features", features), ("Label", labels));
    }

    /// <summary>
    /// Creates simple 1D data where feature > boundary → true.
    /// </summary>
    public static IDataHandle Simple1DData(float boundary = 0.5f, int n = 20, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        var labels = new bool[n];

        for (int i = 0; i < n; i++)
        {
            float x = (float)rng.NextDouble();
            features[i] = [x];
            labels[i] = x > boundary;
        }

        return Data(("Features", features), ("Label", labels));
    }

    /// <summary>
    /// Creates XOR data (not linearly separable).
    /// </summary>
    public static IDataHandle XorData()
    {
        return Data(
            ("Features", new float[][] { [0, 0], [0, 1], [1, 0], [1, 1] }),
            ("Label", new bool[] { false, true, true, false }));
    }

    /// <summary>
    /// Collects float column values from a data handle.
    /// </summary>
    public static List<float> CollectFloat(IDataHandle data, string column)
    {
        var values = new List<float>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<float>(column));
        return values;
    }

    /// <summary>
    /// Collects bool column values from a data handle.
    /// </summary>
    public static List<bool> CollectBool(IDataHandle data, string column)
    {
        var values = new List<bool>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<bool>(column));
        return values;
    }

    /// <summary>
    /// Computes accuracy: fraction of correct predictions.
    /// </summary>
    public static double Accuracy(IDataHandle predictions, string labelColumn = "Label")
    {
        int correct = 0, total = 0;
        using var cursor = predictions.GetCursor(["PredictedLabel", labelColumn]);
        while (cursor.MoveNext())
        {
            var predicted = cursor.Current.GetValue<bool>("PredictedLabel");
            var actual = Convert.ToBoolean(cursor.Current.GetValue<object>(labelColumn));
            if (predicted == actual) correct++;
            total++;
        }
        return total > 0 ? (double)correct / total : 0;
    }

    /// <summary>
    /// Counts rows in a data handle.
    /// </summary>
    public static int CountRows(IDataHandle data)
    {
        int count = 0;
        using var cursor = data.GetCursor(data.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext()) count++;
        return count;
    }

    /// <summary>
    /// Gets the weight norm from LinearModelParameters.
    /// </summary>
    public static double WeightNorm(LinearModelParameters p)
    {
        double sum = 0;
        for (int i = 0; i < p.FeatureCount; i++)
            sum += (double)p.Weights[i] * (double)p.Weights[i];
        return Math.Sqrt(sum);
    }
}
