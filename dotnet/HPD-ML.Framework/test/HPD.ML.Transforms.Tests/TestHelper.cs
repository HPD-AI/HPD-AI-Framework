namespace HPD.ML.Transforms.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

internal static class TestHelper
{
    /// <summary>Build an InMemoryDataHandle from column arrays.</summary>
    public static InMemoryDataHandle Data(params (string Name, Array Values)[] columns)
        => InMemoryDataHandle.FromColumns(columns);

    /// <summary>Collect all values from a float column.</summary>
    public static List<float> CollectFloat(IDataHandle handle, string col)
    {
        var result = new List<float>();
        using var cursor = handle.GetCursor([col]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<float>(col));
        return result;
    }

    /// <summary>Collect all values from an int column.</summary>
    public static List<int> CollectInt(IDataHandle handle, string col)
    {
        var result = new List<int>();
        using var cursor = handle.GetCursor([col]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<int>(col));
        return result;
    }

    /// <summary>Collect all values from a string column.</summary>
    public static List<string> CollectString(IDataHandle handle, string col)
    {
        var result = new List<string>();
        using var cursor = handle.GetCursor([col]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<string>(col));
        return result;
    }

    /// <summary>Collect all values from an object column.</summary>
    public static List<object> CollectObject(IDataHandle handle, string col)
    {
        var result = new List<object>();
        using var cursor = handle.GetCursor([col]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<object>(col));
        return result;
    }

    /// <summary>Collect all values from a bool column.</summary>
    public static List<bool> CollectBool(IDataHandle handle, string col)
    {
        var result = new List<bool>();
        using var cursor = handle.GetCursor([col]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<bool>(col));
        return result;
    }

    /// <summary>Collect float[] vector column values.</summary>
    public static List<float[]> CollectFloatArray(IDataHandle handle, string col)
    {
        var result = new List<float[]>();
        using var cursor = handle.GetCursor([col]);
        while (cursor.MoveNext())
            result.Add(cursor.Current.GetValue<float[]>(col));
        return result;
    }

    /// <summary>Count rows in a data handle.</summary>
    public static int CountRows(IDataHandle handle)
    {
        int count = 0;
        using var cursor = handle.GetCursor(handle.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext()) count++;
        return count;
    }
}
