using System.Text.Json.Serialization;

namespace HPD.Agent.ModelsDev;

public sealed class ModelsDevDatabase
{
    [JsonPropertyName("providers")]
    public Dictionary<string, ModelsDevProvider> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelsDevProvider
{
    [JsonPropertyName("models")]
    public Dictionary<string, ModelsDevModel> Models { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelsDevModel
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("family")]
    public string? Family { get; init; }

    [JsonPropertyName("cost")]
    public ModelsDevCost? Cost { get; init; }

    [JsonPropertyName("limit")]
    public ModelsDevLimit? Limit { get; init; }

    [JsonPropertyName("modalities")]
    public ModelsDevModalities? Modalities { get; init; }

    [JsonPropertyName("reasoning")]
    public bool Reasoning { get; init; }

    [JsonPropertyName("tool_call")]
    public bool ToolCall { get; init; }

    [JsonPropertyName("temperature")]
    public bool Temperature { get; init; }

    [JsonPropertyName("attachment")]
    public bool Attachment { get; init; }

    [JsonPropertyName("open_weights")]
    public bool OpenWeights { get; init; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed class ModelsDevCost
{
    [JsonPropertyName("input")]
    public decimal? Input { get; init; }

    [JsonPropertyName("output")]
    public decimal? Output { get; init; }

    [JsonPropertyName("cache_read")]
    public decimal? CacheRead { get; init; }

    [JsonPropertyName("cache_write")]
    public decimal? CacheWrite { get; init; }
}

public sealed class ModelsDevLimit
{
    [JsonPropertyName("context")]
    public int? Context { get; init; }

    [JsonPropertyName("input")]
    public int? Input { get; init; }

    [JsonPropertyName("output")]
    public int? Output { get; init; }
}

public sealed class ModelsDevModalities
{
    [JsonPropertyName("input")]
    public IReadOnlyList<string> Input { get; init; } = [];

    [JsonPropertyName("output")]
    public IReadOnlyList<string> Output { get; init; } = [];
}

public sealed class ModelsDevCachedData
{
    [JsonPropertyName("database")]
    public ModelsDevDatabase Database { get; init; } = new();

    [JsonPropertyName("lastRefresh")]
    public DateTimeOffset LastRefresh { get; init; }

    [JsonPropertyName("etag")]
    public string? ETag { get; init; }
}
