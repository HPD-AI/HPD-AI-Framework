using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Anthropic;

/// <summary>
/// Anthropic-specific provider configuration based on the official Anthropic SDK.
/// These options map directly to MessageCreateParams in the official SDK.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "anthropic",
///     "ModelName": "claude-sonnet-4-5-20250929",
///     "ApiKey": "sk-ant-...",
///     "ProviderOptionsJson": "{\"thinkingBudgetTokens\":4096,\"serviceTier\":\"auto\"}"
///   }
/// }
/// </code>
/// </summary>
public class AnthropicProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// Maximum number of tokens to generate. Default: 4096.
    /// Different models have different maximum values.
    /// See https://docs.anthropic.com/en/docs/models-overview for details.
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int MaxTokens { get; set; } = 4096;

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// Amount of randomness injected into the response.
    /// Ranges from 0.0 to 1.0. Defaults to 1.0.
    /// Use temperature closer to 0.0 for analytical tasks, and closer to 1.0 for creative tasks.
    /// Maps to MessageCreateParams.Temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// Use nucleus sampling. Compute the cumulative distribution over all options
    /// for each subsequent token in decreasing probability order and cut it off
    /// once it reaches a particular probability specified by top_p.
    /// You should either alter temperature or top_p, but not both.
    /// Maps to MessageCreateParams.TopP.
    /// </summary>
    [JsonPropertyName("topP")]
    public double? TopP { get; set; }

    /// <summary>
    /// Only sample from the top K options for each subsequent token.
    /// Used to remove "long tail" low probability responses.
    /// Anthropic-specific parameter.
    /// Maps to MessageCreateParams.TopK.
    /// </summary>
    [JsonPropertyName("topK")]
    public long? TopK { get; set; }

    /// <summary>
    /// Custom text sequences that will cause the model to stop generating.
    /// If the model encounters one of these sequences, the response stop_reason
    /// will be "stop_sequence".
    /// Maps to MessageCreateParams.StopSequences.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    //
    // EXTENDED THINKING
    //

    /// <summary>
    /// Enable extended thinking mode by setting the token budget.
    /// When enabled, responses include thinking content blocks showing Claude's
    /// thinking process before the final answer.
    /// Must be >= 1024 and less than max_tokens.
    /// Maps to MessageCreateParams.Thinking with ThinkingConfigEnabled.
    /// See https://docs.anthropic.com/en/docs/build-with-claude/extended-thinking
    /// </summary>
    [JsonPropertyName("thinkingBudgetTokens")]
    public long? ThinkingBudgetTokens { get; set; }

    //
    // SERVICE TIER
    //

    /// <summary>
    /// Service tier for request prioritization.
    /// Values: "auto" (default), "standard_only"
    /// - auto: Use priority capacity if available, otherwise standard
    /// - standard_only: Only use standard capacity
    /// Maps to MessageCreateParams.ServiceTier.
    /// See https://docs.anthropic.com/en/api/service-tiers
    /// </summary>
    [JsonPropertyName("serviceTier")]
    public string? ServiceTier { get; set; }

    //
    // PROMPT CACHING
    //

    /// <summary>
    /// Enable prompt caching to reduce costs on repeated prompts.
    /// Prompt caching allows you to cache large contexts (like long documents or extensive code)
    /// and reuse them across multiple API calls, reducing costs by up to 90% for cached content.
    /// See https://docs.anthropic.com/en/docs/build-with-claude/prompt-caching
    /// </summary>
    [JsonPropertyName("enablePromptCaching")]
    public bool EnablePromptCaching { get; set; }

    /// <summary>
    /// Cache time-to-live in minutes. Default is 5 minutes.
    /// Cached content expires after this duration of inactivity.
    /// Valid range: 1-60 minutes.
    /// </summary>
    [JsonPropertyName("promptCacheTTLMinutes")]
    public int? PromptCacheTTLMinutes { get; set; }
}
