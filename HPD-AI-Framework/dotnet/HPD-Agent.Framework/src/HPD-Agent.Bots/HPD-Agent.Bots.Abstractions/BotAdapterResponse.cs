using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.Bots;

/// <summary>
/// Transport-neutral immediate response from a bot adapter dispatch.
/// </summary>
public sealed record BotAdapterResponse
{
    /// <summary>Transport status code. For HTTP bridges this becomes the HTTP status code.</summary>
    public int StatusCode { get; init; } = 200;

    /// <summary>Optional response content type.</summary>
    public string? ContentType { get; init; }

    /// <summary>Optional response body.</summary>
    public byte[]? Body { get; init; }

    /// <summary>Optional response headers.</summary>
    public IReadOnlyDictionary<string, string[]> Headers { get; init; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a 200 OK response without a body.</summary>
    public static BotAdapterResponse Ok() => new();

    /// <summary>Returns a status-only response.</summary>
    public static BotAdapterResponse Status(int statusCode) => new() { StatusCode = statusCode };

    /// <summary>Returns a plain text response.</summary>
    public static BotAdapterResponse Text(string text, string contentType = "text/plain", int statusCode = 200)
        => new()
        {
            StatusCode = statusCode,
            ContentType = contentType,
            Body = Encoding.UTF8.GetBytes(text),
        };

    /// <summary>Returns a JSON response serialized with the provided source-generated type info.</summary>
    public static BotAdapterResponse Json<T>(
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        int statusCode = 200)
        => new()
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Body = JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo),
        };
}
