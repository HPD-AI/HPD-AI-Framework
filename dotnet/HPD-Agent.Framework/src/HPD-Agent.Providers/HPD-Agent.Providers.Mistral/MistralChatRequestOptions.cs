using System;
using System.Text.Json.Serialization;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Mistral;

/// <summary>
/// Mistral-specific per-request chat options not represented by generic <see cref="ChatClientConfig"/>.
/// </summary>
public sealed class MistralChatRequestOptions : IChatRequestOptions
{
    internal const string AdditionalPropertiesKey = "hpd.mistral.chatRequestOptions";

    /// <summary>
    /// Enables Mistral's safety prompt injection before the conversation.
    /// </summary>
    [JsonPropertyName("safePrompt")]
    public bool? SafePrompt { get; set; }

    /// <summary>
    /// Expected completion content used by Mistral prediction to reduce latency for predictable edits.
    /// </summary>
    [JsonPropertyName("predictionContent")]
    public string? PredictionContent { get; set; }

    /// <summary>
    /// Prompt cache key used by Mistral to reuse previously computed prompt prefixes.
    /// </summary>
    [JsonPropertyName("promptCacheKey")]
    public string? PromptCacheKey { get; set; }

    /// <summary>
    /// Number of completions to return for the request.
    /// </summary>
    [JsonPropertyName("completionCount")]
    public int? CompletionCount { get; set; }

    /// <summary>
    /// Applies these options to serializable HPD chat run configuration.
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
        options.AdditionalProperties ??= [];
        options.AdditionalProperties[AdditionalPropertiesKey] = this;
    }

    internal static MistralChatRequestOptions? From(ChatOptions? options)
    {
        if (options?.AdditionalProperties?.TryGetValue(AdditionalPropertiesKey, out var value) != true)
            return null;

        return value as MistralChatRequestOptions;
    }
}

/// <summary>
/// Fluent helpers for applying Mistral-specific per-request options.
/// </summary>
public static class MistralChatRequestOptionsExtensions
{
    /// <summary>
    /// Applies Mistral-specific per-request chat options to HPD chat run configuration.
    /// </summary>
    public static ChatClientConfig UseMistralChatRequestOptions(
        this ChatClientConfig chat,
        MistralChatRequestOptions options)
    {
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Mistral-specific per-request chat options to HPD chat run configuration.
    /// </summary>
    public static ChatClientConfig UseMistralChatRequestOptions(
        this ChatClientConfig chat,
        Action<MistralChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new MistralChatRequestOptions();
        configure(options);
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Mistral-specific per-request chat options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseMistralChatRequestOptions(
        this ChatOptions chat,
        MistralChatRequestOptions options)
    {
        options.ApplyTo(chat);
        return chat;
    }

    /// <summary>
    /// Applies Mistral-specific per-request chat options to Microsoft.Extensions.AI chat options.
    /// </summary>
    public static ChatOptions UseMistralChatRequestOptions(
        this ChatOptions chat,
        Action<MistralChatRequestOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new MistralChatRequestOptions();
        configure(options);
        options.ApplyTo(chat);
        return chat;
    }
}
