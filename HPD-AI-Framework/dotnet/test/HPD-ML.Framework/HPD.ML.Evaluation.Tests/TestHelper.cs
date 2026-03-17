namespace HPD.ML.Evaluation.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

internal static class TestHelper
{
    public static IDataHandle Data(params (string Name, Array Values)[] columns)
        => InMemoryDataHandle.FromColumns(columns);

    public static double ReadMetric(IDataHandle metrics, string name)
    {
        using var cursor = metrics.GetCursor([name]);
        cursor.MoveNext();
        return cursor.Current.GetValue<double>(name);
    }

    public static string ReadString(IDataHandle data, string column, int row = 0)
    {
        using var cursor = data.GetCursor([column]);
        for (int i = 0; i <= row; i++) cursor.MoveNext();
        return cursor.Current.GetValue<string>(column);
    }

    public static int ReadInt(IDataHandle data, string column, int row = 0)
    {
        using var cursor = data.GetCursor([column]);
        for (int i = 0; i <= row; i++) cursor.MoveNext();
        return cursor.Current.GetValue<int>(column);
    }

    public static int CountRows(IDataHandle data)
    {
        int count = 0;
        using var cursor = data.GetCursor(data.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext()) count++;
        return count;
    }

    public static List<double> CollectDouble(IDataHandle data, string column)
    {
        var result = new List<double>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<double>(column));
        return result;
    }

    public static List<string> CollectString(IDataHandle data, string column)
    {
        var result = new List<string>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<string>(column));
        return result;
    }

    /// <summary>Creates a simple binary classification dataset.</summary>
    public static IDataHandle BinaryData(bool[] labels, double[] scores)
        => Data(("Label", labels), ("Score", scores));

    /// <summary>Creates a regression dataset.</summary>
    public static IDataHandle RegressionData(double[] labels, double[] scores)
        => Data(("Label", labels), ("Score", scores));

    /// <summary>Creates a multiclass dataset with int labels and predictions.</summary>
    public static IDataHandle MulticlassData(int[] labels, int[] predicted, float[][]? scores = null)
    {
        if (scores is not null)
            return Data(("Label", labels), ("PredictedLabel", predicted), ("Score", scores));
        return Data(("Label", labels), ("PredictedLabel", predicted), ("Score", new float[labels.Length][]));
    }

    /// <summary>Creates ranking data with groups.</summary>
    public static IDataHandle RankingData(string[] groups, double[] labels, double[] scores)
        => Data(("GroupId", groups), ("Label", labels), ("Score", scores));
}
