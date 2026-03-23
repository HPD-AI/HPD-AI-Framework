namespace HPD.ML.DataSources;

using HPD.ML.Abstractions;

/// <summary>
/// Writes any IDataHandle to a CSV file.
/// </summary>
public static class CsvWriter
{
    public static void Write(IDataHandle data, string path, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        using var writer = new StreamWriter(path, false, options.Encoding);
        var columns = data.Schema.Columns;

        if (options.HasHeader)
            writer.WriteLine(string.Join(options.Separator,
                columns.Select(c => EscapeField(c.Name, options))));

        using var cursor = data.GetCursor(columns.Select(c => c.Name));
        while (cursor.MoveNext())
        {
            var row = cursor.Current;
            var fields = columns.Select(c =>
                EscapeField(row.GetValue<object>(c.Name)?.ToString() ?? "", options));
            writer.WriteLine(string.Join(options.Separator, fields));
        }
    }

    public static async Task WriteAsync(
        IDataHandle data, string path, CsvOptions? options = null, CancellationToken ct = default)
    {
        options ??= new CsvOptions();
        await using var writer = new StreamWriter(path, false, options.Encoding);
        var columns = data.Schema.Columns;

        if (options.HasHeader)
            await writer.WriteLineAsync(
                string.Join(options.Separator,
                    columns.Select(c => EscapeField(c.Name, options))));

        await foreach (var row in data.StreamRows(ct))
        {
            var fields = columns.Select(c =>
                EscapeField(row.GetValue<object>(c.Name)?.ToString() ?? "", options));
            await writer.WriteLineAsync(string.Join(options.Separator, fields));
        }
    }

    private static string EscapeField(string field, CsvOptions options)
    {
        if (field.Contains(options.Separator) || field.Contains(options.Quote) || field.Contains('\n'))
        {
            var escaped = field.Replace(
                options.Quote.ToString(),
                $"{options.Quote}{options.Quote}");
            return $"{options.Quote}{escaped}{options.Quote}";
        }
        return field;
    }
}
