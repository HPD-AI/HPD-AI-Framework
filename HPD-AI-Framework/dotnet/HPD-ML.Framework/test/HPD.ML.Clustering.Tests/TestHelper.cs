namespace HPD.ML.Clustering.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public static class TestHelper
{
    /// <summary>Creates an InMemoryDataHandle from column tuples.</summary>
    public static IDataHandle Data(params (string Name, Array Values)[] columns)
        => InMemoryDataHandle.FromColumns(columns);

    /// <summary>
    /// Creates K Gaussian blobs in 2D. Each blob centered at angle k*(2π/K) on a circle of given radius.
    /// </summary>
    public static IDataHandle BlobData(int pointsPerCluster, int k = 3, float radius = 10f, float spread = 1f, int seed = 42)
    {
        var rng = new Random(seed);
        int n = pointsPerCluster * k;
        var features = new float[n][];

        for (int c = 0; c < k; c++)
        {
            double angle = 2 * Math.PI * c / k;
            float cx = radius * (float)Math.Cos(angle);
            float cy = radius * (float)Math.Sin(angle);

            for (int i = 0; i < pointsPerCluster; i++)
            {
                int idx = c * pointsPerCluster + i;
                features[idx] = [
                    cx + spread * (float)(rng.NextDouble() * 2 - 1),
                    cy + spread * (float)(rng.NextDouble() * 2 - 1)
                ];
            }
        }

        return Data(("Features", features));
    }

    /// <summary>Creates N random points in given dimensionality.</summary>
    public static IDataHandle RandomData(int n, int dim = 2, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        for (int i = 0; i < n; i++)
        {
            features[i] = new float[dim];
            for (int d = 0; d < dim; d++)
                features[i][d] = (float)(rng.NextDouble() * 10);
        }
        return Data(("Features", features));
    }

    /// <summary>Creates N 1D scalar feature points.</summary>
    public static IDataHandle Scalar1DData(int n, int seed = 42)
    {
        var rng = new Random(seed);
        var features = new float[n][];
        for (int i = 0; i < n; i++)
            features[i] = [(float)(rng.NextDouble() * 10)];
        return Data(("Features", features));
    }

    /// <summary>Materializes raw float[][] from test data for init tests.</summary>
    public static float[][] MaterializeFeatures(int pointsPerCluster, int k = 3, float radius = 10f, float spread = 1f, int seed = 42)
    {
        var rng = new Random(seed);
        int n = pointsPerCluster * k;
        var data = new float[n][];

        for (int c = 0; c < k; c++)
        {
            double angle = 2 * Math.PI * c / k;
            float cx = radius * (float)Math.Cos(angle);
            float cy = radius * (float)Math.Sin(angle);

            for (int i = 0; i < pointsPerCluster; i++)
            {
                int idx = c * pointsPerCluster + i;
                data[idx] = [
                    cx + spread * (float)(rng.NextDouble() * 2 - 1),
                    cy + spread * (float)(rng.NextDouble() * 2 - 1)
                ];
            }
        }
        return data;
    }

    public static int CountRows(IDataHandle data)
    {
        int count = 0;
        using var cursor = data.GetCursor(data.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext()) count++;
        return count;
    }

    public static List<T> CollectColumn<T>(IDataHandle data, string columnName)
    {
        var result = new List<T>();
        using var cursor = data.GetCursor(data.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<T>(columnName));
        return result;
    }

    public static List<uint> CollectUInt(IDataHandle data, string columnName)
        => CollectColumn<uint>(data, columnName);

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
