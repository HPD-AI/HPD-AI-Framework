namespace HPD.ML.DataSources;

using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Lazy IDataHandle backed by a CSV file.
/// Schema is inferred on construction (scanning first N rows).
/// Data streams lazily via cursor — file is not loaded into memory.
/// </summary>
public sealed class CsvDataHandle : IDataHandle
{
    private readonly string _path;
    private readonly CsvOptions _options;
    private readonly Schema _schema;
    private readonly long? _rowCount;

    private CsvDataHandle(string path, CsvOptions options, Schema schema, long? rowCount)
    {
        _path = path;
        _options = options;
        _schema = schema;
        _rowCount = rowCount;
    }

    public ISchema Schema => _schema;
    public long? RowCount => _rowCount;
    public OrderingPolicy Ordering => OrderingPolicy.StrictlyOrdered;
    public MaterializationCapabilities Capabilities => MaterializationCapabilities.CursorOnly;

    /// <summary>Create with schema inference from the file.</summary>
    public static CsvDataHandle Create(string path, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        var (schema, rowCount) = InferSchema(path, options);
        return new CsvDataHandle(path, options, schema, rowCount);
    }

    /// <summary>Create with an explicit schema (no inference).</summary>
    public static CsvDataHandle Create(string path, Schema schema, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        return new CsvDataHandle(path, options, schema, null);
    }

    public IRowCursor GetCursor(IEnumerable<string> columnsNeeded)
        => new CsvCursor(_path, _schema, _options, columnsNeeded.ToHashSet());

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
        return false; // CSV is cursor-based; materialize first for batch access
    }

    // ── Schema Inference ───────────────────────────────────────────

    private static (Schema schema, long? rowCount) InferSchema(string path, CsvOptions options)
    {
        using var reader = new StreamReader(path, options.Encoding);
        string[] columnNames;

        var firstLine = reader.ReadLine()
            ?? throw new InvalidOperationException("CSV file is empty.");

        if (options.HasHeader)
        {
            columnNames = ParseLine(firstLine, options.Separator, options.Quote);
        }
        else
        {
            var fields = ParseLine(firstLine, options.Separator, options.Quote);
            columnNames = Enumerable.Range(0, fields.Length)
                .Select(i => $"Column{i}").ToArray();
        }

        // Infer types by scanning rows (null = not yet seen)
        var types = new Type?[columnNames.Length];
        int scanned = 0;
        int limit = options.InferenceScanRows;

        // If no header, the first line is data — include it in inference
        if (!options.HasHeader)
        {
            var fields = ParseLine(firstLine, options.Separator, options.Quote);
            InferRowTypes(fields, types, options);
            scanned++;
        }

        while (reader.ReadLine() is { } line && (limit == 0 || scanned < limit))
        {
            if (options.CommentPrefix.HasValue && line.Length > 0
                && line[0] == options.CommentPrefix.Value)
                continue;

            var fields = ParseLine(line, options.Separator, options.Quote);
            InferRowTypes(fields, types, options);
            scanned++;
        }

        // Apply type hints; default unresolved to string
        var builder = new SchemaBuilder();
        for (int i = 0; i < columnNames.Length; i++)
        {
            var type = options.TypeHints?.TryGetValue(columnNames[i], out var hinted) == true
                ? hinted : types[i] ?? typeof(string);
            builder.AddColumn(columnNames[i], new FieldType(type));
        }

        return (builder.Build(), null);
    }

    private static void InferRowTypes(string[] fields, Type?[] types, CsvOptions options)
    {
        for (int i = 0; i < Math.Min(fields.Length, types.Length); i++)
        {
            var field = fields[i].Trim();
            if (string.IsNullOrWhiteSpace(field)) continue;

            var inferred = InferFieldType(field);
            types[i] = types[i] is null ? inferred : WidenType(types[i]!, inferred);
        }
    }

    private static Type InferFieldType(string value)
    {
        if (int.TryParse(value, out _)) return typeof(int);
        if (long.TryParse(value, out _)) return typeof(long);
        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _)) return typeof(double);
        if (bool.TryParse(value, out _)) return typeof(bool);
        return typeof(string);
    }

    private static Type WidenType(Type existing, Type incoming)
    {
        if (existing == incoming) return existing;
        // string is the widest type — any conflict with string stays string
        if (existing == typeof(string) || incoming == typeof(string)) return typeof(string);
        // numeric widening
        if (existing == typeof(int) && incoming == typeof(long)) return typeof(long);
        if (existing == typeof(long) && incoming == typeof(int)) return typeof(long);
        if ((existing == typeof(int) || existing == typeof(long)) && incoming == typeof(double)) return typeof(double);
        if (existing == typeof(double) && (incoming == typeof(int) || incoming == typeof(long))) return typeof(double);
        return typeof(string); // fallback
    }

    /// <summary>Parse a single CSV line respecting quotes.</summary>
    internal static string[] ParseLine(string line, char separator, char quote)
    {
        var fields = new List<string>();
        int i = 0;

        while (i < line.Length)
        {
            if (line[i] == quote)
            {
                // Quoted field
                i++; // skip opening quote
                var field = new System.Text.StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == quote)
                    {
                        if (i + 1 < line.Length && line[i + 1] == quote)
                        {
                            field.Append(quote);
                            i += 2;
                        }
                        else
                        {
                            i++; // skip closing quote
                            break;
                        }
                    }
                    else
                    {
                        field.Append(line[i]);
                        i++;
                    }
                }
                fields.Add(field.ToString());
                if (i < line.Length && line[i] == separator) i++;
            }
            else
            {
                // Unquoted field
                int start = i;
                while (i < line.Length && line[i] != separator) i++;
                fields.Add(line[start..i]);
                if (i < line.Length) i++; // skip separator
            }
        }

        // Trailing separator means one more empty field
        if (line.Length > 0 && line[^1] == separator)
            fields.Add("");

        return fields.ToArray();
    }
}
