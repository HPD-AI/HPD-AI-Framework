using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;

namespace HPD.Agent.Providers.OpenAICompatible;

/// <summary>
/// Describes the optional Chat Completions request fields accepted by an
/// OpenAI-compatible provider.
/// </summary>
/// <remarks>
/// OpenAI compatibility guarantees only the basic shape of a chat request.
/// Optional fields are disabled by default and must be enabled explicitly by
/// each provider.
/// </remarks>
public sealed class OpenAICompatibleRequestProfile
{
    /// <summary>Gets or initializes whether <c>temperature</c> is supported.</summary>
    public bool Temperature { get; init; }

    /// <summary>Gets or initializes whether <c>top_p</c> is supported.</summary>
    public bool TopP { get; init; }

    /// <summary>Gets or initializes whether <c>top_k</c> is supported.</summary>
    public bool TopK { get; init; }

    /// <summary>Gets or initializes the provider's output-token field.</summary>
    public OpenAICompatibleMaxTokensField MaxTokensField { get; init; }

    /// <summary>Gets or initializes whether <c>frequency_penalty</c> is supported.</summary>
    public bool FrequencyPenalty { get; init; }

    /// <summary>Gets or initializes whether <c>presence_penalty</c> is supported.</summary>
    public bool PresencePenalty { get; init; }

    /// <summary>Gets or initializes whether <c>stop</c> is supported.</summary>
    public bool StopSequences { get; init; }

    /// <summary>Gets or initializes whether <c>seed</c> is supported.</summary>
    public bool Seed { get; init; }

    /// <summary>Gets or initializes whether the text response format is supported.</summary>
    public bool TextResponseFormat { get; init; }

    /// <summary>Gets or initializes whether the JSON object response format is supported.</summary>
    public bool JsonObjectResponseFormat { get; init; }

    /// <summary>Gets or initializes whether the JSON Schema response format is supported.</summary>
    public bool JsonSchemaResponseFormat { get; init; }

    /// <summary>Gets or initializes whether strict JSON schemas are supported.</summary>
    public bool StrictJsonSchema { get; init; }

    /// <summary>Gets or initializes whether function tools are supported.</summary>
    public bool Tools { get; init; }

    /// <summary>Gets or initializes whether strict function schemas are supported.</summary>
    public bool StrictTools { get; init; }

    /// <summary>Gets or initializes whether automatic tool choice is supported.</summary>
    public bool AutoToolChoice { get; init; }

    /// <summary>Gets or initializes whether disabling tool calls is supported.</summary>
    public bool NoneToolChoice { get; init; }

    /// <summary>Gets or initializes whether required tool choice is supported.</summary>
    public bool RequiredToolChoice { get; init; }

    /// <summary>Gets or initializes whether named tool choice is supported.</summary>
    public bool NamedToolChoice { get; init; }

    /// <summary>Gets or initializes whether <c>parallel_tool_calls</c> is supported.</summary>
    public bool ParallelToolCalls { get; init; }

    /// <summary>Gets or initializes whether streamed usage can be requested.</summary>
    public bool StreamingUsage { get; init; }

    /// <summary>Gets or initializes whether OpenAI-shaped image parts are supported.</summary>
    public bool Vision { get; init; }

    /// <summary>
    /// Gets or initializes the provider-specific reasoning translator.
    /// </summary>
    public Action<OpenAICompatibleChatRequest, Microsoft.Extensions.AI.ReasoningOptions>? ApplyReasoning { get; init; }

    /// <summary>
    /// Creates a profile containing the complete request surface.
    /// </summary>
    /// <remarks>This profile is intended for transport tests, not provider defaults.</remarks>
    public static OpenAICompatibleRequestProfile All { get; } = new()
    {
        Temperature = true,
        TopP = true,
        TopK = true,
        MaxTokensField = OpenAICompatibleMaxTokensField.MaxTokens,
        FrequencyPenalty = true,
        PresencePenalty = true,
        StopSequences = true,
        Seed = true,
        TextResponseFormat = true,
        JsonObjectResponseFormat = true,
        JsonSchemaResponseFormat = true,
        StrictJsonSchema = true,
        Tools = true,
        StrictTools = true,
        AutoToolChoice = true,
        NoneToolChoice = true,
        RequiredToolChoice = true,
        NamedToolChoice = true,
        ParallelToolCalls = true,
        StreamingUsage = true,
        Vision = true,
        ApplyReasoning = static (request, reasoning) =>
        {
            request.ReasoningEffort = reasoning.Effort switch
            {
                Microsoft.Extensions.AI.ReasoningEffort.None => "none",
                Microsoft.Extensions.AI.ReasoningEffort.Low => "low",
                Microsoft.Extensions.AI.ReasoningEffort.Medium => "medium",
                Microsoft.Extensions.AI.ReasoningEffort.High => "high",
                Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh => "xhigh",
                _ => null
            };
        }
    };
}

/// <summary>Specifies the output-token field used by a provider.</summary>
public enum OpenAICompatibleMaxTokensField
{
    /// <summary>Do not send an output-token field.</summary>
    None,

    /// <summary>Send the value as <c>max_tokens</c>.</summary>
    MaxTokens,

    /// <summary>Send the value as <c>max_completion_tokens</c>.</summary>
    MaxCompletionTokens
}
