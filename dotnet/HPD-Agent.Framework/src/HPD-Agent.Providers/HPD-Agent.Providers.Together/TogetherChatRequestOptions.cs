using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Serializable Together-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as model, temperature, top-p, top-k, frequency penalty,
/// presence penalty, max output tokens, seed, stop sequences, response format, tools,
/// and reasoning belong on <see cref="ChatClientConfig"/> or <see cref="ChatOptions"/>.
/// </remarks>
public sealed class TogetherChatRequestOptions : IChatRequestOptions
{
    /// <summary>
    /// Behavior when the requested output would exceed the model context length.
    /// </summary>
    public TogetherContextLengthExceededBehavior? ContextLengthExceededBehavior { get; set; }

    /// <summary>
    /// Reduces repeated text. Higher values decrease repetition.
    /// </summary>
    public double? RepetitionPenalty { get; set; }

    /// <summary>
    /// Number of top token log probabilities to include at each generation step. Together supports 0 through 20.
    /// </summary>
    public int? Logprobs { get; set; }

    /// <summary>
    /// Includes the prompt in the response. Often paired with <see cref="Logprobs"/>.
    /// </summary>
    public bool? Echo { get; set; }

    /// <summary>
    /// Number of completions to generate for each prompt.
    /// </summary>
    public int? N { get; set; }

    /// <summary>
    /// Alternative probability threshold to top-p and top-k.
    /// </summary>
    public float? MinP { get; set; }

    /// <summary>
    /// Adjusts the likelihood of specific token ids appearing in the generated output.
    /// </summary>
    public Dictionary<string, float>? LogitBias { get; set; }

    /// <summary>
    /// Together compliance mode value.
    /// </summary>
    public string? Compliance { get; set; }

    /// <summary>
    /// Additional model-engine chat template options.
    /// </summary>
    public Dictionary<string, object>? ChatTemplateKwargs { get; set; }

    /// <summary>
    /// Name of the Together moderation model used to validate generated tokens.
    /// </summary>
    public string? SafetyModel { get; set; }

    /// <summary>
    /// Overrides whether Together reasoning is enabled. If unset, generic reasoning controls this.
    /// </summary>
    public bool? ReasoningEnabled { get; set; }

    /// <summary>
    /// Converts the typed options to additional properties consumed by the Together configured client.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        var properties = new Dictionary<string, object>();

        Add(properties, TogetherChatRequestOptionKeys.ContextLengthExceededBehavior, ToWireString(ContextLengthExceededBehavior));
        Add(properties, TogetherChatRequestOptionKeys.RepetitionPenalty, RepetitionPenalty);
        Add(properties, TogetherChatRequestOptionKeys.Logprobs, Logprobs);
        Add(properties, TogetherChatRequestOptionKeys.Echo, Echo);
        Add(properties, TogetherChatRequestOptionKeys.N, N);
        Add(properties, TogetherChatRequestOptionKeys.MinP, MinP);
        Add(properties, TogetherChatRequestOptionKeys.LogitBias, LogitBias);
        Add(properties, TogetherChatRequestOptionKeys.Compliance, Compliance);
        Add(properties, TogetherChatRequestOptionKeys.ChatTemplateKwargs, ChatTemplateKwargs);
        Add(properties, TogetherChatRequestOptionKeys.SafetyModel, SafetyModel);
        Add(properties, TogetherChatRequestOptionKeys.ReasoningEnabled, ReasoningEnabled);

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

    private static string? ToWireString(TogetherContextLengthExceededBehavior? value)
        => value switch
        {
            TogetherContextLengthExceededBehavior.Error => "error",
            TogetherContextLengthExceededBehavior.Truncate => "truncate",
            _ => null
        };
}

/// <summary>
/// Together behavior when a request exceeds the model context length.
/// </summary>
[JsonConverter(typeof(TogetherContextLengthExceededBehaviorJsonConverter))]
public enum TogetherContextLengthExceededBehavior
{
    Error,
    Truncate
}

internal sealed class TogetherContextLengthExceededBehaviorJsonConverter : JsonConverter<TogetherContextLengthExceededBehavior>
{
    public override TogetherContextLengthExceededBehavior Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString() switch
        {
            "error" => TogetherContextLengthExceededBehavior.Error,
            "truncate" => TogetherContextLengthExceededBehavior.Truncate,
            var value => throw new JsonException($"Unknown Together context length exceeded behavior '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, TogetherContextLengthExceededBehavior value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            TogetherContextLengthExceededBehavior.Error => "error",
            TogetherContextLengthExceededBehavior.Truncate => "truncate",
            _ => throw new JsonException($"Unknown Together context length exceeded behavior '{value}'.")
        });
    }
}

/// <summary>
/// Extension helpers for applying Together-specific chat request options.
/// </summary>
public static class TogetherChatRequestOptionExtensions
{
    /// <summary>
    /// Applies Together-specific runtime options to a serializable HPD chat run configuration.
    /// </summary>
    public static ChatClientConfig UseTogetherChatRequestOptions(
        this ChatClientConfig chat,
        TogetherChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Together-specific runtime options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseTogetherChatRequestOptions(
        this ChatOptions chat,
        TogetherChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }
}

internal static class TogetherChatRequestOptionKeys
{
    public const string ContextLengthExceededBehavior = "context_length_exceeded_behavior";
    public const string RepetitionPenalty = "repetition_penalty";
    public const string Logprobs = "logprobs";
    public const string Echo = "echo";
    public const string N = "n";
    public const string MinP = "min_p";
    public const string LogitBias = "logit_bias";
    public const string Compliance = "compliance";
    public const string ChatTemplateKwargs = "chat_template_kwargs";
    public const string SafetyModel = "safety_model";
    public const string ReasoningEnabled = "reasoning_enabled";

    public static void ApplyRawRequestOptions(ChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hasKnownOptions = HasKnownTogetherOptions(options.AdditionalProperties);
        var hasGenericOptions =
            options.FrequencyPenalty.HasValue ||
            options.PresencePenalty.HasValue ||
            options.Reasoning is not null;

        if (!hasKnownOptions && !hasGenericOptions)
            return;

        var previousFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var request = previousFactory?.Invoke(client) as global::Together.ChatCompletionRequest
                ?? new global::Together.ChatCompletionRequest
                {
                    Model = options.ModelId ?? "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                    Messages = []
                };

            Apply(request, options);
            return request;
        };
    }

    private static void Apply(global::Together.ChatCompletionRequest request, ChatOptions options)
    {
        var properties = options.AdditionalProperties;

        if (options.FrequencyPenalty.HasValue)
            request.FrequencyPenalty ??= (float)options.FrequencyPenalty.Value;

        if (options.PresencePenalty.HasValue)
            request.PresencePenalty ??= (float)options.PresencePenalty.Value;

        if (TryGetString(properties, ContextLengthExceededBehavior, out var behavior) &&
            ToContextLengthExceededBehavior(behavior) is { } behaviorValue)
        {
            request.ContextLengthExceededBehavior ??= behaviorValue;
        }

        if (TryGetDouble(properties, RepetitionPenalty, out var repetitionPenalty))
            request.RepetitionPenalty ??= repetitionPenalty;

        if (TryGetInt(properties, Logprobs, out var logprobs))
            request.Logprobs ??= logprobs;

        if (TryGetBool(properties, Echo, out var echo))
            request.Echo ??= echo;

        if (TryGetInt(properties, N, out var n))
            request.N ??= n;

        if (TryGetFloat(properties, MinP, out var minP))
            request.MinP ??= minP;

        if (TryGetLogitBias(properties, LogitBias, out var logitBias))
            request.LogitBias ??= logitBias;

        if (TryGetString(properties, Compliance, out var compliance))
            request.Compliance ??= compliance;

        if (TryGetObject(properties, ChatTemplateKwargs, out var chatTemplateKwargs))
            request.ChatTemplateKwargs ??= chatTemplateKwargs;

        if (TryGetString(properties, SafetyModel, out var safetyModel))
            request.SafetyModel ??= safetyModel;

        ApplyReasoning(request, options, properties);
    }

    private static void ApplyReasoning(
        global::Together.ChatCompletionRequest request,
        ChatOptions options,
        AdditionalPropertiesDictionary? properties)
    {
        if (options.Reasoning?.Effort is { } effort &&
            ToReasoningEffort(effort) is { } effortValue)
        {
            request.ReasoningEffort ??= effortValue;
        }

        var hasReasoningEnabled = TryGetBool(properties, ReasoningEnabled, out var reasoningEnabled);
        if (!hasReasoningEnabled && options.Reasoning is null)
            return;

        var enabled = hasReasoningEnabled
            ? reasoningEnabled
            : options.Reasoning?.Effort is not Microsoft.Extensions.AI.ReasoningEffort.None;

        request.Reasoning ??= new global::Together.ChatCompletionRequestReasoning();
        request.Reasoning.Enabled ??= enabled;
    }

    private static bool HasKnownTogetherOptions(AdditionalPropertiesDictionary? properties)
        => properties is not null &&
           (properties.ContainsKey(ContextLengthExceededBehavior) ||
            properties.ContainsKey(RepetitionPenalty) ||
            properties.ContainsKey(Logprobs) ||
            properties.ContainsKey(Echo) ||
            properties.ContainsKey(N) ||
            properties.ContainsKey(MinP) ||
            properties.ContainsKey(LogitBias) ||
            properties.ContainsKey(Compliance) ||
            properties.ContainsKey(ChatTemplateKwargs) ||
            properties.ContainsKey(SafetyModel) ||
            properties.ContainsKey(ReasoningEnabled));

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

    private static bool TryGetFloat(AdditionalPropertiesDictionary? properties, string key, out float value)
    {
        value = default;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case float typed:
                value = typed;
                return true;
            case double typed when typed >= float.MinValue && typed <= float.MaxValue:
                value = (float)typed;
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetSingle(out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetDouble(AdditionalPropertiesDictionary? properties, string key, out double value)
    {
        value = default;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case double typed:
                value = typed;
                return true;
            case float typed:
                value = typed;
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.Number && json.TryGetDouble(out var parsed):
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

    private static bool TryGetObject(AdditionalPropertiesDictionary? properties, string key, out object value)
    {
        value = default!;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        value = raw;
        return true;
    }

    private static bool TryGetLogitBias(
        AdditionalPropertiesDictionary? properties,
        string key,
        out Dictionary<string, float>? value)
    {
        value = null;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        value = raw switch
        {
            Dictionary<string, float> typed => typed,
            IReadOnlyDictionary<string, float> typed => new Dictionary<string, float>(typed),
            JsonElement json => json.Deserialize(TogetherJsonContext.Default.DictionaryStringSingle),
            _ => null
        };

        return value is not null;
    }

    private static global::Together.ChatCompletionRequestContextLengthExceededBehavior? ToContextLengthExceededBehavior(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "error" => global::Together.ChatCompletionRequestContextLengthExceededBehavior.Error,
            "truncate" => global::Together.ChatCompletionRequestContextLengthExceededBehavior.Truncate,
            _ => null
        };

    private static global::Together.ChatCompletionRequestReasoningEffort? ToReasoningEffort(Microsoft.Extensions.AI.ReasoningEffort effort)
        => effort switch
        {
            Microsoft.Extensions.AI.ReasoningEffort.High => global::Together.ChatCompletionRequestReasoningEffort.High,
            Microsoft.Extensions.AI.ReasoningEffort.Medium => global::Together.ChatCompletionRequestReasoningEffort.Medium,
            Microsoft.Extensions.AI.ReasoningEffort.Low => global::Together.ChatCompletionRequestReasoningEffort.Low,
            _ => null
        };
}
