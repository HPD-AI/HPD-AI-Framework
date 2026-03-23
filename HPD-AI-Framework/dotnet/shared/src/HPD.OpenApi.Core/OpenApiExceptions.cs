using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.OpenApi.Core;

/// <summary>
/// Structured response returned by <see cref="OpenApiOperationRunner"/> on success.
///
/// Mirrors SK's RestApiOperationResponse pattern: carries the raw response content
/// alongside the <see cref="ExpectedSchema"/> from the OpenAPI spec for the matching
/// status code. The schema is surfaced to the LLM so it can reason about truncated
/// or sparse responses ("there are more fields than what was returned").
///
/// <see cref="ResponseOptimizationMiddleware"/> processes <see cref="Content"/> for
/// field filtering / truncation, then serializes the whole object to JSON as the
/// final function result the LLM sees.
/// </summary>
public sealed class OpenApiOperationResponse
{
    /// <summary>The response body, as parsed JSON or raw string.</summary>
    [JsonPropertyName("content")]
    public object? Content { get; set; }

    /// <summary>HTTP status code of the response.</summary>
    [JsonPropertyName("status")]
    public int StatusCode { get; init; }

    /// <summary>
    /// Expected response schema from the OpenAPI spec for the returned status code.
    /// Null when the spec defines no response schema for the returned status code.
    /// </summary>
    [JsonPropertyName("expectedSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? ExpectedSchema { get; init; }
}

/// <summary>
/// Thrown when OpenAPI spec parsing fails due to structural or validation errors.
/// </summary>
public sealed class OpenApiParseException : Exception
{
    public OpenApiParseException(string message) : base(message) { }
    public OpenApiParseException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Structured error response from an OpenAPI operation execution.
/// Returned by <see cref="OpenApiOperationRunner"/> when an HTTP request returns a non-success
/// status code, or when an <see cref="OpenApiCoreConfig.ErrorDetector"/> callback identifies
/// a logical error in a 200 response.
///
/// The core returns this as data (not an exception). Consumer bridging layers decide how to handle it:
/// - HPD-Agent.OpenApi: retryable errors (429, 5xx, 401) → throw <see cref="OpenApiRequestException"/>
///   so FunctionRetryMiddleware catches and retries; client errors (400, 404, 422) → return to LLM
///   as function result for self-correction
/// - HPD.Integrations.Http: always throw <see cref="OpenApiRequestException"/> → RetryPolicy handles
///
/// <see cref="UserMessage"/> extracts a human-readable message from common JSON error patterns:
/// Stripe ("error.message"), GitHub ("message"), Slack ("error"), etc.
/// </summary>
public sealed class OpenApiErrorResponse
{
    public bool Error { get; init; } = true;
    public int StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }
    public string? Body { get; init; }

    /// <summary>
    /// Lazily-extracted human-readable error message from the response body.
    /// Recursively searches common API error patterns in JSON responses.
    /// Returns raw Body (truncated to 200 chars) if no structured message is found.
    /// </summary>
    public string? UserMessage => _userMessage ??= ExtractUserMessage(Body);
    private string? _userMessage;

    /// <summary>
    /// Retry-After delay extracted from HTTP response headers.
    /// Set by <see cref="OpenApiOperationRunner"/> when the response includes a Retry-After header.
    /// Consumers use this to respect provider-specified rate limit delays.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    public override string ToString() =>
        UserMessage ?? $"HTTP {StatusCode} {ReasonPhrase}: {Body}";

    private static readonly string[] s_messageKeys =
        ["message", "Message", "error", "detail", "description",
         "error_description", "error_message", "reason", "msg"];

    private static readonly string[] s_nestedObjectKeys =
        ["error", "Error", "data", "body", "response"];

    private static string? ExtractUserMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return ExtractFromElement(doc.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return body.Length > 200 ? body[..200] + "..." : body;
        }
    }

    private static string? ExtractFromElement(JsonElement element, int depth)
    {
        if (depth > 3) return null;
        if (element.ValueKind == JsonValueKind.String) return element.GetString();
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var key in s_messageKeys)
        {
            if (element.TryGetProperty(key, out var value))
            {
                if (value.ValueKind == JsonValueKind.String) return value.GetString();
                if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0)
                {
                    var first = value[0];
                    if (first.ValueKind == JsonValueKind.String) return first.GetString();
                    var nested = ExtractFromElement(first, depth + 1);
                    if (nested != null) return nested;
                }
            }
        }

        foreach (var key in s_nestedObjectKeys)
        {
            if (element.TryGetProperty(key, out var nested)
                && nested.ValueKind == JsonValueKind.Object)
            {
                var result = ExtractFromElement(nested, depth + 1);
                if (result != null) return result;
            }
        }

        return null;
    }
}

/// <summary>
/// Exception wrapping an <see cref="OpenApiErrorResponse"/> for integration with retry infrastructure.
///
/// <see cref="OpenApiOperationRunner"/> returns errors as data (<see cref="OpenApiErrorResponse"/>).
/// Consumer bridging layers throw this exception when the error is actionable by retry infrastructure.
///
/// HPD-Agent.OpenApi: throws for 429, 5xx, 401 → FunctionRetryMiddleware catches via
/// OpenApiErrorHandler. Returns 400, 404, 422 as data to the LLM for self-correction.
/// HPD.Integrations.Http: throws for all errors → RetryPolicy handles.
/// </summary>
public sealed class OpenApiRequestException : Exception
{
    public OpenApiErrorResponse ErrorResponse { get; }
    public int StatusCode => ErrorResponse.StatusCode;
    public TimeSpan? RetryAfter => ErrorResponse.RetryAfter;

    public OpenApiRequestException(OpenApiErrorResponse errorResponse)
        : base(errorResponse.UserMessage ?? errorResponse.ToString())
    {
        ErrorResponse = errorResponse;
    }

    public OpenApiRequestException(OpenApiErrorResponse errorResponse, Exception inner)
        : base(errorResponse.UserMessage ?? errorResponse.ToString(), inner)
    {
        ErrorResponse = errorResponse;
    }
}
