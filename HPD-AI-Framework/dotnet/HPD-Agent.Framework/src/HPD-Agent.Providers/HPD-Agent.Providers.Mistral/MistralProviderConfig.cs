using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Mistral;

/// <summary>
/// Mistral-specific provider configuration using the Mistral.SDK.
/// These options map to ChatCompletionRequest parameters.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "mistral",
///     "ModelName": "mistral-large-latest",
///     "ApiKey": "your-api-key",
///     "ProviderOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7,\"safePrompt\":true}"
///   }
/// }
/// </code>
/// </summary>
public class MistralProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// Maximum number of tokens to generate in the completion.
    /// The token count of your prompt plus max_tokens cannot exceed the model's context length.
    /// Maps to ChatCompletionRequest.MaxTokens.
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// What sampling temperature to use, between 0.0 and 1.0.
    /// Higher values like 0.8 will make the output more random, while lower values like 0.2
    /// will make it more focused and deterministic.
    /// You should either alter temperature or topP, but not both.
    /// Default: 0.7
    /// Maps to ChatCompletionRequest.Temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public decimal? Temperature { get; set; }

    /// <summary>
    /// Nucleus sampling, where the model considers the results of the tokens with top_p probability mass.
    /// So 0.1 means only the tokens comprising the top 10% probability mass are considered.
    /// You should either alter temperature or topP, but not both.
    /// Default: 1.0
    /// Ranges from 0.0 to 1.0.
    /// Maps to ChatCompletionRequest.TopP.
    /// </summary>
    [JsonPropertyName("topP")]
    public decimal? TopP { get; set; }

    //
    // DETERMINISM
    //

    /// <summary>
    /// The seed to use for random sampling. If set, different calls will generate deterministic results.
    /// This enables reproducible outputs for testing and debugging.
    /// Maps to ChatCompletionRequest.RandomSeed.
    /// </summary>
    [JsonPropertyName("randomSeed")]
    public int? RandomSeed { get; set; }

    //
    // RESPONSE FORMAT
    //

    /// <summary>
    /// Output format specification. Options:
    /// - "text" (default): Plain text response
    /// - "json_object": Forces the model to return valid JSON
    ///
    /// When using json_object mode, you should instruct the model to produce JSON
    /// via a system or user message.
    /// Maps to ChatCompletionRequest.ResponseFormat.
    /// </summary>
    [JsonPropertyName("responseFormat")]
    public string? ResponseFormat { get; set; }

    //
    // SAFETY
    //

    /// <summary>
    /// Whether to inject a safety prompt before all conversations.
    /// This adds Mistral's safety guardrails to prevent harmful outputs.
    /// Default: false
    /// Maps to ChatCompletionRequest.SafePrompt.
    /// </summary>
    [JsonPropertyName("safePrompt")]
    public bool? SafePrompt { get; set; }

    //
    // TOOL/FUNCTION CALLING
    //

    /// <summary>
    /// Tool choice behavior control. Options:
    /// - "auto" (default): Model decides whether to call tools
    /// - "any": Model must call at least one tool (similar to "required")
    /// - "none": Model will not call any tools
    ///
    /// Maps to ChatCompletionRequest.ToolChoice.
    /// Note: Tools are defined at the request level via AgentBuilder, not in config.
    /// </summary>
    [JsonPropertyName("toolChoice")]
    public string? ToolChoice { get; set; }

    /// <summary>
    /// Whether to enable parallel function calling during tool use.
    /// When true, the model can call multiple tools in parallel.
    /// When false, tools are called sequentially.
    /// Default: true
    /// Maps to ChatCompletionRequest.ParallelToolCalls.
    /// </summary>
    [JsonPropertyName("parallelToolCalls")]
    public bool? ParallelToolCalls { get; set; }

    //
    // ADVANCED OPTIONS
    //

    /// <summary>
    /// Additional custom parameters to pass to the model.
    /// This is a flexible dictionary for model-specific parameters not covered
    /// by the standard options.
    /// </summary>
    [JsonPropertyName("additionalProperties")]
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}
