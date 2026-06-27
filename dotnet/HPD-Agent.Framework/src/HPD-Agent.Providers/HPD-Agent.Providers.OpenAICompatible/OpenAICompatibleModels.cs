using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OpenAICompatible;

public sealed class OpenAICompatibleChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAICompatibleRequestMessage> Messages { get; set; } = [];

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

    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    [JsonPropertyName("tools")]
    public List<OpenAICompatibleRequestTool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    public JsonElement? ToolChoice { get; set; }

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; set; }

    [JsonPropertyName("reasoning_effort")]
    public string? ReasoningEffort { get; set; }

    [JsonPropertyName("response_format")]
    public OpenAICompatibleResponseFormatRequest? ResponseFormat { get; set; }

    [JsonPropertyName("stream_options")]
    public OpenAICompatibleStreamOptions? StreamOptions { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class OpenAICompatibleRequestMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAICompatibleRequestToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }
}

public sealed class OpenAICompatibleRequestToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAICompatibleRequestFunction Function { get; set; } = new();
}

public sealed class OpenAICompatibleRequestFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

public sealed class OpenAICompatibleRequestTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAICompatibleRequestToolFunction Function { get; set; } = new();
}

public sealed class OpenAICompatibleRequestToolFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; }
}

public sealed class OpenAICompatibleRequestContentPart
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    public OpenAICompatibleImageUrlContentPart? ImageUrl { get; set; }
}

public sealed class OpenAICompatibleImageUrlContentPart
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

public sealed class OpenAICompatibleResponseFormatRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("json_schema")]
    public OpenAICompatibleJsonSchemaResponseFormat? JsonSchema { get; set; }
}

public sealed class OpenAICompatibleJsonSchemaResponseFormat
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "response";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("schema")]
    public JsonElement Schema { get; set; }

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; }
}

public sealed class OpenAICompatibleStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool? IncludeUsage { get; set; }
}

public sealed class OpenAICompatibleChatResponseEnvelope
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAICompatibleChoice> Choices { get; set; } = [];

    [JsonPropertyName("usage")]
    public OpenAICompatibleUsage? Usage { get; set; }

    [JsonPropertyName("citations")]
    public List<string>? Citations { get; set; }

    [JsonPropertyName("search_results")]
    public List<OpenAICompatibleSearchResult>? SearchResults { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; set; }
}

public sealed class OpenAICompatibleChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAICompatibleResponseMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class OpenAICompatibleResponseMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAICompatibleResponseToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("refusal")]
    public string? Refusal { get; set; }

    [JsonPropertyName("annotations")]
    public List<OpenAICompatibleAnnotation>? Annotations { get; set; }
}

public sealed class OpenAICompatibleResponseToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public OpenAICompatibleResponseFunctionCall? Function { get; set; }
}

public sealed class OpenAICompatibleResponseFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

public sealed class OpenAICompatibleUsage
{
    [JsonPropertyName("prompt_tokens")]
    public long? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public long? CompletionTokens { get; set; }

    [JsonPropertyName("input_tokens")]
    public long? InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public long? OutputTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public long? TotalTokens { get; set; }

    [JsonPropertyName("prompt_tokens_details")]
    public OpenAICompatibleTokenUsageDetails? PromptTokenDetails { get; set; }

    [JsonPropertyName("completion_tokens_details")]
    public OpenAICompatibleTokenUsageDetails? CompletionTokenDetails { get; set; }

    [JsonPropertyName("input_tokens_details")]
    public OpenAICompatibleTokenUsageDetails? InputTokenDetails { get; set; }

    [JsonPropertyName("output_tokens_details")]
    public OpenAICompatibleTokenUsageDetails? OutputTokenDetails { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraCounts { get; set; }
}

public sealed class OpenAICompatibleTokenUsageDetails
{
    [JsonPropertyName("cached_tokens")]
    public long? CachedTokens { get; set; }

    [JsonPropertyName("reasoning_tokens")]
    public long? ReasoningTokens { get; set; }

    [JsonPropertyName("audio_tokens")]
    public long? AudioTokens { get; set; }

    [JsonPropertyName("accepted_prediction_tokens")]
    public long? AcceptedPredictionTokens { get; set; }

    [JsonPropertyName("rejected_prediction_tokens")]
    public long? RejectedPredictionTokens { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraCounts { get; set; }
}

public sealed class OpenAICompatibleStreamingResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAICompatibleStreamingChoice> Choices { get; set; } = [];

    [JsonPropertyName("usage")]
    public OpenAICompatibleUsage? Usage { get; set; }

    [JsonPropertyName("error")]
    public OpenAICompatibleError? Error { get; set; }
}

public sealed class OpenAICompatibleStreamingChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public OpenAICompatibleDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class OpenAICompatibleDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAICompatibleToolCallDelta>? ToolCalls { get; set; }
}

public sealed class OpenAICompatibleToolCallDelta
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("function")]
    public OpenAICompatibleFunctionCallDelta? Function { get; set; }
}

public sealed class OpenAICompatibleFunctionCallDelta
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

public sealed class OpenAICompatibleError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public JsonElement? Code { get; set; }
}

public sealed class OpenAICompatibleAnnotation
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url_citation")]
    public OpenAICompatibleUrlCitation? UrlCitation { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class OpenAICompatibleUrlCitation
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("start_index")]
    public int? StartIndex { get; set; }

    [JsonPropertyName("end_index")]
    public int? EndIndex { get; set; }
}

public sealed class OpenAICompatibleSearchResult
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("snippet")]
    public string? Snippet { get; set; }
}
