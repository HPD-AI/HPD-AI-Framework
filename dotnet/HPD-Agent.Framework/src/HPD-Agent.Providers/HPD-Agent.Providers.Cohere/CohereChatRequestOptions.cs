using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// Serializable Cohere-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as model, temperature, top-p, top-k, max output tokens,
/// seed, stop sequences, response format, tools, and reasoning belong on
/// <see cref="ChatClientConfig"/> or <see cref="ChatOptions"/>.
/// </remarks>
public sealed class CohereChatRequestOptions : IChatRequestOptions
{
    /// <summary>
    /// Forces tool calls to follow their tool definitions strictly.
    /// </summary>
    public bool? StrictTools { get; set; }

    /// <summary>
    /// Relevant documents that the model can cite while generating an answer.
    /// </summary>
    public List<CohereChatDocument>? Documents { get; set; }

    /// <summary>
    /// Controls citation generation.
    /// </summary>
    public CohereCitationMode? CitationMode { get; set; }

    /// <summary>
    /// Selects the Cohere safety instruction mode.
    /// </summary>
    public CohereSafetyMode? SafetyMode { get; set; }

    /// <summary>
    /// Includes output token log probabilities in the raw Cohere response.
    /// </summary>
    public bool? Logprobs { get; set; }

    /// <summary>
    /// Overrides whether Cohere thinking is enabled. If unset, generic reasoning controls this.
    /// </summary>
    public bool? ThinkingEnabled { get; set; }

    /// <summary>
    /// Maximum number of tokens the model can use for thinking.
    /// </summary>
    public int? ThinkingTokenBudget { get; set; }

    /// <summary>
    /// Request priority. Lower numbers indicate higher priority; Cohere defaults to 0.
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Converts the typed options to additional properties consumed by the Cohere configured client.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        var properties = new Dictionary<string, object>();

        Add(properties, CohereChatRequestOptionKeys.StrictTools, StrictTools);
        Add(properties, CohereChatRequestOptionKeys.Documents, Documents);
        Add(properties, CohereChatRequestOptionKeys.CitationMode, ToWireString(CitationMode));
        Add(properties, CohereChatRequestOptionKeys.SafetyMode, ToWireString(SafetyMode));
        Add(properties, CohereChatRequestOptionKeys.Logprobs, Logprobs);
        Add(properties, CohereChatRequestOptionKeys.ThinkingEnabled, ThinkingEnabled);
        Add(properties, CohereChatRequestOptionKeys.ThinkingTokenBudget, ThinkingTokenBudget);
        Add(properties, CohereChatRequestOptionKeys.Priority, Priority);

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

    private static string? ToWireString(CohereCitationMode? value)
        => value switch
        {
            CohereCitationMode.Accurate => "accurate",
            CohereCitationMode.Disabled => "disabled",
            CohereCitationMode.Enabled => "enabled",
            CohereCitationMode.Fast => "fast",
            CohereCitationMode.Off => "off",
            _ => null
        };

    private static string? ToWireString(CohereSafetyMode? value)
        => value switch
        {
            CohereSafetyMode.Contextual => "contextual",
            CohereSafetyMode.Off => "off",
            CohereSafetyMode.Strict => "strict",
            _ => null
        };
}

/// <summary>
/// Cohere citation generation mode.
/// </summary>
[JsonConverter(typeof(CohereCitationModeJsonConverter))]
public enum CohereCitationMode
{
    Enabled,
    Disabled,
    Accurate,
    Fast,
    Off
}

/// <summary>
/// Cohere safety instruction mode.
/// </summary>
[JsonConverter(typeof(CohereSafetyModeJsonConverter))]
public enum CohereSafetyMode
{
    Contextual,
    Off,
    Strict
}

internal sealed class CohereCitationModeJsonConverter : JsonConverter<CohereCitationMode>
{
    public override CohereCitationMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "enabled" => CohereCitationMode.Enabled,
            "disabled" => CohereCitationMode.Disabled,
            "accurate" => CohereCitationMode.Accurate,
            "fast" => CohereCitationMode.Fast,
            "off" => CohereCitationMode.Off,
            var value => throw new JsonException($"Unknown Cohere citation mode '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, CohereCitationMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            CohereCitationMode.Enabled => "enabled",
            CohereCitationMode.Disabled => "disabled",
            CohereCitationMode.Accurate => "accurate",
            CohereCitationMode.Fast => "fast",
            CohereCitationMode.Off => "off",
            _ => throw new JsonException($"Unknown Cohere citation mode '{value}'.")
        });
    }
}

internal sealed class CohereSafetyModeJsonConverter : JsonConverter<CohereSafetyMode>
{
    public override CohereSafetyMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "contextual" => CohereSafetyMode.Contextual,
            "off" => CohereSafetyMode.Off,
            "strict" => CohereSafetyMode.Strict,
            var value => throw new JsonException($"Unknown Cohere safety mode '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, CohereSafetyMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            CohereSafetyMode.Contextual => "contextual",
            CohereSafetyMode.Off => "off",
            CohereSafetyMode.Strict => "strict",
            _ => throw new JsonException($"Unknown Cohere safety mode '{value}'.")
        });
    }
}

/// <summary>
/// Serializable Cohere document supplied to chat requests for citation-grounded responses.
/// </summary>
public sealed class CohereChatDocument
{
    /// <summary>
    /// Optional identifier referenced by Cohere citations.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Plain document text. Use this for simple string documents.
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Structured document data and metadata.
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Extension helpers for applying Cohere-specific chat request options.
/// </summary>
public static class CohereChatRequestOptionExtensions
{
    /// <summary>
    /// Applies Cohere-specific runtime options to a serializable HPD chat run configuration.
    /// </summary>
    public static ChatClientConfig UseCohereChatRequestOptions(
        this ChatClientConfig chat,
        CohereChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Cohere-specific runtime options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseCohereChatRequestOptions(
        this ChatOptions chat,
        CohereChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }
}

internal static class CohereChatRequestOptionKeys
{
    public const string StrictTools = "strict_tools";
    public const string Documents = "documents";
    public const string CitationMode = "citation_mode";
    public const string SafetyMode = "safety_mode";
    public const string Logprobs = "logprobs";
    public const string ThinkingEnabled = "thinking_enabled";
    public const string ThinkingTokenBudget = "thinking_token_budget";
    public const string Priority = "priority";

    public static void ApplyRawRequestOptions(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hasCohereOptions = HasKnownCohereOptions(options.AdditionalProperties);
        var hasGenericReasoning = options.Reasoning is not null;
        if (!hasCohereOptions && !hasGenericReasoning)
            return;

        var previousFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var request = previousFactory?.Invoke(client) as global::Cohere.Chatv2Request
                ?? new global::Cohere.Chatv2Request
                {
                    Model = options.ModelId ?? "command-r-plus",
                    Messages = []
                };

            Apply(request, options);
            return request;
        };
    }

    private static void Apply(global::Cohere.Chatv2Request request, ChatOptions options)
    {
        var properties = options.AdditionalProperties;

        if (TryGetBool(properties, StrictTools, out var strictTools))
            request.StrictTools ??= strictTools;

        if (TryGetDocuments(properties, Documents, out var documents) && documents is { Count: > 0 })
            request.Documents ??= documents;

        if (TryGetString(properties, CitationMode, out var citationMode) &&
            ToCitationMode(citationMode) is { } citationModeValue)
        {
            request.CitationOptions ??= new global::Cohere.CitationOptions();
            request.CitationOptions.Mode ??= citationModeValue;
        }

        if (TryGetString(properties, SafetyMode, out var safetyMode) &&
            ToSafetyMode(safetyMode) is { } safetyModeValue)
        {
            request.SafetyMode ??= safetyModeValue;
        }

        if (TryGetBool(properties, Logprobs, out var logprobs))
            request.Logprobs ??= logprobs;

        if (TryGetInt(properties, Priority, out var priority))
            request.Priority ??= priority;

        ApplyThinking(request, options, properties);
    }

    private static void ApplyThinking(
        global::Cohere.Chatv2Request request,
        ChatOptions options,
        AdditionalPropertiesDictionary? properties)
    {
        var hasThinkingEnabled = TryGetBool(properties, ThinkingEnabled, out var thinkingEnabled);
        var hasTokenBudget = TryGetInt(properties, ThinkingTokenBudget, out var tokenBudget);

        if (!hasThinkingEnabled && !hasTokenBudget && options.Reasoning is null)
            return;

        var enabled = hasThinkingEnabled
            ? thinkingEnabled
            : options.Reasoning?.Effort is not Microsoft.Extensions.AI.ReasoningEffort.None;

        var thinkingType = enabled
            ? global::Cohere.ThinkingType.Enabled
            : global::Cohere.ThinkingType.Disabled;

        request.Thinking ??= new global::Cohere.Thinking
        {
            Type = thinkingType
        };

        if (hasTokenBudget)
            request.Thinking.TokenBudget ??= tokenBudget;
    }

    private static bool HasKnownCohereOptions(AdditionalPropertiesDictionary? properties)
        => properties is not null &&
           (properties.ContainsKey(StrictTools) ||
            properties.ContainsKey(Documents) ||
            properties.ContainsKey(CitationMode) ||
            properties.ContainsKey(SafetyMode) ||
            properties.ContainsKey(Logprobs) ||
            properties.ContainsKey(ThinkingEnabled) ||
            properties.ContainsKey(ThinkingTokenBudget) ||
            properties.ContainsKey(Priority));

    private static bool TryGetBool(AdditionalPropertiesDictionary? properties, string key, out bool value)
    {
        value = default;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case bool typed:
                value = typed;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetInt(AdditionalPropertiesDictionary? properties, string key, out int value)
    {
        value = default;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case int typed:
                value = typed;
                return true;
            case long typed when typed >= int.MinValue && typed <= int.MaxValue:
                value = (int)typed;
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetInt32(out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetString(AdditionalPropertiesDictionary? properties, string key, out string value)
    {
        value = string.Empty;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case string typed when !string.IsNullOrWhiteSpace(typed):
                value = typed;
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.String:
                value = json.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            default:
                return false;
        }
    }

    private static bool TryGetDocuments(
        AdditionalPropertiesDictionary? properties,
        string key,
        out IList<global::Cohere.OneOf<string, global::Cohere.Document>>? documents)
    {
        documents = null;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        var source = raw switch
        {
            List<CohereChatDocument> typed => typed,
            CohereChatDocument[] typed => [.. typed],
            JsonElement json => json.Deserialize(CohereJsonContext.Default.ListCohereChatDocument),
            _ => null
        };

        if (source is null)
            return false;

        documents = new List<global::Cohere.OneOf<string, global::Cohere.Document>>();
        foreach (var document in source)
        {
            if (!string.IsNullOrWhiteSpace(document.Text))
            {
                documents.Add(document.Text);
                continue;
            }

            if (document.Data is not null)
            {
                documents.Add(new global::Cohere.Document
                {
                    Id = document.Id,
                    Data = document.Data
                });
            }
        }

        return true;
    }

    private static global::Cohere.CitationOptionsMode? ToCitationMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "accurate" => global::Cohere.CitationOptionsMode.Accurate,
            "disabled" => global::Cohere.CitationOptionsMode.Disabled,
            "enabled" => global::Cohere.CitationOptionsMode.Enabled,
            "fast" => global::Cohere.CitationOptionsMode.Fast,
            "off" => global::Cohere.CitationOptionsMode.Off,
            _ => null
        };

    private static global::Cohere.Chatv2RequestSafetyMode? ToSafetyMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "contextual" => global::Cohere.Chatv2RequestSafetyMode.Contextual,
            "off" => global::Cohere.Chatv2RequestSafetyMode.Off,
            "strict" => global::Cohere.Chatv2RequestSafetyMode.Strict,
            _ => null
        };
}
