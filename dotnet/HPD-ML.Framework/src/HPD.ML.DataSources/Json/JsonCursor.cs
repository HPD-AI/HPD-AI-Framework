namespace HPD.ML.DataSources;

using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Streaming cursor over JSON/JSONL. Parses one record at a time.
/// </summary>
internal sealed class JsonCursor : IRowCursor
{
    private readonly StreamReader? _lineReader;
    private readonly List<JsonElement>? _arrayElements;
    private readonly JsonDocument? _arrayDoc;
    private readonly Schema _schema;
    private readonly JsonOptions _options;
    private readonly HashSet<string> _activeColumns;
    private JsonRow? _current;
    private JsonDocument? _currentLineDoc;
    private int _arrayIndex;

    public JsonCursor(string path, Schema schema, JsonOptions options, bool isJsonLines, HashSet<string> activeColumns)
    {
        _schema = schema;
        _options = options;
        _activeColumns = activeColumns;

        if (isJsonLines)
        {
            _lineReader = new StreamReader(path, options.Encoding);
        }
        else
        {
            var stream = File.OpenRead(path);
            _arrayDoc = JsonDocument.Parse(stream);
            // Copy elements to a list to avoid struct enumerator issues
            _arrayElements = [];
            foreach (var element in _arrayDoc.RootElement.EnumerateArray())
                _arrayElements.Add(element);
            _arrayIndex = 0;
        }
    }

    public IRow Current => _current
        ?? throw new InvalidOperationException("Cursor not positioned.");

    public bool MoveNext()
    {
        if (_lineReader is not null)
        {
            while (_lineReader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                _currentLineDoc?.Dispose();
                _currentLineDoc = JsonDocument.Parse(line);
                _current = new JsonRow(_schema, _currentLineDoc.RootElement, _options);
                return true;
            }
            return false;
        }

        if (_arrayElements is not null && _arrayIndex < _arrayElements.Count)
        {
            _current = new JsonRow(_schema, _arrayElements[_arrayIndex], _options);
            _arrayIndex++;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        _lineReader?.Dispose();
        _currentLineDoc?.Dispose();
        _arrayDoc?.Dispose();
    }
}

/// <summary>
/// A single JSON row. Reads values from a JsonElement on demand.
/// </summary>
internal sealed class JsonRow : IRow
{
    private readonly JsonElement _element;
    private readonly JsonOptions _options;

    public JsonRow(ISchema schema, JsonElement element, JsonOptions options)
    {
        Schema = schema;
        _element = element;
        _options = options;
    }

    public ISchema Schema { get; }

    public T GetValue<T>(string columnName) where T : allows ref struct
    {
        var propertyPath = ResolvePropertyPath(columnName);
        var element = NavigatePath(_element, propertyPath);
        return DeserializeElement<T>(element, columnName);
    }

    public bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct
    {
        try
        {
            value = GetValue<T>(columnName);
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }

    private string ResolvePropertyPath(string columnName)
    {
        if (_options.PropertyMapping is not null)
        {
            foreach (var (jsonPath, mapped) in _options.PropertyMapping)
            {
                if (mapped == columnName) return jsonPath;
            }
        }
        return columnName;
    }

    private static JsonElement NavigatePath(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (!current.TryGetProperty(segment, out current))
                throw new KeyNotFoundException($"JSON property '{path}' not found.");
        }
        return current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T DeserializeElement<T>(JsonElement element, string columnName) where T : allows ref struct
    {
        var targetType = typeof(T);

        if (targetType == typeof(string)) { var v = element.ToString() ?? ""; return Unsafe.As<string, T>(ref v); }
        if (targetType == typeof(int)) { var v = element.GetInt32(); return Unsafe.As<int, T>(ref v); }
        if (targetType == typeof(long)) { var v = element.GetInt64(); return Unsafe.As<long, T>(ref v); }
        if (targetType == typeof(float)) { var v = element.GetSingle(); return Unsafe.As<float, T>(ref v); }
        if (targetType == typeof(double)) { var v = element.GetDouble(); return Unsafe.As<double, T>(ref v); }
        if (targetType == typeof(bool)) { var v = element.GetBoolean(); return Unsafe.As<bool, T>(ref v); }

        // For object (used by Materialize), box using schema column type
        if (targetType == typeof(object))
        {
            var col = Schema.FindByName(columnName);
            if (col is not null)
            {
                object boxed = DeserializeToType(element, col.Type.ClrType);
                return Unsafe.As<object, T>(ref boxed);
            }
            object str = element.ToString()!;
            return Unsafe.As<object, T>(ref str);
        }

        object fallback = element.ToString()!;
        return Unsafe.As<object, T>(ref fallback);
    }

    private static object DeserializeToType(JsonElement element, Type type)
    {
        if (type == typeof(string)) return element.ToString() ?? "";
        if (type == typeof(int)) return element.GetInt32();
        if (type == typeof(long)) return element.GetInt64();
        if (type == typeof(float)) return element.GetSingle();
        if (type == typeof(double)) return element.GetDouble();
        if (type == typeof(bool)) return element.GetBoolean();
        return element.ToString() ?? "";
    }
}
