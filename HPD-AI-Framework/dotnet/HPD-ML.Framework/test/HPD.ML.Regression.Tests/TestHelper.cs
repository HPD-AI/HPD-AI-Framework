namespace HPD.ML.Regression.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public static class TestHelper
{
    public static IDataHandle Data(params (string Name, Array Values)[] columns)
        => InMemoryDataHandle.FromColumns(columns);

    /// <summary>
    /// Creates linear regression data: y = slope * x + intercept + noise.
    /// Single feature.
    /// </summary>
    public static IDataHandle LinearData(
        double slope = 2.0, double intercept = 1.0,
        double noise = 0.1, int n = 20, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        var labels = new float[n];

        for (int i = 0; i < n; i++)
        {
            float x = (float)(rng.NextDouble() * 4 - 2); // [-2, 2]
            features[i] = [x];
            labels[i] = (float)(slope * x + intercept + noise * (rng.NextDouble() - 0.5));
        }

        return Data(("Features", features), ("Label", labels));
    }

    /// <summary>
    /// Creates 2D linear regression data: y = w1*x1 + w2*x2 + intercept + noise.
    /// </summary>
    public static IDataHandle LinearData2D(
        double w1 = 3.0, double w2 = -1.0, double intercept = 2.0,
        double noise = 0.1, int n = 30, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        var labels = new float[n];

        for (int i = 0; i < n; i++)
        {
            float x1 = (float)(rng.NextDouble() * 4 - 2);
            float x2 = (float)(rng.NextDouble() * 4 - 2);
            features[i] = [x1, x2];
            labels[i] = (float)(w1 * x1 + w2 * x2 + intercept + noise * (rng.NextDouble() - 0.5));
        }

        return Data(("Features", features), ("Label", labels));
    }

    /// <summary>
    /// Creates Poisson-like count data: y ~ Poisson(exp(w·x + b)).
    /// Uses deterministic rounding for reproducibility.
    /// </summary>
    public static IDataHandle CountData(int n = 30, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        var labels = new float[n];

        for (int i = 0; i < n; i++)
        {
            float x = (float)(rng.NextDouble() * 2); // [0, 2]
            features[i] = [x];
            // True model: E[y] = exp(0.5*x + 0.1)
            double rate = Math.Exp(0.5 * x + 0.1);
            labels[i] = (float)Math.Max(0, Math.Round(rate + rng.NextDouble() - 0.5));
        }

        return Data(("Features", features), ("Label", labels));
    }

    /// <summary>
    /// Simple 1D data with known linear relationship.
    /// </summary>
    public static IDataHandle Simple1DData(int n = 10, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        var labels = new float[n];

        for (int i = 0; i < n; i++)
        {
            float x = (float)rng.NextDouble();
            features[i] = [x];
            labels[i] = 2f * x + 1f;
        }

        return Data(("Features", features), ("Label", labels));
    }

    public static List<float> CollectFloat(IDataHandle data, string column)
    {
        var values = new List<float>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<float>(column));
        return values;
    }

    public static int CountRows(IDataHandle data)
    {
        int count = 0;
        using var cursor = data.GetCursor(data.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext()) count++;
        return count;
    }

    /// <summary>
    /// Computes MSE between Score column and Label column.
    /// </summary>
    public static double MSE(IDataHandle predictions, string labelColumn = "Label")
    {
        double sum = 0;
        int count = 0;
        using var cursor = predictions.GetCursor(["Score", labelColumn]);
        while (cursor.MoveNext())
        {
            var score = cursor.Current.GetValue<float>("Score");
            var label = Convert.ToDouble(cursor.Current.GetValue<object>(labelColumn));
            double diff = score - label;
            sum += diff * diff;
            count++;
        }
        return count > 0 ? sum / count : 0;
    }

    public static double WeightNorm(LinearModelParameters p)
    {
        double sum = 0;
        for (int i = 0; i < p.FeatureCount; i++)
            sum += (double)p.Weights[i] * (double)p.Weights[i];
        return Math.Sqrt(sum);
    }
}

public sealed class Observer<T> : IObserver<T>
{
    private readonly Action<T> _onNext;
    public Observer(Action<T> onNext) => _onNext = onNext;
    public void OnNext(T value) => _onNext(value);
    public void OnCompleted() { }
    public void OnError(Exception error) { }
}
