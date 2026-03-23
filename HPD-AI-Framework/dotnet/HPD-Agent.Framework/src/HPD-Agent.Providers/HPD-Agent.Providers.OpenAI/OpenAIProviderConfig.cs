using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// OpenAI-specific provider configuration options.
/// These options map to CreateResponseOptions in the OpenAI .NET SDK (Responses API).
///
/// JSON Example:
/// <code>
/// {
///   "Provider": {
///     "ProviderKey": "openai",
///     "ModelName": "gpt-4o",
///     "ApiKey": "sk-...",
///     "ProviderOptionsJson": "{\"maxOutputTokenCount\":4096,\"temperature\":0.7}"
///   }
/// }
/// </code>
/// </summary>
public class OpenAIProviderConfig
{
    //
    // CORE PARAMETERS
    //

    /// <summary>
    /// An upper bound for the number of tokens that can be generated for a completion,
    /// including visible output tokens and, on applicable models, reasoning tokens.
    /// Maps to CreateResponseOptions.MaxOutputTokenCount.
    /// </summary>
    [JsonPropertyName("maxOutputTokenCount")]
    public int? MaxOutputTokenCount { get; set; }

    //
    // SAMPLING PARAMETERS
    //

    /// <summary>
    /// Amount of randomness injected into the response.
    /// Ranges from 0.0 to 2.0. Higher values increase creativity.
    /// Use temperature closer to 0.0 for analytical tasks, and closer to 1.0+ for creative tasks.
    /// Maps to CreateResponseOptions.Temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>
    /// Use nucleus sampling (Top-P). Compute the cumulative distribution over all options
    /// for each subsequent token in decreasing probability order and cut it off
    /// once it reaches a particular probability specified by top_p.
    /// Ranges from 0.0 to 1.0.
    /// You should either alter temperature or topP, but not both.
    /// Maps to CreateResponseOptions.TopP.
    /// </summary>
    [JsonPropertyName("topP")]
    public float? TopP { get; set; }

    /// <summary>
    /// Frequency penalty reduces the likelihood of repeating the same token.
    /// Ranges from -2.0 to 2.0.
    /// Positive values penalize tokens based on their frequency in the text so far.
    /// Maps to CreateResponseOptions.FrequencyPenalty.
    /// </summary>
    [JsonPropertyName("frequencyPenalty")]
    public float? FrequencyPenalty { get; set; }

    /// <summary>
    /// Presence penalty reduces the likelihood of repeating any token that has appeared.
    /// Ranges from -2.0 to 2.0.
    /// Positive values penalize tokens that have already appeared in the text.
    /// Maps to CreateResponseOptions.PresencePenalty.
    /// </summary>
    [JsonPropertyName("presencePenalty")]
    public float? PresencePenalty { get; set; }

    /// <summary>
    /// Custom text sequences that will cause the model to stop generating.
    /// If the model encounters one of these sequences, generation stops.
    /// Up to 4 sequences allowed.
    /// Maps to CreateResponseOptions.StopSequences.
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
    /// Maps to CreateResponseOptions.Seed.
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
    /// Maps to CreateResponseOptions.ResponseFormat.
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
    /// Maps to CreateResponseOptions.ToolChoice.
    /// Note: Tools are defined at the request level, not in config.
    /// </summary>
    [JsonPropertyName("toolChoice")]
    public string? ToolChoice { get; set; }

    /// <summary>
    /// Whether to enable parallel function calling during tool use.
    /// Maps to CreateResponseOptions.AllowParallelToolCalls.
    /// Assumed true if not otherwise specified.
    /// </summary>
    [JsonPropertyName("allowParallelToolCalls")]
    public bool? AllowParallelToolCalls { get; set; }

    //
    // LOG PROBABILITIES
    //

    /// <summary>
    /// Whether to return log probabilities of the output tokens or not.
    /// If true, returns the log probabilities of each output token returned in the message content.
    /// Maps to CreateResponseOptions.IncludeLogProbabilities.
    /// </summary>
    [JsonPropertyName("includeLogProbabilities")]
    public bool? IncludeLogProbabilities { get; set; }

    /// <summary>
    /// An integer between 0 and 20 specifying the number of most likely tokens to return
    /// at each token position, each with an associated log probability.
    /// IncludeLogProbabilities must be set to true if this property is used.
    /// Maps to CreateResponseOptions.TopLogProbabilityCount.
    /// </summary>
    [JsonPropertyName("topLogProbabilityCount")]
    public int? TopLogProbabilityCount { get; set; }

    //
    // LOGIT BIASES
    //

    /// <summary>
    /// Modifies the likelihood of specified tokens appearing in the completion.
    /// Maps tokens (specified by their token ID in the tokenizer) to an associated bias value from -100 to 100.
    /// Mathematically, the bias is added to the logits generated by the model prior to sampling.
    /// Values between -1 and 1 should decrease or increase likelihood of selection;
    /// values like -100 or 100 should result in a ban or exclusive selection of the relevant token.
    /// Maps to CreateResponseOptions.LogitBiases.
    /// JSON format: { "tokenId": biasValue }
    /// </summary>
    [JsonPropertyName("logitBiases")]
    public Dictionary<int, int>? LogitBiases { get; set; }

    //
    // REASONING (O1 MODELS)
    //

    /// <summary>
    /// (o1 and newer reasoning models only) Constrains effort on reasoning for reasoning models.
    /// Currently supported values: "low", "medium", "high", "minimal".
    /// Reducing reasoning effort can result in faster responses and fewer tokens used on reasoning.
    /// Maps to CreateResponseOptions.ReasoningEffortLevel.
    /// </summary>
    [JsonPropertyName("reasoningEffortLevel")]
    public string? ReasoningEffortLevel { get; set; }

    //
    // AUDIO (GPT-4O-AUDIO-PREVIEW)
    //

    /// <summary>
    /// Specifies the content types that the model should generate in its responses.
    /// Options: "text", "audio", or both as a comma-separated string "text,audio".
    /// Most models can generate text by default.
    /// Some models like gpt-4o-audio-preview can also generate audio.
    /// Maps to CreateResponseOptions.ResponseModalities.
    /// </summary>
    [JsonPropertyName("responseModalities")]
    public string? ResponseModalities { get; set; }

    /// <summary>
    /// Audio output voice selection when audio modality is enabled.
    /// Options: "alloy", "ash", "ballad", "coral", "echo", "sage", "shimmer", "verse".
    /// Maps to CreateResponseOptions.AudioOptions.Voice.
    /// </summary>
    [JsonPropertyName("audioVoice")]
    public string? AudioVoice { get; set; }

    /// <summary>
    /// Audio output format when audio modality is enabled.
    /// Options: "wav", "mp3", "flac", "opus", "pcm16".
    /// Maps to CreateResponseOptions.AudioOptions.Format.
    /// </summary>
    [JsonPropertyName("audioFormat")]
    public string? AudioFormat { get; set; }

    //
    // SERVICE TIER
    //

    /// <summary>
    /// Configures the policy that the server will use to process the request
    /// in terms of pricing, performance, etc.
    /// Options: "auto" (default), "default".
    /// Maps to CreateResponseOptions.ServiceTier.
    /// </summary>
    [JsonPropertyName("serviceTier")]
    public string? ServiceTier { get; set; }

    //
    // USER TRACKING & SAFETY
    //

    /// <summary>
    /// A unique identifier representing your end-user, which can help OpenAI to monitor and detect abuse.
    /// Learn more: https://platform.openai.com/docs/guides/safety-best-practices/end-user-ids
    /// Maps to CreateResponseOptions.EndUserId.
    /// </summary>
    [JsonPropertyName("endUserId")]
    public string? EndUserId { get; set; }

    /// <summary>
    /// A stable identifier that can be used to help detect end-users of your application
    /// that may be violating OpenAI's usage policies.
    /// Maps to CreateResponseOptions.SafetyIdentifier.
    /// </summary>
    [JsonPropertyName("safetyIdentifier")]
    public string? SafetyIdentifier { get; set; }

    //
    // STORAGE & METADATA
    //

    /// <summary>
    /// Indicates whether to store the output of this chat completion request for use in
    /// model distillation or evals.
    /// Maps to CreateResponseOptions.StoredOutputEnabled.
    /// </summary>
    [JsonPropertyName("storedOutputEnabled")]
    public bool? StoredOutputEnabled { get; set; }

    /// <summary>
    /// Developer-defined tags and values used for filtering completions in the
    /// OpenAI Platform dashboard.
    /// Maps to CreateResponseOptions.Metadata.
    /// JSON format: { "key": "value" }
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    //
    // WEB SEARCH (EXPERIMENTAL)
    //

    /// <summary>
    /// Enable web search for the model. Set to true to enable.
    /// Maps to CreateResponseOptions.WebSearchOptions.
    /// </summary>
    [JsonPropertyName("webSearchEnabled")]
    public bool? WebSearchEnabled { get; set; }

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
