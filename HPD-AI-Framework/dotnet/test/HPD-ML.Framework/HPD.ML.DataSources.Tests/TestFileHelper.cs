namespace HPD.ML.DataSources.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Helpers for creating temporary test files and collecting data from handles.
/// </summary>
internal static class TestFileHelper
{
    /// <summary>Write content to a temp file and return the path. Caller should delete.</summary>
    public static string WriteTempFile(string content, string extension = ".csv")
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpdml_test_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Collect all int values from a column via cursor.</summary>
    public static List<int> CollectIntColumn(IDataHandle handle, string column)
    {
        var values = new List<int>();
        using var cursor = handle.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<int>(column));
        return values;
    }

    /// <summary>Collect all double values from a column via cursor.</summary>
    public static List<double> CollectDoubleColumn(IDataHandle handle, string column)
    {
        var values = new List<double>();
        using var cursor = handle.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<double>(column));
        return values;
    }

    /// <summary>Collect all float values from a column via cursor.</summary>
    public static List<float> CollectFloatColumn(IDataHandle handle, string column)
    {
        var values = new List<float>();
        using var cursor = handle.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<float>(column));
        return values;
    }

    /// <summary>Collect all string values from a column via cursor.</summary>
    public static List<string> CollectStringColumn(IDataHandle handle, string column)
    {
        var values = new List<string>();
        using var cursor = handle.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<string>(column));
        return values;
    }

    /// <summary>Collect all bool values from a column via cursor.</summary>
    public static List<bool> CollectBoolColumn(IDataHandle handle, string column)
    {
        var values = new List<bool>();
        using var cursor = handle.GetCursor([column]);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<bool>(column));
        return values;
    }

    /// <summary>Count rows via cursor.</summary>
    public static int CountRows(IDataHandle handle)
    {
        int count = 0;
        using var cursor = handle.GetCursor(handle.Schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext()) count++;
        return count;
    }
}
