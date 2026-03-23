using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

/// <summary>
/// Shared helpers for DataHandle tests.
/// </summary>
internal static class TestHelpers
{
    public static InMemoryDataHandle CreateSimpleHandle(int rowCount = 5)
    {
        var ids = Enumerable.Range(0, rowCount).ToArray();
        var values = Enumerable.Range(0, rowCount).Select(i => (float)i * 10).ToArray();
        return InMemoryDataHandle.FromColumns(
            ("Id", ids),
            ("Value", values));
    }

    public static InMemoryDataHandle CreateThreeColumnHandle(int rowCount = 5)
    {
        var a = Enumerable.Range(0, rowCount).ToArray();
        var b = Enumerable.Range(0, rowCount).Select(i => (float)i).ToArray();
        var c = Enumerable.Range(0, rowCount).Select(i => (double)i * 100).ToArray();
        return InMemoryDataHandle.FromColumns(("A", a), ("B", b), ("C", c));
    }

    public static List<int> CollectIntColumn(IDataHandle handle, string columnName)
    {
        var results = new List<int>();
        using var cursor = handle.GetCursor([columnName]);
        while (cursor.MoveNext())
            results.Add(cursor.Current.GetValue<int>(columnName));
        return results;
    }

    public static List<float> CollectFloatColumn(IDataHandle handle, string columnName)
    {
        var results = new List<float>();
        using var cursor = handle.GetCursor([columnName]);
        while (cursor.MoveNext())
            results.Add(cursor.Current.GetValue<float>(columnName));
        return results;
    }
}
