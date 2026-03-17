namespace HPD.ML.LightGBM.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

internal static class TestHelper
{
    /// <summary>Create a simple DataHandle with float[] features and float labels.</summary>
    internal static IDataHandle Data(float[][] features, float[] labels)
    {
        return InMemoryDataHandle.FromColumns(
            ("Features", features),
            ("Label", labels));
    }

    /// <summary>Create a DataHandle with scalar float features and float labels.</summary>
    internal static IDataHandle ScalarData(float[] features, float[] labels)
    {
        return InMemoryDataHandle.FromColumns(
            ("Features", features),
            ("Label", labels));
    }

    /// <summary>Create a simple two-leaf regression tree.</summary>
    internal static RegressionTree TwoLeafTree(
        int splitFeature, double threshold, double leftValue, double rightValue)
    {
        return new RegressionTree(
            numLeaves: 2,
            splitFeatures: [splitFeature],
            thresholds: [threshold],
            leftChild: [~0],   // leaf 0
            rightChild: [~1],  // leaf 1
            isCategoricalSplit: [false],
            categoricalValues: [null],
            leafValues: [leftValue, rightValue]);
    }

    /// <summary>Create a single-leaf (stump) tree.</summary>
    internal static RegressionTree SingleLeafTree(double value)
    {
        return new RegressionTree(
            numLeaves: 1,
            splitFeatures: [],
            thresholds: [],
            leftChild: [],
            rightChild: [],
            isCategoricalSplit: [],
            categoricalValues: [],
            leafValues: [value]);
    }

    /// <summary>Collect all values of a float column from a DataHandle.</summary>
    internal static List<float> CollectFloat(IDataHandle data, string column)
    {
        var result = new List<float>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<float>(column));
        return result;
    }

    /// <summary>Collect all values of a bool column from a DataHandle.</summary>
    internal static List<bool> CollectBool(IDataHandle data, string column)
    {
        var result = new List<bool>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<bool>(column));
        return result;
    }

    /// <summary>Collect all values of a uint column from a DataHandle.</summary>
    internal static List<uint> CollectUint(IDataHandle data, string column)
    {
        var result = new List<uint>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<uint>(column));
        return result;
    }

    /// <summary>Collect all float[] values from a DataHandle column.</summary>
    internal static List<float[]> CollectFloatArray(IDataHandle data, string column)
    {
        var result = new List<float[]>();
        using var cursor = data.GetCursor([column]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<float[]>(column));
        return result;
    }

    internal static int CountRows(IDataHandle data)
    {
        int count = 0;
        using var cursor = data.GetCursor(data.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext()) count++;
        return count;
    }
}
