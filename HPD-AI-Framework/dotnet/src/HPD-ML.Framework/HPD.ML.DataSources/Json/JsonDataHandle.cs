namespace HPD.ML.DataSources;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Lazy IDataHandle backed by a JSON or JSONL file.
/// Schema is inferred on construction. Data streams lazily.
/// </summary>
public sealed class JsonDataHandle : IDataHandle
{
    private readonly string _path;
    private readonly JsonOptions _options;
    private readonly Schema _schema;
    private readonly bool _isJsonLines;

    private JsonDataHandle(string path, JsonOptions options, Schema schema, bool isJsonLines)
    {
        _path = path;
        _options = options;
        _schema = schema;
        _isJsonLines = isJsonLines;
    }

    public ISchema Schema => _schema;
    public long? RowCount => null;
    public OrderingPolicy Ordering => OrderingPolicy.StrictlyOrdered;
    public MaterializationCapabilities Capabilities => MaterializationCapabilities.CursorOnly;

    public static JsonDataHandle Create(string path, JsonOptions? options = null)
    {
        options ??= new JsonOptions();

        bool isJsonLines;
        if (options.IsJsonLines.HasValue)
        {
            isJsonLines = options.IsJsonLines.Value;
        }
        else
        {
            using var peek = new StreamReader(path, options.Encoding);
            int firstChar;
            do { firstChar = peek.Read(); }
            while (firstChar == ' ' || firstChar == '\n' || firstChar == '\r');
            isJsonLines = firstChar != '[';
        }

        var schema = InferSchema(path, options, isJsonLines);
        return new JsonDataHandle(path, options, schema, isJsonLines);
    }

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => new JsonCursor(_path, _schema, _options, _isJsonLines, columnsNeeded.ToHashSet());

    public IDataHandle Materialize()
    {
        var columns = new Dictionary<string, List<object>>();
        foreach (var col in _schema.Columns)
            columns[col.Name] = [];

        using var cursor = GetCursor(_schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext())
        {
            foreach (var col in _schema.Columns)
                columns[col.Name].Add(cursor.Current.GetValue<object>(col.Name));
        }

        var arrays = new Dictionary<string, Array>();
        foreach (var col in _schema.Columns)
        {
            var list = columns[col.Name];
            var array = Array.CreateInstance(col.Type.ClrType, list.Count);
            for (int i = 0; i < list.Count; i++)
                array.SetValue(list[i], i);
            arrays[col.Name] = array;
        }

        return new InMemoryDataHandle(_schema, arrays);
    }

    public async IAsyncEnumerable<IRow> StreamRows(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var cursor = GetCursor(_schema.Columns.Select(c => c.Name));
        while (cursor.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            yield return cursor.Current;
        }
    }

    public bool TryGetColumnBatch<T>(
        string columnName, int startRow, int rowCount,
        out ReadOnlyTensorSpan<T> batch) where T : unmanaged, INumber<T>
    {
        batch = default;
        return false;
    }

    private static Schema InferSchema(string path, JsonOptions options, bool isJsonLines)
    {
        var properties = new Dictionary<string, Type>();
        int scanned = 0;
        int limit = options.InferenceScanRows;

        if (isJsonLines)
        {
            using var reader = new StreamReader(path, options.Encoding);
            while (reader.ReadLine() is { } line && scanned < limit)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                CollectProperties(doc.RootElement, properties, "", options.MaxFlattenDepth, 0);
                scanned++;
            }
        }
        else
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (scanned >= limit) break;
                CollectProperties(element, properties, "", options.MaxFlattenDepth, 0);
                scanned++;
            }
        }

        var builder = new SchemaBuilder();
        foreach (var (name, type) in properties)
        {
            var columnName = options.PropertyMapping?.TryGetValue(name, out var mapped) == true
                ? mapped : name;
            var finalType = options.TypeHints?.TryGetValue(columnName, out var hinted) == true
                ? hinted : type;
            builder.AddColumn(columnName, new FieldType(finalType));
        }

        return builder.Build();
    }

    internal static void CollectProperties(
        JsonElement element, Dictionary<string, Type> properties,
        string prefix, int maxDepth, int currentDepth)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var name = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

            if (prop.Value.ValueKind == JsonValueKind.Object && currentDepth < maxDepth)
            {
                CollectProperties(prop.Value, properties, name, maxDepth, currentDepth + 1);
            }
            else
            {
                var type = prop.Value.ValueKind switch
                {
                    JsonValueKind.Number when prop.Value.TryGetInt32(out _) => typeof(int),
                    JsonValueKind.Number when prop.Value.TryGetInt64(out _) => typeof(long),
                    JsonValueKind.Number => typeof(double),
                    JsonValueKind.True or JsonValueKind.False => typeof(bool),
                    _ => typeof(string)
                };

                if (properties.TryGetValue(name, out var existing) && existing != type)
                    properties[name] = WidenType(existing, type);
                else
                    properties[name] = type;
            }
        }
    }

    internal static Type WidenType(Type a, Type b)
    {
        if (a == b) return a;
        if (a == typeof(string) || b == typeof(string)) return typeof(string);
        if (a == typeof(double) || b == typeof(double)) return typeof(double);
        if (a == typeof(long) || b == typeof(long)) return typeof(long);
        return typeof(string);
    }
}
