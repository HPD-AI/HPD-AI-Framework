using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Google AI (Gemini) provider-specific configuration.
/// These options map directly to Google's GenerationConfig API.
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "google-ai",
///     "ModelName": "gemini-2.0-flash",
///     "ApiKey": "your-api-key",
///     "ProviderOptionsJson": "{\"maxOutputTokens\":8192,\"temperature\":0.7}"
///   }
/// }
/// </code>
/// </summary>
/// <seealso href="https://ai.google.dev/api/generate-content#generationconfig">See Official API Documentation</seealso>
public class GoogleAIProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// Maximum number of tokens to include in a response candidate.
    /// Maps to GenerationConfig.MaxOutputTokens.
    /// Note: The default value varies by model.
    /// </summary>
    [JsonPropertyName("maxOutputTokens")]
    public int? MaxOutputTokens { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// Controls the randomness of the output.
    /// Higher values (e.g., 1.0) make output more random, lower values (e.g., 0.0) make it more deterministic.
    /// Maps to GenerationConfig.Temperature.
    /// Note: The default value varies by model.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// Maximum cumulative probability of tokens to consider when sampling (nucleus sampling).
    /// The model uses combined Top-k and Top-p (nucleus) sampling.
    /// Ranges from 0.0 to 1.0.
    /// Maps to GenerationConfig.TopP.
    /// Note: The default value varies by model.
    /// </summary>
    [JsonPropertyName("topP")]
    public double? TopP { get; set; }

    /// <summary>
    /// Maximum number of tokens to consider when sampling.
    /// Gemini models use Top-p (nucleus) sampling or a combination of Top-k and nucleus sampling.
    /// Maps to GenerationConfig.TopK.
    /// Note: Not all models support TopK. An empty TopK attribute indicates the model doesn't allow setting TopK.
    /// </summary>
    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    /// <summary>
    /// Presence penalty applied to the next token's logprobs if the token has already been seen.
    /// This penalty is binary on/off and not dependent on the number of times the token is used.
    /// Positive values discourage reuse (increasing vocabulary), negative values encourage reuse (decreasing vocabulary).
    /// Maps to GenerationConfig.PresencePenalty.
    /// </summary>
    [JsonPropertyName("presencePenalty")]
    public double? PresencePenalty { get; set; }

    /// <summary>
    /// Frequency penalty applied to the next token's logprobs, multiplied by the number of times each token has been seen.
    /// Positive values discourage reuse proportional to usage, negative values encourage repetition.
    /// Maps to GenerationConfig.FrequencyPenalty.
    /// </summary>
    [JsonPropertyName("frequencyPenalty")]
    public double? FrequencyPenalty { get; set; }

    /// <summary>
    /// Character sequences (up to 5) that will stop output generation.
    /// If the model encounters one of these sequences, generation stops (sequence not included in response).
    /// Maps to GenerationConfig.StopSequences.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    //
    // DETERMINISM
    //

    /// <summary>
    /// Seed for deterministic generation. Setting a seed makes the model attempt to generate
    /// the same output for the same input (best-effort).
    /// Maps to GenerationConfig.Seed.
    /// </summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; set; }

    //
    // RESPONSE FORMAT
    //

    /// <summary>
    /// MIME type of the generated candidate text. Options:
    /// - "text/plain" (default): Text output
    /// - "application/json": JSON response
    /// - "text/x.enum": ENUM as string response
    /// Maps to GenerationConfig.ResponseMimeType.
    /// </summary>
    [JsonPropertyName("responseMimeType")]
    public string? ResponseMimeType { get; set; }

    /// <summary>
    /// Output schema of the generated candidate text as a JSON string.
    /// Must be a subset of OpenAPI schema (objects, primitives, or arrays).
    /// When set, ResponseMimeType must be "application/json".
    /// Maps to GenerationConfig.ResponseSchema.
    /// Example: "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}"
    /// </summary>
    [JsonPropertyName("responseSchema")]
    public string? ResponseSchema { get; set; }

    /// <summary>
    /// Alternative to ResponseSchema that accepts JSON Schema as a JSON string.
    /// If set, ResponseSchema must be omitted, but ResponseMimeType is required.
    /// Maps to GenerationConfig.ResponseJsonSchema.
    /// </summary>
    [JsonPropertyName("responseJsonSchema")]
    public string? ResponseJsonSchema { get; set; }

    /// <summary>
    /// Requested modalities of the response (e.g., text, image, audio).
    /// An empty list is equivalent to requesting only text.
    /// Maps to GenerationConfig.ResponseModalities.
    /// Example: ["TEXT", "IMAGE"]
    /// </summary>
    [JsonPropertyName("responseModalities")]
    public List<string>? ResponseModalities { get; set; }

    /// <summary>
    /// Number of generated responses to return.
    /// Currently, this value can only be set to 1. If unset, defaults to 1.
    /// Maps to GenerationConfig.CandidateCount.
    /// </summary>
    [JsonPropertyName("candidateCount")]
    public int? CandidateCount { get; set; }

    //
    // ADVANCED FEATURES
    //

    /// <summary>
    /// If true, export the logprobs results in response.
    /// Maps to GenerationConfig.ResponseLogprobs.
    /// </summary>
    [JsonPropertyName("responseLogprobs")]
    public bool? ResponseLogprobs { get; set; }

    /// <summary>
    /// Number of top logprobs to return at each decoding step.
    /// Only valid if ResponseLogprobs is true.
    /// Maps to GenerationConfig.Logprobs.
    /// </summary>
    [JsonPropertyName("logprobs")]
    public int? Logprobs { get; set; }

    /// <summary>
    /// Enables enhanced civic answers. May not be available for all models.
    /// Maps to GenerationConfig.EnableEnhancedCivicAnswers.
    /// </summary>
    [JsonPropertyName("enableEnhancedCivicAnswers")]
    public bool? EnableEnhancedCivicAnswers { get; set; }

    /// <summary>
    /// If enabled, the model will detect emotions and adapt its responses accordingly.
    /// Maps to GenerationConfig.EnableAffectiveDialog.
    /// </summary>
    [JsonPropertyName("enableAffectiveDialog")]
    public bool? EnableAffectiveDialog { get; set; }

    //
    // THINKING CONFIGURATION (Gemini 3+)
    //

    /// <summary>
    /// Indicates whether to include thoughts in the response (Gemini 3+ models).
    /// If true, thoughts are returned only if the model supports thought and thoughts are available.
    /// Maps to GenerationConfig.ThinkingConfig.IncludeThoughts.
    /// </summary>
    [JsonPropertyName("includeThoughts")]
    public bool? IncludeThoughts { get; set; }

    /// <summary>
    /// Thinking budget in tokens (Gemini 3+ models).
    /// Maps to GenerationConfig.ThinkingConfig.ThinkingBudget.
    /// </summary>
    [JsonPropertyName("thinkingBudget")]
    public int? ThinkingBudget { get; set; }

    /// <summary>
    /// Controls the maximum depth of the model's internal reasoning process (Gemini 3+ models).
    /// Options: "THINKING_LEVEL_UNSPECIFIED", "LOW", "HIGH"
    /// - LOW: Faster responses with less deep reasoning
    /// - HIGH: Deeper reasoning with potentially slower responses (recommended for complex tasks)
    /// Maps to GenerationConfig.ThinkingConfig.ThinkingLevel.
    /// </summary>
    [JsonPropertyName("thinkingLevel")]
    public string? ThinkingLevel { get; set; }

    //
    // MEDIA & IMAGE CONFIGURATION
    //

    /// <summary>
    /// Media resolution for input media.
    /// Options: "MEDIA_RESOLUTION_UNSPECIFIED", "MEDIA_RESOLUTION_LOW", "MEDIA_RESOLUTION_MEDIUM", "MEDIA_RESOLUTION_HIGH"
    /// Maps to GenerationConfig.MediaResolution.
    /// </summary>
    [JsonPropertyName("mediaResolution")]
    public string? MediaResolution { get; set; }

    /// <summary>
    /// If enabled, audio timestamp will be included in the request to the model.
    /// Maps to GenerationConfig.AudioTimestamp.
    /// </summary>
    [JsonPropertyName("audioTimestamp")]
    public bool? AudioTimestamp { get; set; }

    /// <summary>
    /// Aspect ratio for generated images (image generation models only).
    /// Common values: "1:1" (square), "16:9" (landscape), "9:16" (portrait), "4:3", "3:4"
    /// Maps to GenerationConfig.ImageConfig.AspectRatio.
    /// </summary>
    [JsonPropertyName("imageAspectRatio")]
    public string? ImageAspectRatio { get; set; }

    /// <summary>
    /// Output resolution for generated images (image generation models only).
    /// Options: "1K", "2K", "4K"
    /// Only supported on certain models like gemini-3-pro-image-preview.
    /// Maps to GenerationConfig.ImageConfig.ImageSize.
    /// </summary>
    [JsonPropertyName("imageSize")]
    public string? ImageSize { get; set; }

    /// <summary>
    /// Image output MIME type for generated images (image generation models only).
    /// Options: "image/png" (default), "image/jpeg"
    /// Maps to GenerationConfig.ImageConfig.ImageOutputOptions.MimeType.
    /// </summary>
    [JsonPropertyName("imageOutputMimeType")]
    public string? ImageOutputMimeType { get; set; }

    /// <summary>
    /// Compression quality for JPEG images (0-100, default: 75).
    /// Only applicable when ImageOutputMimeType is "image/jpeg".
    /// Maps to GenerationConfig.ImageConfig.ImageOutputOptions.CompressionQuality.
    /// </summary>
    [JsonPropertyName("imageCompressionQuality")]
    public int? ImageCompressionQuality { get; set; }

    //
    // ROUTING CONFIGURATION
    //

    /// <summary>
    /// Model routing preference for automated routing.
    /// Options: "UNKNOWN", "PRIORITIZE_QUALITY", "BALANCED", "PRIORITIZE_COST"
    /// Maps to GenerationConfig.RoutingConfig.AutoMode.ModelRoutingPreference.
    /// </summary>
    [JsonPropertyName("modelRoutingPreference")]
    public string? ModelRoutingPreference { get; set; }

    /// <summary>
    /// Specific model name for manual routing (e.g., "gemini-1.5-pro-001").
    /// Only public LLM models are accepted.
    /// Maps to GenerationConfig.RoutingConfig.ManualMode.ModelName.
    /// </summary>
    [JsonPropertyName("manualRoutingModelName")]
    public string? ManualRoutingModelName { get; set; }

    //
    // SAFETY SETTINGS
    //

    /// <summary>
    /// Safety settings to control content filtering.
    /// List of safety settings with category and threshold pairs.
    /// Example: [{"category":"HARM_CATEGORY_HARASSMENT","threshold":"BLOCK_MEDIUM_AND_ABOVE"}]
    /// </summary>
    [JsonPropertyName("safetySettings")]
    public List<SafetySettingConfig>? SafetySettings { get; set; }

    //
    // FUNCTION CALLING CONFIGURATION
    //

    /// <summary>
    /// Function calling mode. Options:
    /// - "AUTO" (default): Model decides to predict either a function call or natural language response
    /// - "ANY": Model must predict a function call (limited by AllowedFunctionNames if set)
    /// - "NONE": Model will not predict any function call
    /// Maps to ToolConfig.FunctionCallingConfig.Mode.
    /// </summary>
    [JsonPropertyName("functionCallingMode")]
    public string? FunctionCallingMode { get; set; }

    /// <summary>
    /// Set of function names that limits which functions the model will call.
    /// Should only be set when FunctionCallingMode is "ANY".
    /// Maps to ToolConfig.FunctionCallingConfig.AllowedFunctionNames.
    /// </summary>
    [JsonPropertyName("allowedFunctionNames")]
    public List<string>? AllowedFunctionNames { get; set; }

    //
    // ADDITIONAL OPTIONS
    //

    /// <summary>
    /// Additional custom parameters for model-specific features not covered by standard options.
    /// </summary>
    [JsonPropertyName("additionalProperties")]
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}

/// <summary>
/// Safety setting configuration for content filtering.
/// </summary>
public class SafetySettingConfig
{
    /// <summary>
    /// The harm category to configure.
    /// Options: HARM_CATEGORY_HARASSMENT, HARM_CATEGORY_HATE_SPEECH, HARM_CATEGORY_SEXUALLY_EXPLICIT,
    /// HARM_CATEGORY_DANGEROUS_CONTENT, HARM_CATEGORY_CIVIC_INTEGRITY
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// The blocking threshold for this category.
    /// Options: BLOCK_NONE, BLOCK_ONLY_HIGH, BLOCK_MEDIUM_AND_ABOVE, BLOCK_LOW_AND_ABOVE, OFF
    /// </summary>
    [JsonPropertyName("threshold")]
    public string? Threshold { get; set; }

    /// <summary>
    /// Harm block method (probability or probability and severity scores).
    /// Options: HARM_BLOCK_METHOD_UNSPECIFIED, SEVERITY, PROBABILITY
    /// </summary>
    [JsonPropertyName("method")]
    public string? Method { get; set; }
}
