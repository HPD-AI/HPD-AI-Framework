using System;
using System.Collections.Generic;
using System.Text.Json;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// Serializable Hugging Face-specific chat request options.
/// </summary>
/// <remarks>
/// Generic runtime settings such as model, temperature, top-p, max output tokens,
/// frequency penalty, presence penalty, seed, stop sequences, response format, and tools
/// belong on <see cref="ChatRunConfig"/> or <see cref="ChatOptions"/>.
/// </remarks>
public sealed class HuggingFaceChatRequestOptions
{
    /// <summary>
    /// Returns output token log probabilities.
    /// </summary>
    public bool? Logprobs { get; set; }

    /// <summary>
    /// Number of likely tokens to return at each token position. Hugging Face supports 0 through 5.
    /// </summary>
    public int? TopLogprobs { get; set; }

    /// <summary>
    /// Number of chat completion choices to generate.
    /// </summary>
    public int? N { get; set; }

    /// <summary>
    /// Bias values for token ids in the raw Hugging Face chat request.
    /// </summary>
    public List<float>? LogitBias { get; set; }

    /// <summary>
    /// Prompt appended before tool definitions for models that need tool-use steering.
    /// </summary>
    public string? ToolPrompt { get; set; }

    /// <summary>
    /// Converts the typed options to additional properties consumed by the Hugging Face configured client.
    /// </summary>
    public Dictionary<string, object> ToAdditionalProperties()
    {
        var properties = new Dictionary<string, object>();

        Add(properties, HuggingFaceChatRequestOptionKeys.Logprobs, Logprobs);
        Add(properties, HuggingFaceChatRequestOptionKeys.TopLogprobs, TopLogprobs);
        Add(properties, HuggingFaceChatRequestOptionKeys.N, N);
        Add(properties, HuggingFaceChatRequestOptionKeys.LogitBias, LogitBias);
        Add(properties, HuggingFaceChatRequestOptionKeys.ToolPrompt, ToolPrompt);

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
/// Extension helpers for applying Hugging Face-specific chat request options.
/// </summary>
public static class HuggingFaceChatRequestOptionExtensions
{
    /// <summary>
    /// Applies Hugging Face-specific runtime options to a serializable HPD chat run configuration.
    /// </summary>
    public static ChatRunConfig UseHuggingFaceChatRequestOptions(
        this ChatRunConfig chat,
        HuggingFaceChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Hugging Face-specific runtime options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseHuggingFaceChatRequestOptions(
        this ChatOptions chat,
        HuggingFaceChatRequestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApplyTo(chat);
        return chat;
    }
}

internal static class HuggingFaceChatRequestOptionKeys
{
    public const string Logprobs = "logprobs";
    public const string TopLogprobs = "top_logprobs";
    public const string N = "n";
    public const string LogitBias = "logit_bias";
    public const string ToolPrompt = "tool_prompt";

    public static global::HuggingFace.ChatRequest BuildRequest(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions options,
        string defaultModelId,
        bool stream)
    {
        ArgumentNullException.ThrowIfNull(chatMessages);
        ArgumentNullException.ThrowIfNull(options);

        var request = new global::HuggingFace.ChatRequest
        {
            Model = string.IsNullOrWhiteSpace(options.ModelId) ? defaultModelId : options.ModelId,
            Messages = CreateMessages(chatMessages),
            MaxTokens = options.MaxOutputTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            Seed = options.Seed,
            Stop = options.StopSequences?.ToList(),
            Stream = stream,
            Tools = CreateTools(options.Tools),
            ResponseFormat = CreateResponseFormat(options.ResponseFormat)
        };

        var properties = options.AdditionalProperties;
        if (TryGetBool(properties, Logprobs, out var logprobs))
            request.Logprobs = logprobs;

        if (TryGetInt(properties, TopLogprobs, out var topLogprobs))
            request.TopLogprobs = topLogprobs;

        if (TryGetInt(properties, N, out var n))
            request.N = n;

        if (TryGetFloatList(properties, LogitBias, out var logitBias))
            request.LogitBias = logitBias;

        if (TryGetString(properties, ToolPrompt, out var toolPrompt))
            request.ToolPrompt = toolPrompt;

        return request;
    }

    private static List<global::HuggingFace.Message> CreateMessages(IEnumerable<ChatMessage> chatMessages)
    {
        var messages = new List<global::HuggingFace.Message>();
        foreach (var chatMessage in chatMessages)
        {
            var role =
                chatMessage.Role == ChatRole.System ? "system" :
                chatMessage.Role == ChatRole.Assistant ? "assistant" :
                chatMessage.Role == ChatRole.Tool ? "tool" :
                "user";

            var text = string.Concat(chatMessage.Contents.OfType<TextContent>().Select(static content => content.Text));
            if (string.IsNullOrEmpty(text))
            {
                text = chatMessage.Text ?? string.Empty;
            }

            messages.Add(new global::HuggingFace.Message(
                body: new global::HuggingFace.MessageBody(new global::HuggingFace.MessageBodyVariant1(new global::HuggingFace.MessageContent(text))),
                messageVariant2: new global::HuggingFace.MessageVariant2(role, name: null)));
        }

        return messages;
    }

    private static IList<global::HuggingFace.Tool>? CreateTools(IList<AITool>? tools)
    {
        if (tools is not { Count: > 0 })
            return null;

        var result = new List<global::HuggingFace.Tool>();
        foreach (var function in tools.OfType<AIFunction>())
        {
            result.Add(new global::HuggingFace.Tool
            {
                Type = "function",
                Function = new global::HuggingFace.FunctionDefinition
                {
                    Name = function.Name,
                    Description = function.Description,
                    Arguments = function.JsonSchema
                }
            });
        }

        return result.Count > 0 ? result : null;
    }

    private static global::HuggingFace.GrammarType? CreateResponseFormat(ChatResponseFormat? responseFormat)
    {
        if (responseFormat is ChatResponseFormatJson json && json.Schema is JsonElement schema)
        {
            return new global::HuggingFace.GrammarType(
                new global::HuggingFace.GrammarTypeVariant1
                {
                    Type = global::HuggingFace.GrammarTypeVariant1Type.Json,
                    Value = schema
                });
        }

        if (responseFormat is ChatResponseFormatJson)
        {
            return new global::HuggingFace.GrammarType(
                new global::HuggingFace.GrammarTypeVariant1
                {
                    Type = global::HuggingFace.GrammarTypeVariant1Type.Json,
                    Value = new Dictionary<string, object>()
                });
        }

        return null;
    }

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

    private static bool TryGetFloatList(AdditionalPropertiesDictionary? properties, string key, out IList<float>? value)
    {
        value = null;
        if (properties is null || !properties.TryGetValue(key, out var raw) || raw is null)
            return false;

        value = raw switch
        {
            List<float> typed => typed,
            float[] typed => [.. typed],
            JsonElement json => json.Deserialize(HuggingFaceJsonContext.Default.ListSingle),
            _ => null
        };

        return value is not null;
    }
}
