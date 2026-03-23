namespace HPD.ML.DataSources;

using HPD.ML.Core;

/// <summary>
/// Creates an InMemoryDataHandle from explicit rows. For testing and small datasets.
/// </summary>
public static class RowsDataHandle
{
    /// <summary>
    /// Create from explicit row dictionaries.
    /// </summary>
    public static InMemoryDataHandle Create(
        Schema schema,
        params ReadOnlySpan<Dictionary<string, object>> rows)
    {
        var columns = new Dictionary<string, Array>();
        foreach (var col in schema.Columns)
        {
            var array = Array.CreateInstance(col.Type.ClrType, rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].TryGetValue(col.Name, out var val))
                    array.SetValue(val, i);
            }
            columns[col.Name] = array;
        }

        return new InMemoryDataHandle(schema, columns);
    }
}
