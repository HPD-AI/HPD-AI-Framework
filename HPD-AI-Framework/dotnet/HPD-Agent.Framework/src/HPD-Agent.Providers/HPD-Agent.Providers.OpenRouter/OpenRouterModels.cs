using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OpenRouter;

/// <summary>
/// OpenRouter-specific response models for handling reasoning content
/// </summary>
internal class OpenRouterChatResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenRouterChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public OpenRouterUsage? Usage { get; set; }
}

internal class OpenRouterChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenRouterMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class OpenRouterMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_details")]
    public List<OpenRouterReasoningDetail>? ReasoningDetails { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenRouterToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("refusal")]
    public string? Refusal { get; set; }
}

internal class OpenRouterReasoningDetail
{
    [JsonPropertyName("type")]
    public string? Type { get; set; } // "reasoning.summary" | "reasoning.encrypted" | "reasoning.text"

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; } // "anthropic-claude-v1" | "openai-responses-v1"

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; } // encrypted data

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}

internal class OpenRouterToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public OpenRouterFunctionCall? Function { get; set; }
}

internal class OpenRouterFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

internal class OpenRouterUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

internal class OpenRouterError
{
    [JsonPropertyName("code")]
    public object? Code { get; set; } // Can be string or number

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

// Streaming response models
internal class OpenRouterStreamingResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenRouterStreamingChoice> Choices { get; set; } = new();

    [JsonPropertyName("error")]
    public OpenRouterError? Error { get; set; }

    [JsonPropertyName("usage")]
    public OpenRouterUsage? Usage { get; set; }
}

internal class OpenRouterStreamingChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public OpenRouterDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class OpenRouterDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_details")]
    public List<OpenRouterReasoningDetail>? ReasoningDetails { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenRouterToolCallDelta>? ToolCalls { get; set; }
}

internal class OpenRouterToolCallDelta
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public OpenRouterFunctionCallDelta? Function { get; set; }
}

internal class OpenRouterFunctionCallDelta
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

// Request models for sending messages to OpenRouter
internal class OpenRouterChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("models")]
    public List<string>? Models { get; set; }

    [JsonPropertyName("messages")]
    public List<OpenRouterRequestMessage> Messages { get; set; } = new();

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    [JsonPropertyName("frequency_penalty")]
    public float? FrequencyPenalty { get; set; }

    [JsonPropertyName("presence_penalty")]
    public float? PresencePenalty { get; set; }

    [JsonPropertyName("stop")]
    public List<string>? Stop { get; set; }

    [JsonPropertyName("tools")]
    public List<OpenRouterRequestTool>? Tools { get; set; }

    [JsonPropertyName("reasoning")]
    public OpenRouterReasoningConfig? Reasoning { get; set; }

    [JsonPropertyName("stream_options")]
    public OpenRouterStreamOptions? StreamOptions { get; set; }

    [JsonPropertyName("verbosity")]
    public string? Verbosity { get; set; } // "low", "medium", "high"

    [JsonPropertyName("min_p")]
    public float? MinP { get; set; }

    [JsonPropertyName("top_a")]
    public float? TopA { get; set; }

    [JsonPropertyName("repetition_penalty")]
    public float? RepetitionPenalty { get; set; }

    [JsonPropertyName("top_k")]
    public int? TopK { get; set; }

    [JsonPropertyName("seed")]
    public int? Seed { get; set; }

    [JsonPropertyName("logprobs")]
    public bool? Logprobs { get; set; }

    [JsonPropertyName("top_logprobs")]
    public int? TopLogprobs { get; set; }

    [JsonPropertyName("Toolkits")]
    public List<OpenRouterToolkit>? Toolkits { get; set; }

    [JsonPropertyName("provider")]
    public OpenRouterProviderPreferences? Provider { get; set; }
}

internal class OpenRouterReasoningConfig
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("effort")]
    public string? Effort { get; set; } // "xhigh", "high", "medium", "low", "minimal", "none"

    [JsonPropertyName("summary")]
    public string? Summary { get; set; } // "detailed", "concise", "auto"

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("exclude")]
    public bool? Exclude { get; set; }
}

internal class OpenRouterStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool? IncludeUsage { get; set; }
}

internal class OpenRouterRequestMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public object? Content { get; set; } // Can be string (simple text) or array (multimodal content)

    [JsonPropertyName("tool_calls")]
    public List<OpenRouterRequestToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }

    [JsonPropertyName("reasoning_details")]
    public List<OpenRouterReasoningDetail>? ReasoningDetails { get; set; }
}

internal class OpenRouterRequestToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenRouterRequestFunction Function { get; set; } = new();
}

internal class OpenRouterRequestFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;
}

internal class OpenRouterRequestTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenRouterRequestToolFunction Function { get; set; } = new();
}

internal class OpenRouterRequestToolFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

internal class OpenRouterToolkit
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("pdf")]
    public OpenRouterPdfConfig? Pdf { get; set; }
}

internal class OpenRouterPdfConfig
{
    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "mistral-ocr"; // "mistral-ocr", "pdf-text", "native"
}

internal class OpenRouterProviderPreferences
{
    [JsonPropertyName("order")]
    public List<string>? Order { get; set; }

    [JsonPropertyName("allow_fallbacks")]
    public bool? AllowFallbacks { get; set; }

    [JsonPropertyName("require_parameters")]
    public bool? RequireParameters { get; set; }

    [JsonPropertyName("data_collection")]
    public string? DataCollection { get; set; } // "allow" | "deny"

    [JsonPropertyName("zdr")]
    public bool? Zdr { get; set; }

    [JsonPropertyName("enforce_distillable_text")]
    public bool? EnforceDistillableText { get; set; }

    [JsonPropertyName("only")]
    public List<string>? Only { get; set; }

    [JsonPropertyName("ignore")]
    public List<string>? Ignore { get; set; }

    [JsonPropertyName("quantizations")]
    public List<string>? Quantizations { get; set; }

    [JsonPropertyName("sort")]
    public string? Sort { get; set; } // "price" | "throughput" | "latency"

    [JsonPropertyName("max_price")]
    public OpenRouterMaxPrice? MaxPrice { get; set; }
}

internal class OpenRouterMaxPrice
{
    [JsonPropertyName("prompt")]
    public float? Prompt { get; set; }

    [JsonPropertyName("completion")]
    public float? Completion { get; set; }

    [JsonPropertyName("request")]
    public float? Request { get; set; }

    [JsonPropertyName("image")]
    public float? Image { get; set; }
}

internal class OpenRouterContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "text", "image_url", "file", "input_audio", "input_video"

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    public OpenRouterImageUrl? ImageUrl { get; set; }

    [JsonPropertyName("file")]
    public OpenRouterFile? File { get; set; }

    [JsonPropertyName("input_audio")]
    public OpenRouterInputAudio? InputAudio { get; set; }

    [JsonPropertyName("video_url")]
    public OpenRouterVideoUrl? VideoUrl { get; set; }

    [JsonPropertyName("cache_control")]
    public OpenRouterCacheControl? CacheControl { get; set; }
}

internal class OpenRouterImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

internal class OpenRouterFile
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("file_data")]
    public string FileData { get; set; } = string.Empty; // base64 data URI
}

internal class OpenRouterInputAudio
{
    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty; // base64 audio data

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty; // "wav", "mp3", etc.
}

internal class OpenRouterVideoUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

internal class OpenRouterCacheControl
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ephemeral"; // Currently only "ephemeral" supported
}

internal class OpenRouterKeyInfo
{
    [JsonPropertyName("data")]
    public OpenRouterKeyData Data { get; set; } = new();
}

internal class OpenRouterKeyData
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("limit")]
    public float? Limit { get; set; } // Credit limit for the key, or null if unlimited

    [JsonPropertyName("limit_reset")]
    public string? LimitReset { get; set; } // Type of limit reset for the key, or null if never resets

    [JsonPropertyName("limit_remaining")]
    public float? LimitRemaining { get; set; } // Remaining credits for the key, or null if unlimited

    [JsonPropertyName("include_byok_in_limit")]
    public bool IncludeByokInLimit { get; set; } // Whether to include external BYOK usage in the credit limit

    [JsonPropertyName("usage")]
    public float Usage { get; set; } // Number of credits used (all time)

    [JsonPropertyName("usage_daily")]
    public float UsageDaily { get; set; } // Number of credits used (current UTC day)

    [JsonPropertyName("usage_weekly")]
    public float UsageWeekly { get; set; } // ... (current UTC week, starting Monday)

    [JsonPropertyName("usage_monthly")]
    public float UsageMonthly { get; set; } // ... (current UTC month)

    [JsonPropertyName("byok_usage")]
    public float ByokUsage { get; set; } // Same for external BYOK usage

    [JsonPropertyName("byok_usage_daily")]
    public float ByokUsageDaily { get; set; }

    [JsonPropertyName("byok_usage_weekly")]
    public float ByokUsageWeekly { get; set; }

    [JsonPropertyName("byok_usage_monthly")]
    public float ByokUsageMonthly { get; set; }

    [JsonPropertyName("is_free_tier")]
    public bool IsFreeTier { get; set; } // Whether the user has paid for credits before
}
