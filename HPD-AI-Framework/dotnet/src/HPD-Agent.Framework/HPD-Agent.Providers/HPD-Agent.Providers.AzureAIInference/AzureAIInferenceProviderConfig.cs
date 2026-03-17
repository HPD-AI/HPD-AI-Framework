using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.AzureAIInference;

/// <summary>
/// Azure AI Inference-specific provider configuration based on the Azure AI Inference SDK.
/// These options map directly to ChatCompletionsOptions in the official SDK.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "azure-ai-inference",
///     "ModelName": "llama-3-8b",
///     "Endpoint": "https://your-resource.inference.ai.azure.com",
///     "ApiKey": "your-api-key",
///     "ProviderOptionsJson": "{\"maxTokens\":2048,\"temperature\":0.7,\"seed\":12345}"
///   }
/// }
/// </code>
/// </summary>
public class AzureAIInferenceProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// Maximum number of tokens to generate. Default: 2048.
    /// Maps to ChatCompletionsOptions.MaxTokens.
    /// See https://learn.microsoft.com/en-us/azure/ai-studio/how-to/deploy-models-inference
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// Amount of randomness injected into the response.
    /// Ranges from 0.0 to 1.0. Higher values increase creativity.
    /// Use temperature closer to 0.0 for analytical tasks, and closer to 1.0 for creative tasks.
    /// Maps to ChatCompletionsOptions.Temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Use nucleus sampling (Top-P). Compute the cumulative distribution over all options
    /// for each subsequent token in decreasing probability order and cut it off
    /// once it reaches a particular probability specified by top_p.
    /// Ranges from 0.0 to 1.0.
    /// You should either alter temperature or topP, but not both.
    /// Maps to ChatCompletionsOptions.NucleusSamplingFactor.
    /// </summary>
    [JsonPropertyName("topP")]
    public float? TopP { get; set; }

    /// <summary>
    /// Frequency penalty reduces the likelihood of repeating the same token.
    /// Ranges from -2.0 to 2.0.
    /// Positive values penalize tokens based on their frequency in the text so far.
    /// Maps to ChatCompletionsOptions.FrequencyPenalty.
    /// </summary>
    [JsonPropertyName("frequencyPenalty")]
    public float? FrequencyPenalty { get; set; }

    /// <summary>
    /// Presence penalty reduces the likelihood of repeating any token that has appeared.
    /// Ranges from -2.0 to 2.0.
    /// Positive values penalize tokens that have already appeared in the text.
    /// Maps to ChatCompletionsOptions.PresencePenalty.
    /// </summary>
    [JsonPropertyName("presencePenalty")]
    public float? PresencePenalty { get; set; }

    /// <summary>
    /// Custom text sequences that will cause the model to stop generating.
    /// If the model encounters one of these sequences, generation stops.
    /// Maps to ChatCompletionsOptions.StopSequences.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    //
    // DETERMINISM
    //

    /// <summary>
    /// Seed for deterministic generation. Setting a seed will make the model
    /// attempt to generate the same output for the same input.
    /// Note: This is best-effort determinism and may vary across different
    /// model versions or infrastructure.
    /// Maps to ChatCompletionsOptions.Seed.
    /// </summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    //
    // RESPONSE FORMAT
    //

    /// <summary>
    /// Output format specification. Options:
    /// - "text" (default): Plain text response
    /// - "json_object": Loose JSON mode (model generates valid JSON)
    /// - "json_schema": Structured output with strict schema validation
    ///
    /// For json_schema mode, use JsonSchemaName and JsonSchema properties.
    /// Maps to ChatCompletionsOptions.ResponseFormat.
    /// </summary>
    [JsonPropertyName("responseFormat")]
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// Name of the JSON schema when using json_schema response format.
    /// Required when ResponseFormat is "json_schema".
    /// </summary>
    [JsonPropertyName("jsonSchemaName")]
    public string? JsonSchemaName { get; set; }

    /// <summary>
    /// JSON schema definition as a JSON string when using json_schema response format.
    /// Required when ResponseFormat is "json_schema".
    /// Example: "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"
    /// </summary>
    [JsonPropertyName("jsonSchema")]
    public string? JsonSchema { get; set; }

    /// <summary>
    /// Optional description for the JSON schema.
    /// Only used when ResponseFormat is "json_schema".
    /// </summary>
    [JsonPropertyName("jsonSchemaDescription")]
    public string? JsonSchemaDescription { get; set; }

    /// <summary>
    /// Whether to enforce strict schema adherence for structured outputs.
    /// Default: true when using json_schema response format.
    /// Only used when ResponseFormat is "json_schema".
    /// </summary>
    [JsonPropertyName("jsonSchemaIsStrict")]
    public bool? JsonSchemaIsStrict { get; set; }

    //
    // TOOL/FUNCTION CALLING
    //

    /// <summary>
    /// Tool choice behavior control. Options:
    /// - "auto" (default): Model decides whether to call tools
    /// - "none": Model will not call any tools
    /// - "required": Model must call at least one tool
    ///
    /// Maps to ChatCompletionsOptions.ToolChoice.
    /// Note: Tools are defined at the request level, not in config.
    /// </summary>
    [JsonPropertyName("toolChoice")]
    public string? ToolChoice { get; set; }

    //
    // ADVANCED OPTIONS
    //

    /// <summary>
    /// Specific model ID to use when multiple models are deployed to the same endpoint.
    /// If not specified, uses the model from ProviderConfig.ModelName.
    /// Maps to ChatCompletionsOptions.Model.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Additional custom parameters to pass to the model.
    /// This is a flexible dictionary for model-specific parameters not covered
    /// by the standard options.
    /// Maps to ChatCompletionsOptions.AdditionalProperties.
    /// </summary>
    [JsonPropertyName("additionalProperties")]
    public Dictionary<string, object>? AdditionalProperties { get; set; }

    /// <summary>
    /// How to handle extra/unknown parameters when sending to the API.
    /// Options:
    /// - "pass-through" (default): Allow extra parameters
    /// - "error": Reject request with extra parameters
    /// - "drop": Remove extra parameters silently
    ///
    /// This controls the "extra-parameters" header sent to the API.
    /// </summary>
    [JsonPropertyName("extraParametersMode")]
    public string? ExtraParametersMode { get; set; }
}
