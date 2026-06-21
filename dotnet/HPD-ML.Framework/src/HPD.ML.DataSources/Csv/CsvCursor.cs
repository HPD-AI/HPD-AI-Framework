namespace HPD.ML.DataSources;

using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Streaming cursor over a CSV file. Reads one line at a time.
/// Parses only active columns for efficiency.
/// </summary>
internal sealed class CsvCursor : IRowCursor
{
    private readonly StreamReader _reader;
    private readonly Schema _schema;
    private readonly CsvOptions _options;
    private readonly HashSet<string> _activeColumns;
    private CsvRow? _current;
    private int _rowsSkipped;
    private int _rowsRead;

    public CsvCursor(string path, Schema schema, CsvOptions options, HashSet<string> activeColumns)
    {
        _reader = new StreamReader(path, options.Encoding);
        _schema = schema;
        _options = options;
        _activeColumns = activeColumns;

        // Skip header
        if (options.HasHeader)
            _reader.ReadLine();
    }

    public IRow Current => _current
        ?? throw new InvalidOperationException("Cursor not positioned. Call MoveNext().");

    public bool MoveNext()
    {
        while (true)
        {
            var line = _reader.ReadLine();
            if (line is null) return false;

            // Skip comments
            if (_options.CommentPrefix.HasValue && line.Length > 0
                && line[0] == _options.CommentPrefix.Value)
                continue;

            // Skip rows
            if (_rowsSkipped < _options.SkipRows)
            {
                _rowsSkipped++;
                continue;
            }

            // Max rows
            if (_options.MaxRows.HasValue && _rowsRead >= _options.MaxRows.Value)
                return false;

            var fields = CsvDataHandle.ParseLine(line, _options.Separator, _options.Quote);
            _current = new CsvRow(_schema, fields, _options);
            _rowsRead++;
            return true;
        }
    }

    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// A single CSV row. Parses field values on demand with type coercion.
/// </summary>
internal sealed class CsvRow : IRow
{
    private readonly string[] _fields;
    private readonly CsvOptions _options;

    public CsvRow(ISchema schema, string[] fields, CsvOptions options)
    {
        Schema = schema;
        _fields = fields;
        _options = options;
    }

    public ISchema Schema { get; }

    public T GetValue<T>(string columnName) where T : allows ref struct
    {
        int index = GetColumnIndex(columnName);
        if (index < 0)
            throw new KeyNotFoundException($"Column '{columnName}' not found.");
        // Field beyond row length = missing value
        if (index >= _fields.Length)
            return HandleMissing<T>(columnName);
        return CoerceValue<T>(_fields[index], columnName);
    }

    public bool TryGetValue<T>(string columnName, out T value) where T : allows ref struct
    {
        int index = GetColumnIndex(columnName);
        if (index < 0)
        {
            value = default!;
            return false;
        }
        if (index >= _fields.Length)
        {
            value = HandleMissing<T>(columnName);
            return true;
        }

        try
        {
            value = CoerceValue<T>(_fields[index], columnName);
            return true;
        }
        catch
        {
            value = default!;
            return false;
        }
    }

    private int GetColumnIndex(string name)
    {
        for (int i = 0; i < Schema.Columns.Count; i++)
        {
            if (Schema.Columns[i].Name == name) return i;
        }
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private T CoerceValue<T>(string raw, string columnName) where T : allows ref struct
    {
        if (string.IsNullOrWhiteSpace(raw))
            return HandleMissing<T>(columnName);

        var targetType = typeof(T);
        raw = raw.Trim();

        // Fast paths for common types
        if (targetType == typeof(string)) return Unsafe.As<string, T>(ref raw);
        if (targetType == typeof(float)) { var v = float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture); return Unsafe.As<float, T>(ref v); }
        if (targetType == typeof(double)) { var v = double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture); return Unsafe.As<double, T>(ref v); }
        if (targetType == typeof(int)) { var v = int.Parse(raw); return Unsafe.As<int, T>(ref v); }
        if (targetType == typeof(long)) { var v = long.Parse(raw); return Unsafe.As<long, T>(ref v); }
        if (targetType == typeof(bool)) { var v = bool.Parse(raw); return Unsafe.As<bool, T>(ref v); }

        // For object (used by Materialize), box the value using the schema's column type
        if (targetType == typeof(object))
        {
            var col = Schema.FindByName(columnName);
            if (col is not null)
            {
                object boxed = CoerceToType(raw, col.Type.ClrType);
                return Unsafe.As<object, T>(ref boxed);
            }
            object str = raw;
            return Unsafe.As<object, T>(ref str);
        }

        var converted = Convert.ChangeType(raw, targetType);
        return Unsafe.As<object, T>(ref converted);
    }

    private static object CoerceToType(string raw, Type type)
    {
        if (type == typeof(string)) return raw;
        if (type == typeof(int)) return int.Parse(raw);
        if (type == typeof(long)) return long.Parse(raw);
        if (type == typeof(float)) return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        if (type == typeof(double)) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        if (type == typeof(bool)) return bool.Parse(raw);
        return Convert.ChangeType(raw, type);
    }

    private T HandleMissing<T>(string columnName) where T : allows ref struct
    {
        if (_options.MissingValuePolicy == MissingValuePolicy.Error)
            throw new InvalidOperationException($"Missing value in column '{columnName}'.");

        if (_options.MissingValuePolicy == MissingValuePolicy.NaN)
        {
            if (typeof(T) == typeof(float)) { var v = float.NaN; return Unsafe.As<float, T>(ref v); }
            if (typeof(T) == typeof(double)) { var v = double.NaN; return Unsafe.As<double, T>(ref v); }

            // When T is object (Materialize path), check schema column type
            if (typeof(T) == typeof(object))
            {
                var col = Schema.FindByName(columnName);
                if (col?.Type.ClrType == typeof(float)) { object v = float.NaN; return Unsafe.As<object, T>(ref v); }
                if (col?.Type.ClrType == typeof(double)) { object v = double.NaN; return Unsafe.As<object, T>(ref v); }
            }
        }

        // For object type with value-type schema, return typed default
        if (typeof(T) == typeof(object))
        {
            var col = Schema.FindByName(columnName);
            if (col?.Type.ClrType.IsValueType == true)
            {
                object v = Activator.CreateInstance(col.Type.ClrType)!;
                return Unsafe.As<object, T>(ref v);
            }
        }

        return default!;
    }
}
