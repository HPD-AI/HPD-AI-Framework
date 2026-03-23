namespace HPD.ML.DataSources;

/// <summary>
/// Configuration for JSON/JSONL loading.
/// </summary>
public sealed record JsonOptions
{
    /// <summary>
    /// True = one JSON object per line (JSONL). False = single JSON array.
    /// Default: auto-detect from first character.
    /// </summary>
    public bool? IsJsonLines { get; init; }

    /// <summary>
    /// JSON property name mapping to column names.
    /// Example: { ["user.name"] = "UserName" } flattens nested objects.
    /// </summary>
    public IReadOnlyDictionary<string, string>? PropertyMapping { get; init; }

    /// <summary>
    /// Explicit type hints per column name. Overrides inference.
    /// </summary>
    public IReadOnlyDictionary<string, Type>? TypeHints { get; init; }

    /// <summary>Maximum rows to scan for schema inference. Default: 100.</summary>
    public int InferenceScanRows { get; init; } = 100;

    /// <summary>Maximum depth to flatten nested objects. Default: 1 (top-level only).</summary>
    public int MaxFlattenDepth { get; init; } = 1;

    /// <summary>File encoding. Default: UTF-8.</summary>
    public System.Text.Encoding Encoding { get; init; } = System.Text.Encoding.UTF8;
}
