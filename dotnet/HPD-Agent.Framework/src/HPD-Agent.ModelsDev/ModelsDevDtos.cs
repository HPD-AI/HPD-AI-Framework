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
    public required decimal Input { get; init; }

    [JsonPropertyName("output")]
    public required decimal Output { get; init; }

    [JsonPropertyName("reasoning")]
    public decimal? Reasoning { get; init; }

    [JsonPropertyName("cache_read")]
    public decimal? CacheRead { get; init; }

    [JsonPropertyName("cache_write")]
    public decimal? CacheWrite { get; init; }

    [JsonPropertyName("input_audio")]
    public decimal? InputAudio { get; init; }

    [JsonPropertyName("output_audio")]
    public decimal? OutputAudio { get; init; }

    [JsonPropertyName("tiers")]
    public IReadOnlyList<ModelsDevCostTier> Tiers { get; init; } = [];
}

public sealed class ModelsDevCostTier
{
    [JsonPropertyName("tier")]
    public required ModelsDevCostTierSelector Tier { get; init; }

    [JsonPropertyName("input")]
    public required decimal Input { get; init; }

    [JsonPropertyName("output")]
    public required decimal Output { get; init; }

    [JsonPropertyName("reasoning")]
    public decimal? Reasoning { get; init; }

    [JsonPropertyName("cache_read")]
    public decimal? CacheRead { get; init; }

    [JsonPropertyName("cache_write")]
    public decimal? CacheWrite { get; init; }

    [JsonPropertyName("input_audio")]
    public decimal? InputAudio { get; init; }

    [JsonPropertyName("output_audio")]
    public decimal? OutputAudio { get; init; }
}

public sealed class ModelsDevCostTierSelector
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }
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

internal sealed class ModelsDevCachedData
{
    [JsonPropertyName("payload")]
    public required string Payload { get; init; }

    [JsonPropertyName("retrieved_at")]
    public required DateTimeOffset RetrievedAt { get; init; }

    [JsonPropertyName("etag")]
    public string? ETag { get; init; }

    [JsonPropertyName("content_digest")]
    public required string ContentDigest { get; init; }

    [JsonPropertyName("source")]
    public required Uri Source { get; init; }
}
