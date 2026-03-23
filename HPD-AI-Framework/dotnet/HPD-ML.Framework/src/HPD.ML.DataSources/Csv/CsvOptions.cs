namespace HPD.ML.DataSources;

/// <summary>
/// Configuration for CSV loading.
/// </summary>
public sealed record CsvOptions
{
    /// <summary>Column delimiter. Default: ','.</summary>
    public char Separator { get; init; } = ',';

    /// <summary>Whether the first row contains column names. Default: true.</summary>
    public bool HasHeader { get; init; } = true;

    /// <summary>Quote character for escaping. Default: '"'.</summary>
    public char Quote { get; init; } = '"';

    /// <summary>Comment prefix. Lines starting with this are skipped. Default: null.</summary>
    public char? CommentPrefix { get; init; }

    /// <summary>File encoding. Default: UTF-8.</summary>
    public System.Text.Encoding Encoding { get; init; } = System.Text.Encoding.UTF8;

    /// <summary>
    /// Explicit type hints per column name. Overrides inference.
    /// </summary>
    public IReadOnlyDictionary<string, Type>? TypeHints { get; init; }

    /// <summary>
    /// Number of rows to scan for type inference. Default: 100.
    /// Set to 0 to scan all rows.
    /// </summary>
    public int InferenceScanRows { get; init; } = 100;

    /// <summary>
    /// How to handle missing/empty values.
    /// </summary>
    public MissingValuePolicy MissingValuePolicy { get; init; } = MissingValuePolicy.DefaultValue;

    /// <summary>Number of rows to skip from the start (after header). Default: 0.</summary>
    public int SkipRows { get; init; }

    /// <summary>Maximum rows to read. Default: null (all rows).</summary>
    public int? MaxRows { get; init; }
}

public enum MissingValuePolicy
{
    /// <summary>Use default(T) for value types, null for nullable/string.</summary>
    DefaultValue,
    /// <summary>Treat missing values as NaN for floating-point columns.</summary>
    NaN,
    /// <summary>Throw on missing values.</summary>
    Error
}
