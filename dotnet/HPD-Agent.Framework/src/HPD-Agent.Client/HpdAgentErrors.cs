using System.Net;
using System.Text.Json;

namespace HPD.Agent.Client;

/// <summary>A non-success HTTP response from an HPD Agent endpoint.</summary>
public sealed class HpdAgentApiException : Exception
{
    public HpdAgentApiException(
        HttpStatusCode statusCode,
        string responseBody,
        IReadOnlyDictionary<string, string[]> headers)
        : base($"HPD Agent returned HTTP {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Headers = headers;
        Problem = HpdAgentProblemDetails.TryParse(responseBody);
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
    public IReadOnlyDictionary<string, string[]> Headers { get; }

    /// <summary>Gets the RFC 9457 details when the server returned a problem document.</summary>
    public HpdAgentProblemDetails? Problem { get; }

    /// <summary>A stable status-derived code, or the server problem type when present.</summary>
    public string Code => Problem?.Type ?? HpdAgentProblemDetails.CodeFor(StatusCode);

    /// <summary>Gets a human-readable title or detail suitable for diagnostics.</summary>
    public string? ProblemMessage => Problem?.Detail ?? Problem?.Title;

    /// <summary>Gets field validation messages, when present.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors => Problem?.Errors;
}

/// <summary>Structured details from an application/problem+json response.</summary>
public sealed record HpdAgentProblemDetails(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    string? Instance,
    IReadOnlyDictionary<string, string[]>? Errors,
    JsonElement? Extensions)
{
    internal static HpdAgentProblemDetails? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            var hasProblemMember =
                root.TryGetProperty("type", out _) ||
                root.TryGetProperty("title", out _) ||
                root.TryGetProperty("detail", out _) ||
                root.TryGetProperty("status", out _) ||
                root.TryGetProperty("errors", out _) ||
                root.TryGetProperty("error", out _) ||
                root.TryGetProperty("message", out _);
            if (!hasProblemMember)
                return null;

            var type = ReadString(root, "type") ?? ReadString(root, "code");
            var title = ReadString(root, "title") ?? ReadString(root, "error") ?? ReadString(root, "message");
            int? status = root.TryGetProperty("status", out var statusElement) &&
                          statusElement.TryGetInt32(out var parsedStatus)
                ? parsedStatus
                : null;
            var instance = ReadString(root, "instance");
            var errors = TryReadErrors(root);
            var extensions = root.Clone();
            return new HpdAgentProblemDetails(
                type,
                title,
                status,
                ReadString(root, "detail"),
                instance,
                errors,
                extensions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string CodeFor(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.BadRequest => "BAD_REQUEST",
            HttpStatusCode.Unauthorized => "UNAUTHORIZED",
            HttpStatusCode.Forbidden => "FORBIDDEN",
            HttpStatusCode.NotFound => "NOT_FOUND",
            HttpStatusCode.Conflict => "CONFLICT",
            HttpStatusCode.UnprocessableEntity => "VALIDATION_ERROR",
            HttpStatusCode.TooManyRequests => "RATE_LIMITED",
            HttpStatusCode.InternalServerError => "INTERNAL_SERVER_ERROR",
            HttpStatusCode.BadGateway => "BAD_GATEWAY",
            HttpStatusCode.ServiceUnavailable => "SERVICE_UNAVAILABLE",
            HttpStatusCode.GatewayTimeout => "GATEWAY_TIMEOUT",
            _ => "HTTP_ERROR"
        };

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyDictionary<string, string[]>? TryReadErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) ||
            errors.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var property in errors.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                result[property.Name] = property.Value.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString()!)
                    .ToArray();
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[property.Name] = [property.Value.GetString()!];
            }
        }
        return result.Count == 0 ? null : result;
    }
}

/// <summary>One non-destructive HTTP response for diagnostics or custom handling.</summary>
public sealed record HpdAgentRawResponse(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, string[]> Headers,
    string Body);
