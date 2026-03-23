using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.AzureAI;

/// <summary>
/// Azure AI Projects-specific provider configuration using the Azure.AI.Projects SDK.
/// These options map to Azure OpenAI chat completion options.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "azure-ai",
///     "ModelName": "gpt-4",
///     "Endpoint": "https://your-project.services.ai.azure.com",
///     "ProviderOptionsJson": "{\"maxTokens\":4096,\"temperature\":0.7}"
///   }
/// }
/// </code>
/// </summary>
public class AzureAIProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// Maximum number of tokens to generate. Default: 4096.
    /// Maps to ChatCompletionOptions.MaxOutputTokenCount.
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// Amount of randomness injected into the response.
    /// Ranges from 0.0 to 2.0. Higher values increase creativity.
    /// Use temperature closer to 0.0 for analytical tasks, and closer to 1.0+ for creative tasks.
    /// Maps to ChatCompletionOptions.Temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Use nucleus sampling (Top-P). Compute the cumulative distribution over all options
    /// for each subsequent token in decreasing probability order and cut it off
    /// once it reaches a particular probability specified by top_p.
    /// Ranges from 0.0 to 1.0.
    /// You should either alter temperature or topP, but not both.
    /// Maps to ChatCompletionOptions.TopP.
    /// </summary>
    [JsonPropertyName("topP")]
    public float? TopP { get; set; }

    /// <summary>
    /// Frequency penalty reduces the likelihood of repeating the same token.
    /// Ranges from -2.0 to 2.0.
    /// Positive values penalize tokens based on their frequency in the text so far.
    /// Maps to ChatCompletionOptions.FrequencyPenalty.
    /// </summary>
    [JsonPropertyName("frequencyPenalty")]
    public float? FrequencyPenalty { get; set; }

    /// <summary>
    /// Presence penalty reduces the likelihood of repeating any token that has appeared.
    /// Ranges from -2.0 to 2.0.
    /// Positive values penalize tokens that have already appeared in the text.
    /// Maps to ChatCompletionOptions.PresencePenalty.
    /// </summary>
    [JsonPropertyName("presencePenalty")]
    public float? PresencePenalty { get; set; }

    /// <summary>
    /// Custom text sequences that will cause the model to stop generating.
    /// If the model encounters one of these sequences, generation stops.
    /// Maps to ChatCompletionOptions.StopSequences.
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
    /// Maps to ChatCompletionOptions.Seed.
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
    /// Maps to ChatCompletionOptions.ResponseFormat.
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
    /// Maps to ChatCompletionOptions.ToolChoice.
    /// Note: Tools are defined at the request level, not in config.
    /// </summary>
    [JsonPropertyName("toolChoice")]
    public string? ToolChoice { get; set; }

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

    //
    // AZURE AI PROJECTS SPECIFIC
    //

    /// <summary>
    /// Azure AI Project ID/Name. If not provided, will be extracted from the endpoint URL.
    /// Format: The project name portion of https://account.services.ai.azure.com/api/projects/PROJECT_NAME
    /// </summary>
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    /// <summary>
    /// Whether to use DefaultAzureCredential for OAuth/Entra ID authentication.
    /// Default: false (uses API key if provided).
    /// When true, ignores ApiKey and uses Azure Identity for authentication.
    /// </summary>
    [JsonPropertyName("useDefaultAzureCredential")]
    public bool UseDefaultAzureCredential { get; set; }
}
