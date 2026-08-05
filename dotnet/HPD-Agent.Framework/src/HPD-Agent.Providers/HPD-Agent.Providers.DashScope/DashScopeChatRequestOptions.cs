using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cnblogs.DashScope.Core;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// Serializable DashScope-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as model, temperature, top-p, top-k, max output tokens,
/// seed, stop sequences, tools, response format, and reasoning belong on
/// <see cref="ChatClientConfig"/> or <see cref="ChatOptions"/>.
/// </remarks>
public sealed class DashScopeChatRequestOptions : IChatRequestOptions
{
    /// <summary>
    /// Overrides whether the request uses DashScope multimodal generation endpoints.
    /// </summary>
    public bool? UseVl { get; set; }

    /// <summary>
    /// Enables DashScope web search for the request.
    /// </summary>
    public bool? EnableSearch { get; set; }

    /// <summary>
    /// Allows image tags to be included in search-augmented text output.
    /// </summary>
    public bool? EnableTextImageMixed { get; set; }

    /// <summary>
    /// DashScope web-search options. <see cref="EnableSearch"/> should also be enabled.
    /// </summary>
    public DashScopeSearchRequestOptions? SearchOptions { get; set; }

    /// <summary>
    /// Exact maximum length of thinking content for supported models.
    /// Use generic reasoning effort for normal reasoning control.
    /// </summary>
    public int? ThinkingBudget { get; set; }

    /// <summary>
    /// Makes previous reasoning content in chat history visible to the model.
    /// </summary>
    public bool? PreserveThinking { get; set; }

    /// <summary>
    /// Enables DashScope's internal code interpreter. This cannot be combined with normal tools.
    /// </summary>
    public bool? EnableCodeInterpreter { get; set; }

    /// <summary>
    /// Requests output token log probabilities.
    /// </summary>
    public bool? Logprobs { get; set; }

    /// <summary>
    /// Number of most likely token alternatives to return with log probabilities.
    /// </summary>
    public int? TopLogprobs { get; set; }

    /// <summary>
    /// Number of choices the model should generate.
    /// </summary>
    public int? N { get; set; }

    /// <summary>
    /// Token id bias map. Values near -100 ban a token; values near 100 force selection.
    /// </summary>
    public Dictionary<string, int>? LogitBias { get; set; }

    /// <summary>
    /// Translation options for Qwen-MT models.
    /// </summary>
    public DashScopeTranslationRequestOptions? TranslationOptions { get; set; }

    /// <summary>
    /// Cache-control options for supported DashScope models such as qwen-coder.
    /// </summary>
    public DashScopeCacheControlRequestOptions? CacheControl { get; set; }

    /// <summary>
    /// Allows higher-resolution image inputs for multimodal models.
    /// </summary>
    public bool? VlHighResolutionImages { get; set; }

    /// <summary>
    /// Negative prompt for supported multimodal/image-generation requests.
    /// </summary>
    public string? NegativePrompt { get; set; }

    /// <summary>
    /// Converts the typed options to additional properties consumed by the DashScope configured client.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        var properties = new Dictionary<string, object>();

        Add(properties, DashScopeChatRequestOptionKeys.UseVl, UseVl);
        Add(properties, DashScopeChatRequestOptionKeys.EnableSearch, EnableSearch);
        Add(properties, DashScopeChatRequestOptionKeys.EnableTextImageMixed, EnableTextImageMixed);
        Add(properties, DashScopeChatRequestOptionKeys.SearchOptions, SearchOptions);
        Add(properties, DashScopeChatRequestOptionKeys.ThinkingBudget, ThinkingBudget);
        Add(properties, DashScopeChatRequestOptionKeys.PreserveThinking, PreserveThinking);
        Add(properties, DashScopeChatRequestOptionKeys.EnableCodeInterpreter, EnableCodeInterpreter);
        Add(properties, DashScopeChatRequestOptionKeys.Logprobs, Logprobs);
        Add(properties, DashScopeChatRequestOptionKeys.TopLogprobs, TopLogprobs);
        Add(properties, DashScopeChatRequestOptionKeys.N, N);
        Add(properties, DashScopeChatRequestOptionKeys.LogitBias, LogitBias);
        Add(properties, DashScopeChatRequestOptionKeys.TranslationOptions, TranslationOptions);
        Add(properties, DashScopeChatRequestOptionKeys.CacheControl, CacheControl);
        Add(properties, DashScopeChatRequestOptionKeys.VlHighResolutionImages, VlHighResolutionImages);
        Add(properties, DashScopeChatRequestOptionKeys.NegativePrompt, NegativePrompt);

        return properties;
    }

    /// <summary>
    /// Applies these options to a serializable HPD chat run configuration.
    /// </summary>
    public void ApplyTo(ChatClientConfig chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        chat.ProviderOptions = this;
    }

    /// <summary>
    /// Applies these options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public void ApplyTo(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var properties = ToAdditionalProperties();
        if (properties.Count == 0)
            return;

        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        foreach (var property in properties)
        {
            options.AdditionalProperties[property.Key] = property.Value;
        }
    }

    private static void Add<T>(Dictionary<string, object> properties, string key, T? value)
        where T : struct
    {
        if (value.HasValue)
            properties[key] = value.Value;
    }

    private static void Add(Dictionary<string, object> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            properties[key] = value;
    }

    private static void Add<T>(Dictionary<string, object> properties, string key, T? value)
        where T : class
    {
        if (value is not null)
            properties[key] = value;
    }
}

/// <summary>
/// Serializable DashScope web-search request options.
/// </summary>
public sealed class DashScopeSearchRequestOptions
{
    /// <summary>
    /// Includes search source information in the response.
    /// </summary>
    public bool? EnableSource { get; set; }

    /// <summary>
    /// Includes citations in generated output.
    /// </summary>
    public bool? EnableCitation { get; set; }

    /// <summary>
    /// Citation format. DashScope defaults to a numbered bracket format.
    /// </summary>
    public string? CitationFormat { get; set; }

    /// <summary>
    /// Forces the model to use web search.
    /// </summary>
    public bool? ForcedSearch { get; set; }

    /// <summary>
    /// Search strategy, such as <c>turbo</c> or <c>max</c>.
    /// </summary>
    public DashScopeSearchStrategy? SearchStrategy { get; set; }

    /// <summary>
    /// Enables enhanced search for supported areas.
    /// </summary>
    public bool? EnableSearchExtension { get; set; }

    /// <summary>
    /// Returns search results first when incremental output is enabled.
    /// </summary>
    public bool? PrependSearchResult { get; set; }

    /// <summary>
    /// Limits search content to a recent window in days, such as 7, 30, 180, or 365.
    /// </summary>
    public int? Freshness { get; set; }

    /// <summary>
    /// Restricts search to the specified sites.
    /// </summary>
    public List<string>? AssignedSiteList { get; set; }
}

/// <summary>
/// Serializable DashScope Qwen-MT translation options.
/// </summary>
public sealed class DashScopeTranslationRequestOptions
{
    /// <summary>
    /// Input language name. Use <c>auto</c> to enable automatic detection.
    /// </summary>
    public string? SourceLang { get; set; }

    /// <summary>
    /// Output language name.
    /// </summary>
    public string? TargetLang { get; set; }

    /// <summary>
    /// Domain information about the source text. DashScope currently supports English domain names.
    /// </summary>
    public string? Domains { get; set; }
}

/// <summary>
/// Serializable DashScope cache-control options.
/// </summary>
public sealed class DashScopeCacheControlRequestOptions
{
    /// <summary>
    /// Cache type. DashScope defaults to <c>ephemeral</c>.
    /// </summary>
    public DashScopeCacheControlType? Type { get; set; }
}

/// <summary>
/// DashScope web-search strategy.
/// </summary>
[JsonConverter(typeof(DashScopeSearchStrategyJsonConverter))]
public enum DashScopeSearchStrategy
{
    Turbo,
    Max
}

/// <summary>
/// DashScope cache-control type.
/// </summary>
[JsonConverter(typeof(DashScopeCacheControlTypeJsonConverter))]
public enum DashScopeCacheControlType
{
    Ephemeral
}

public static class DashScopeChatRequestOptionExtensions
{
    public static ChatClientConfig UseDashScopeChatRequestOptions(
        this ChatClientConfig chat,
        DashScopeChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(options);

        options.ApplyTo(chat);
        return chat;
    }

    public static ChatOptions UseDashScopeChatRequestOptions(
        this ChatOptions chat,
        DashScopeChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(options);

        options.ApplyTo(chat);
        return chat;
    }
}

internal static class DashScopeChatRequestOptionKeys
{
    public const string UseVl = "useVl";
    public const string EnableSearch = "enable_search";
    public const string EnableTextImageMixed = "enable_text_image_mixed";
    public const string SearchOptions = "search_options";
    public const string ThinkingBudget = "thinking_budget";
    public const string PreserveThinking = "preserve_thinking";
    public const string EnableCodeInterpreter = "enable_code_interpreter";
    public const string Logprobs = "logprobs";
    public const string TopLogprobs = "top_logprobs";
    public const string N = "n";
    public const string LogitBias = "logit_bias";
    public const string TranslationOptions = "translation_options";
    public const string CacheControl = "cache_control";
    public const string VlHighResolutionImages = "vl_high_resolution_images";
    public const string NegativePrompt = "negative_prompt";
    public const string Raw = "raw";

    public static bool HasKnownOptions(ChatOptions options)
        => options.AdditionalProperties?.Keys.Any(IsKnown) == true;

    public static bool IsKnown(string key)
        => key is UseVl or EnableSearch or EnableTextImageMixed or SearchOptions or ThinkingBudget or
            PreserveThinking or EnableCodeInterpreter or Logprobs or TopLogprobs or N or LogitBias or
            TranslationOptions or CacheControl or VlHighResolutionImages or NegativePrompt;

    public static void ApplyRawParameters(ChatOptions options, string defaultModelId, bool? defaultUseVl)
    {
        if (options.AdditionalProperties is null ||
            options.AdditionalProperties.ContainsKey(Raw) ||
            !HasKnownOptions(options))
        {
            return;
        }

        var useVl = GetBoolean(options, UseVl) ?? InferUseVl(options.ModelId ?? defaultModelId, defaultUseVl);
        options.AdditionalProperties[UseVl] = useVl;
        options.AdditionalProperties[Raw] = useVl
            ? BuildMultimodalParameters(options)
            : BuildTextGenerationParameters(options);
    }

    private static TextGenerationParameters BuildTextGenerationParameters(ChatOptions options)
    {
        var parameters = new TextGenerationParameters
        {
            ResultFormat = "message",
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            RepetitionPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            MaxTokens = options.MaxOutputTokens,
            Stop = options.StopSequences is null ? null : new TextGenerationStop(options.StopSequences),
            Seed = (ulong?)options.Seed,
            ResponseFormat = ToDashScopeResponseFormat(options.ResponseFormat),
            EnableThinking = ToEnableThinking(options),
            EnableSearch = GetBoolean(options, EnableSearch),
            EnableTextImageMixed = GetBoolean(options, EnableTextImageMixed),
            SearchOptions = GetSearchOptions(options),
            ThinkingBudget = GetInt32(options, ThinkingBudget),
            PreserveThinking = GetBoolean(options, PreserveThinking),
            EnableCodeInterpreter = GetBoolean(options, EnableCodeInterpreter),
            Logprobs = GetBoolean(options, Logprobs),
            TopLogprobs = GetInt32(options, TopLogprobs),
            N = GetInt32(options, N),
            LogitBias = GetStringIntDictionary(options, LogitBias),
            TranslationOptions = GetTranslationOptions(options),
            CacheControl = GetCacheControlOptions(options)
        };

        return parameters;
    }

    private static MultimodalParameters BuildMultimodalParameters(ChatOptions options)
    {
        var parameters = new MultimodalParameters
        {
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            RepetitionPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            MaxTokens = options.MaxOutputTokens,
            Stop = options.StopSequences is null ? null : new TextGenerationStop(options.StopSequences),
            Seed = (ulong?)options.Seed,
            ResponseFormat = ToDashScopeResponseFormat(options.ResponseFormat),
            EnableThinking = ToEnableThinking(options),
            EnableSearch = GetBoolean(options, EnableSearch),
            EnableTextImageMixed = GetBoolean(options, EnableTextImageMixed),
            SearchOptions = GetSearchOptions(options),
            ThinkingBudget = GetInt32(options, ThinkingBudget),
            PreserveThinking = GetBoolean(options, PreserveThinking),
            EnableCodeInterpreter = GetBoolean(options, EnableCodeInterpreter),
            VlHighResolutionImages = GetBoolean(options, VlHighResolutionImages),
            NegativePrompt = GetString(options, NegativePrompt)
        };

        return parameters;
    }

    private static bool InferUseVl(string modelId, bool? defaultUseVl)
        => defaultUseVl
           ?? modelId.StartsWith("qwen-vl", StringComparison.OrdinalIgnoreCase)
           || modelId.StartsWith("qwen3-vl", StringComparison.OrdinalIgnoreCase)
           || modelId.StartsWith("qwen3.5", StringComparison.OrdinalIgnoreCase)
           || modelId.StartsWith("qwen3.6", StringComparison.OrdinalIgnoreCase)
           || modelId.StartsWith("qwen3.7", StringComparison.OrdinalIgnoreCase)
           || modelId.StartsWith("qwen3-omni", StringComparison.OrdinalIgnoreCase)
           || modelId.StartsWith("gui-plus", StringComparison.OrdinalIgnoreCase);

    private static DashScopeResponseFormat? ToDashScopeResponseFormat(ChatResponseFormat? format)
        => format switch
        {
            null => null,
            ChatResponseFormatJson => DashScopeResponseFormat.Json,
            ChatResponseFormatText => DashScopeResponseFormat.Text,
            _ => null
        };

    private static bool? ToEnableThinking(ChatOptions options)
        => options.Reasoning?.Effort switch
        {
            null => null,
            Microsoft.Extensions.AI.ReasoningEffort.None => false,
            _ => true
        };

    private static bool? GetBoolean(ChatOptions options, string key)
        => GetValue(options, key) switch
        {
            null => null,
            bool value => value,
            JsonElement json when json.ValueKind is JsonValueKind.True or JsonValueKind.False => json.GetBoolean(),
            JsonElement json when json.ValueKind == JsonValueKind.String && bool.TryParse(json.GetString(), out var value) => value,
            var value => Convert.ToBoolean(value)
        };

    private static int? GetInt32(ChatOptions options, string key)
        => GetValue(options, key) switch
        {
            null => null,
            int value => value,
            JsonElement json when json.ValueKind == JsonValueKind.Number => json.GetInt32(),
            JsonElement json when json.ValueKind == JsonValueKind.String && int.TryParse(json.GetString(), out var value) => value,
            var value => Convert.ToInt32(value)
        };

    private static string? GetString(ChatOptions options, string key)
        => GetValue(options, key) switch
        {
            null => null,
            string value => value,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString(),
            var value => value.ToString()
        };

    private static SearchOptions? GetSearchOptions(ChatOptions options)
    {
        var value = GetValue(options, SearchOptions);
        return value switch
        {
            null => null,
            SearchOptions sdkOptions => sdkOptions,
            DashScopeSearchRequestOptions typed => ToSdkSearchOptions(typed),
            JsonElement json => ToSdkSearchOptions(json.Deserialize(DashScopeJsonContext.Default.DashScopeSearchRequestOptions)),
            _ => null
        };
    }

    private static TextGenerationTranslationOptions? GetTranslationOptions(ChatOptions options)
    {
        var value = GetValue(options, TranslationOptions);
        return value switch
        {
            null => null,
            TextGenerationTranslationOptions sdkOptions => sdkOptions,
            DashScopeTranslationRequestOptions typed => ToSdkTranslationOptions(typed),
            JsonElement json => ToSdkTranslationOptions(json.Deserialize(DashScopeJsonContext.Default.DashScopeTranslationRequestOptions)),
            _ => null
        };
    }

    private static CacheControlOptions? GetCacheControlOptions(ChatOptions options)
    {
        var value = GetValue(options, CacheControl);
        return value switch
        {
            null => null,
            CacheControlOptions sdkOptions => sdkOptions,
            DashScopeCacheControlRequestOptions typed => ToSdkCacheControlOptions(typed),
            JsonElement json => ToSdkCacheControlOptions(json.Deserialize(DashScopeJsonContext.Default.DashScopeCacheControlRequestOptions)),
            _ => null
        };
    }

    private static Dictionary<string, int>? GetStringIntDictionary(ChatOptions options, string key)
    {
        var value = GetValue(options, key);
        return value switch
        {
            null => null,
            Dictionary<string, int> dictionary => dictionary,
            JsonElement json => json.Deserialize(DashScopeJsonContext.Default.DictionaryStringInt32),
            _ => null
        };
    }

    private static SearchOptions? ToSdkSearchOptions(DashScopeSearchRequestOptions? options)
        => options is null
            ? null
            : new SearchOptions
            {
                EnableSource = options.EnableSource,
                EnableCitation = options.EnableCitation,
                CitationFormat = options.CitationFormat,
                ForcedSearch = options.ForcedSearch,
                SearchStrategy = ToWireString(options.SearchStrategy),
                EnableSearchExtension = options.EnableSearchExtension,
                PrependSearchResult = options.PrependSearchResult,
                Freshness = options.Freshness,
                AssignedSiteList = options.AssignedSiteList
            };

    private static TextGenerationTranslationOptions? ToSdkTranslationOptions(DashScopeTranslationRequestOptions? options)
        => options is null
            ? null
            : new TextGenerationTranslationOptions
            {
                SourceLang = string.IsNullOrWhiteSpace(options.SourceLang) ? "auto" : options.SourceLang!,
                TargetLang = options.TargetLang ?? string.Empty,
                Domains = options.Domains
            };

    private static CacheControlOptions? ToSdkCacheControlOptions(DashScopeCacheControlRequestOptions? options)
        => options is null
            ? null
            : new CacheControlOptions
            {
                Type = ToWireString(options.Type) ?? "ephemeral"
            };

    private static object? GetValue(ChatOptions options, string key)
        => options.AdditionalProperties?.TryGetValue(key, out var value) == true ? value : null;

    private static string? ToWireString(DashScopeSearchStrategy? value)
        => value switch
        {
            DashScopeSearchStrategy.Turbo => "turbo",
            DashScopeSearchStrategy.Max => "max",
            _ => null
        };

    private static string? ToWireString(DashScopeCacheControlType? value)
        => value switch
        {
            DashScopeCacheControlType.Ephemeral => "ephemeral",
            _ => null
        };
}

internal sealed class DashScopeSearchStrategyJsonConverter : JsonConverter<DashScopeSearchStrategy>
{
    public override DashScopeSearchStrategy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "turbo" => DashScopeSearchStrategy.Turbo,
            "max" => DashScopeSearchStrategy.Max,
            var value => throw new JsonException($"Unknown DashScope search strategy '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, DashScopeSearchStrategy value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            DashScopeSearchStrategy.Turbo => "turbo",
            DashScopeSearchStrategy.Max => "max",
            _ => throw new JsonException($"Unknown DashScope search strategy '{value}'.")
        });
    }
}

internal sealed class DashScopeCacheControlTypeJsonConverter : JsonConverter<DashScopeCacheControlType>
{
    public override DashScopeCacheControlType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "ephemeral" => DashScopeCacheControlType.Ephemeral,
            var value => throw new JsonException($"Unknown DashScope cache-control type '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, DashScopeCacheControlType value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            DashScopeCacheControlType.Ephemeral => "ephemeral",
            _ => throw new JsonException($"Unknown DashScope cache-control type '{value}'.")
        });
    }
}
