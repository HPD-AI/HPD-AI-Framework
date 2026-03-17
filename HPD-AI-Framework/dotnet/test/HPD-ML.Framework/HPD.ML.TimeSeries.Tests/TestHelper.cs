namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public static class TestHelper
{
    /// <summary>Generate sine wave data with optional noise.</summary>
    public static IDataHandle SineData(int n, double period = 12, double amplitude = 1, double noise = 0, int seed = 42)
    {
        var rng = new Random(seed);
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = (float)(amplitude * Math.Sin(2 * Math.PI * i / period) + noise * (rng.NextDouble() - 0.5));
        return InMemoryDataHandle.FromColumns(("Value", values));
    }

    /// <summary>Generate constant data with a spike at a given index.</summary>
    public static IDataHandle SpikeData(int n, int spikeIndex, float baseline = 10f, float spikeValue = 100f)
    {
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = i == spikeIndex ? spikeValue : baseline;
        return InMemoryDataHandle.FromColumns(("Value", values));
    }

    /// <summary>Generate data with a mean shift at the given index.</summary>
    public static IDataHandle MeanShiftData(int n, int shiftIndex, float meanBefore = 0f, float meanAfter = 10f, float noise = 0.1f, int seed = 42)
    {
        var rng = new Random(seed);
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = (i < shiftIndex ? meanBefore : meanAfter) + (float)(noise * (rng.NextDouble() - 0.5));
        return InMemoryDataHandle.FromColumns(("Value", values));
    }

    /// <summary>Generate constant data.</summary>
    public static IDataHandle ConstantData(int n, float value = 5f)
    {
        var values = new float[n];
        Array.Fill(values, value);
        return InMemoryDataHandle.FromColumns(("Value", values));
    }

    /// <summary>Generate random noise data.</summary>
    public static IDataHandle NoiseData(int n, int seed = 42)
    {
        var rng = new Random(seed);
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = (float)(rng.NextDouble() * 2 - 1);
        return InMemoryDataHandle.FromColumns(("Value", values));
    }

    /// <summary>Generate linear trend data.</summary>
    public static IDataHandle LinearData(int n, double slope = 1, double intercept = 0)
    {
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = (float)(slope * i + intercept);
        return InMemoryDataHandle.FromColumns(("Value", values));
    }

    /// <summary>Generate repeating seasonal pattern + optional trend.</summary>
    public static IDataHandle SeasonalData(int n, int period, double[] pattern, double trendSlope = 0)
    {
        var values = new float[n];
        for (int i = 0; i < n; i++)
            values[i] = (float)(pattern[i % period] + trendSlope * i);
        return InMemoryDataHandle.FromColumns(("Value", values));
    }

    public static int CountRows(IDataHandle data)
    {
        int count = 0;
        var allCols = data.Schema.Columns.Select(c => c.Name);
        using var cursor = data.GetCursor(allCols);
        while (cursor.MoveNext()) count++;
        return count;
    }

    public static List<T> CollectColumn<T>(IDataHandle data, string columnName)
    {
        var result = new List<T>();
        var allCols = data.Schema.Columns.Select(c => c.Name);
        using var cursor = data.GetCursor(allCols);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<T>(columnName));
        return result;
    }

    public static List<float> CollectFloat(IDataHandle data, string columnName)
        => CollectColumn<float>(data, columnName);

    public static List<bool> CollectBool(IDataHandle data, string columnName)
        => CollectColumn<bool>(data, columnName);

    public static List<float[]> CollectFloatArray(IDataHandle data, string columnName)
        => CollectColumn<float[]>(data, columnName);
}

public class Observer<T>(Action<T> onNext) : IObserver<T>
{
    private bool _completed;
    public bool Completed => _completed;
    public void OnNext(T value) => onNext(value);
    public void OnCompleted() => _completed = true;
    public void OnError(Exception error) { }
}
