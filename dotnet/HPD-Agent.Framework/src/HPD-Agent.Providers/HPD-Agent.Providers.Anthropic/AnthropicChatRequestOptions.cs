using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Anthropic;

/// <summary>
/// Serializable Anthropic-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as model, temperature, top-p, max output tokens,
/// stop sequences, tools, response format, and reasoning belong on
/// <see cref="ChatRunConfig"/> or <see cref="ChatOptions"/>.
/// </remarks>
public sealed class AnthropicChatRequestOptions
{
    /// <summary>
    /// Anthropic request service tier.
    /// </summary>
    public AnthropicServiceTier? ServiceTier { get; set; }

    /// <summary>
    /// Exact extended-thinking token budget. Use generic reasoning for normal reasoning control.
    /// </summary>
    public long? ThinkingBudgetTokens { get; set; }

    /// <summary>
    /// Anthropic-specific thinking display mode used with <see cref="ThinkingBudgetTokens"/>.
    /// </summary>
    public AnthropicThinkingDisplay? ThinkingDisplay { get; set; }

    /// <summary>
    /// Anthropic prompt-cache policy for request content blocks.
    /// </summary>
    public AnthropicCacheControlConfig? CacheControl { get; set; }

    /// <summary>
    /// Converts the typed options to additional properties consumed by the Anthropic configured client.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        Validate();

        var properties = new Dictionary<string, object>();

        Add(properties, AnthropicChatRequestOptionKeys.ServiceTier, ServiceTier);
        Add(properties, AnthropicChatRequestOptionKeys.ThinkingBudgetTokens, ThinkingBudgetTokens);
        Add(properties, AnthropicChatRequestOptionKeys.ThinkingDisplay, ThinkingDisplay);
        Add(properties, AnthropicChatRequestOptionKeys.CacheControl, CacheControl);

        return properties;
    }

    /// <summary>
    /// Applies these options to a serializable HPD chat run configuration.
    /// </summary>
    public void ApplyTo(ChatRunConfig chat)
    {
        ArgumentNullException.ThrowIfNull(chat);

        var properties = ToAdditionalProperties();
        if (properties.Count == 0)
            return;

        chat.AdditionalProperties ??= new Dictionary<string, object>();
        foreach (var property in properties)
        {
            chat.AdditionalProperties[property.Key] = property.Value;
        }
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

    private void Validate()
    {
        if (ThinkingBudgetTokens is < 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ThinkingBudgetTokens),
                ThinkingBudgetTokens,
                "ThinkingBudgetTokens must be at least 1024.");
        }
    }

    private static void Add<T>(Dictionary<string, object> properties, string key, T? value)
        where T : struct
    {
        if (value.HasValue)
            properties[key] = value.Value;
    }

    private static void Add<T>(Dictionary<string, object> properties, string key, T? value)
        where T : class
    {
        if (value is not null)
            properties[key] = value;
    }
}

/// <summary>
/// Extension helpers for applying Anthropic-specific chat request options.
/// </summary>
public static class AnthropicChatRequestOptionExtensions
{
    /// <summary>
    /// Applies Anthropic-specific runtime options to a serializable HPD chat run configuration.
    /// </summary>
    public static ChatRunConfig UseAnthropicChatRequestOptions(
        this ChatRunConfig chat,
        AnthropicChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Anthropic-specific runtime options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseAnthropicChatRequestOptions(
        this ChatOptions chat,
        AnthropicChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }
}

/// <summary>
/// Anthropic service tier values for request routing.
/// </summary>
[JsonConverter(typeof(AnthropicServiceTierJsonConverter))]
public enum AnthropicServiceTier
{
    /// <summary>
    /// Let Anthropic choose the service tier automatically.
    /// </summary>
    Auto,

    /// <summary>
    /// Use only the standard service tier.
    /// </summary>
    StandardOnly
}

/// <summary>
/// Anthropic extended-thinking display mode.
/// </summary>
[JsonConverter(typeof(AnthropicThinkingDisplayJsonConverter))]
public enum AnthropicThinkingDisplay
{
    /// <summary>
    /// Return summarized thinking content when the model supports it.
    /// </summary>
    Summarized,

    /// <summary>
    /// Omit thinking content from the response.
    /// </summary>
    Omitted
}

/// <summary>
/// Anthropic prompt-cache policy for selected message text blocks.
/// </summary>
public sealed class AnthropicCacheControlConfig
{
    /// <summary>
    /// Cache system message text blocks.
    /// </summary>
    public AnthropicCacheTtl? SystemMessages { get; set; }

    /// <summary>
    /// Cache text blocks on the final user message in the request.
    /// </summary>
    public AnthropicCacheTtl? LastUserMessage { get; set; }
}

/// <summary>
/// Anthropic prompt-cache time-to-live values.
/// </summary>
[JsonConverter(typeof(AnthropicCacheTtlJsonConverter))]
public enum AnthropicCacheTtl
{
    /// <summary>
    /// Cache for five minutes.
    /// </summary>
    FiveMinutes,

    /// <summary>
    /// Cache for one hour.
    /// </summary>
    OneHour
}

internal static class AnthropicChatRequestOptionKeys
{
    public const string ServiceTier = "serviceTier";
    public const string ThinkingBudgetTokens = "thinkingBudgetTokens";
    public const string ThinkingDisplay = "thinkingDisplay";
    public const string CacheControl = "cacheControl";

    public static bool HasRequestOptions(ChatOptions options)
        => TryGetServiceTier(options, out _) ||
           TryGetThinkingBudgetTokens(options, out _) ||
           TryGetThinkingDisplay(options, out _) ||
           TryGetCacheControl(options, out _);

    public static AnthropicCacheControlConfig? GetCacheControl(ChatOptions options)
        => TryGetCacheControl(options, out var cacheControl) ? cacheControl : null;

    public static void ApplyRawRequestOptions(
        ChatOptions options,
        string defaultModelId,
        int defaultMaxTokens)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!HasRequestOptions(options))
            return;

        var previousFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var createParams = previousFactory?.Invoke(client) as MessageCreateParams
                ?? new MessageCreateParams
                {
                    MaxTokens = options.MaxOutputTokens ?? defaultMaxTokens,
                    Model = options.ModelId ?? defaultModelId,
                    Messages = []
                };

            if (createParams.ServiceTier is null &&
                TryGetServiceTier(options, out var serviceTier))
            {
                createParams = createParams with { ServiceTier = ToAnthropicServiceTier(serviceTier) };
            }

            if (createParams.Thinking is null &&
                TryGetThinkingBudgetTokens(options, out var budgetTokens))
            {
                if (budgetTokens < 1024)
                    throw new ArgumentOutOfRangeException(ThinkingBudgetTokens, budgetTokens, "ThinkingBudgetTokens must be at least 1024.");

                var maxTokens = createParams.MaxTokens;
                if (maxTokens <= budgetTokens)
                {
                    maxTokens = budgetTokens + defaultMaxTokens;
                    createParams = createParams with { MaxTokens = maxTokens };
                }

                var enabled = new ThinkingConfigEnabled(budgetTokens);
                if (TryGetThinkingDisplay(options, out var display))
                {
                    enabled = enabled with { Display = ToAnthropicThinkingDisplay(display) };
                }

                createParams = createParams with
                {
                    Thinking = new ThinkingConfigParam(enabled)
                };
            }

            return createParams;
        };
    }

    private static bool TryGetServiceTier(ChatOptions options, out AnthropicServiceTier value)
        => TryGetEnum(options.AdditionalProperties, ServiceTier, out value);

    private static bool TryGetThinkingBudgetTokens(ChatOptions options, out long value)
        => TryGetInt64(options.AdditionalProperties, ThinkingBudgetTokens, out value);

    private static bool TryGetThinkingDisplay(ChatOptions options, out AnthropicThinkingDisplay value)
        => TryGetEnum(options.AdditionalProperties, ThinkingDisplay, out value);

    private static bool TryGetCacheControl(ChatOptions options, out AnthropicCacheControlConfig? value)
    {
        value = null;
        if (options.AdditionalProperties?.TryGetValue(CacheControl, out var raw) != true || raw is null)
            return false;

        if (raw is AnthropicCacheControlConfig typed)
        {
            value = typed;
            return true;
        }

        if (raw is JsonElement json)
        {
            value = json.Deserialize(AnthropicJsonContext.Default.AnthropicCacheControlConfig);
            return value is not null;
        }

        return false;
    }

    private static bool TryGetEnum<T>(AdditionalPropertiesDictionary? properties, string key, out T value)
        where T : struct, Enum
    {
        value = default;
        if (properties?.TryGetValue(key, out var raw) != true || raw is null)
            return false;

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        if (raw is string text && TryParseEnumText(text, out T parsed))
        {
            value = parsed;
            return true;
        }

        if (raw is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.String &&
                TryParseEnumText(json.GetString(), out parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseEnumText<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (Enum.TryParse<T>(text, ignoreCase: true, out value))
            return true;

        var normalized = text.Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetInt64(AdditionalPropertiesDictionary? properties, string key, out long value)
    {
        value = default;
        if (properties?.TryGetValue(key, out var raw) != true || raw is null)
            return false;

        switch (raw)
        {
            case long longValue:
                value = longValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetInt64(out var jsonValue):
                value = jsonValue;
                return true;
            default:
                return false;
        }
    }

    private static ServiceTier ToAnthropicServiceTier(AnthropicServiceTier serviceTier)
        => serviceTier switch
        {
            AnthropicServiceTier.Auto => global::Anthropic.Models.Messages.ServiceTier.Auto,
            AnthropicServiceTier.StandardOnly => global::Anthropic.Models.Messages.ServiceTier.StandardOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(serviceTier), serviceTier, null)
        };

    private static ThinkingConfigEnabledDisplay ToAnthropicThinkingDisplay(
        AnthropicThinkingDisplay display)
        => display switch
        {
            AnthropicThinkingDisplay.Summarized => ThinkingConfigEnabledDisplay.Summarized,
            AnthropicThinkingDisplay.Omitted => ThinkingConfigEnabledDisplay.Omitted,
            _ => throw new ArgumentOutOfRangeException(nameof(display), display, null)
        };
}

internal sealed class AnthropicServiceTierJsonConverter : JsonConverter<AnthropicServiceTier>
{
    public override AnthropicServiceTier Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "auto" => AnthropicServiceTier.Auto,
            "standard_only" => AnthropicServiceTier.StandardOnly,
            _ => throw new JsonException("Invalid Anthropic service tier.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        AnthropicServiceTier value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            AnthropicServiceTier.Auto => "auto",
            AnthropicServiceTier.StandardOnly => "standard_only",
            _ => throw new JsonException("Invalid Anthropic service tier.")
        });
}

internal sealed class AnthropicThinkingDisplayJsonConverter : JsonConverter<AnthropicThinkingDisplay>
{
    public override AnthropicThinkingDisplay Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "summarized" => AnthropicThinkingDisplay.Summarized,
            "omitted" => AnthropicThinkingDisplay.Omitted,
            _ => throw new JsonException("Invalid Anthropic thinking display.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        AnthropicThinkingDisplay value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            AnthropicThinkingDisplay.Summarized => "summarized",
            AnthropicThinkingDisplay.Omitted => "omitted",
            _ => throw new JsonException("Invalid Anthropic thinking display.")
        });
}

internal sealed class AnthropicCacheTtlJsonConverter : JsonConverter<AnthropicCacheTtl>
{
    public override AnthropicCacheTtl Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "5m" => AnthropicCacheTtl.FiveMinutes,
            "1h" => AnthropicCacheTtl.OneHour,
            _ => throw new JsonException("Invalid Anthropic cache TTL.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        AnthropicCacheTtl value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            AnthropicCacheTtl.FiveMinutes => "5m",
            AnthropicCacheTtl.OneHour => "1h",
            _ => throw new JsonException("Invalid Anthropic cache TTL.")
        });
}
