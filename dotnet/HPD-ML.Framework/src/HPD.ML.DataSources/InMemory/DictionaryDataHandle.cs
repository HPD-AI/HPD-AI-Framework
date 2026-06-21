namespace HPD.ML.DataSources;

using HPD.ML.Core;

/// <summary>
/// Creates an InMemoryDataHandle from a sequence of string-keyed dictionaries.
/// Useful for dynamic data (API responses, config-driven schemas).
/// </summary>
public static class DictionaryDataHandle
{
    /// <summary>
    /// Create from dictionaries with schema inference from all rows.
    /// Types are widened across all rows to find the best common type.
    /// </summary>
    public static InMemoryDataHandle Create(
        IEnumerable<IReadOnlyDictionary<string, object>> rows)
    {
        var rowList = rows.ToList();
        if (rowList.Count == 0)
            throw new ArgumentException("At least one row required for schema inference.", nameof(rows));

        // Infer schema from all rows — widen types across rows
        var columnTypes = new Dictionary<string, Type>();
        foreach (var row in rowList)
        {
            foreach (var (key, value) in row)
            {
                var type = value?.GetType() ?? typeof(string);
                if (columnTypes.TryGetValue(key, out var existing) && existing != type)
                    columnTypes[key] = JsonDataHandle.WidenType(existing, type);
                else
                    columnTypes.TryAdd(key, type);
            }
        }

        var builder = new SchemaBuilder();
        foreach (var (name, type) in columnTypes)
            builder.AddColumn(name, new FieldType(type));
        var schema = builder.Build();

        return CreateFromSchema(rowList, schema);
    }

    /// <summary>
    /// Create from dictionaries with explicit schema.
    /// </summary>
    public static InMemoryDataHandle Create(
        IEnumerable<IReadOnlyDictionary<string, object>> rows,
        Schema schema)
        => CreateFromSchema(rows.ToList(), schema);

    private static InMemoryDataHandle CreateFromSchema(
        List<IReadOnlyDictionary<string, object>> rowList, Schema schema)
    {
        var columns = new Dictionary<string, Array>();
        foreach (var col in schema.Columns)
        {
            var array = Array.CreateInstance(col.Type.ClrType, rowList.Count);
            for (int i = 0; i < rowList.Count; i++)
            {
                if (rowList[i].TryGetValue(col.Name, out var val))
                    array.SetValue(val, i);
            }
            columns[col.Name] = array;
        }

        return new InMemoryDataHandle(schema, columns);
    }
}
