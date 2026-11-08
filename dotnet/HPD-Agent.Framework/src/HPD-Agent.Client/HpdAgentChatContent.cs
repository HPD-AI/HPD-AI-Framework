using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Client;

/// <summary>AOT-safe builders for the common Microsoft.Extensions.AI wire shapes.</summary>
public static class HpdAgentChatContent
{
    public static JsonElement Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("$type", "text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public static JsonElement UserMessage(string text) =>
        UserMessage([Text(text)]);

    public static JsonElement UserMessage(IEnumerable<JsonElement> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("contents");
            writer.WriteStartArray();
            foreach (var content in contents)
                content.WriteTo(writer);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>Creates a stored-content AIContent reference scoped to a session.</summary>
    public static JsonElement StoredContentReference(
        string sessionId,
        string contentId,
        string version,
        string mediaType,
        string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("$type", "hpd:stored-content-reference");
            writer.WritePropertyName("address");
            writer.WriteStartObject();
            writer.WritePropertyName("scope");
            writer.WriteStartObject();
            writer.WriteString("value", sessionId);
            writer.WriteEndObject();
            writer.WriteString("contentId", contentId);
            writer.WriteString("version", version);
            writer.WriteEndObject();
            writer.WriteString("mediaType", mediaType);
            if (name is not null)
                writer.WriteString("name", name);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}

/// <summary>Common fields from an accepted or rejected agent-input submission.</summary>
public sealed class HpdAgentInputSubmissionResult
{
    [JsonPropertyName("disposition")]
    public string? Disposition { get; init; }

    [JsonPropertyName("threadExecutionId")]
    public string? ThreadExecutionId { get; init; }

    [JsonPropertyName("startedAt")]
    public string? StartedAt { get; init; }

    [JsonPropertyName("activeExecution")]
    public JsonElement? ActiveExecution { get; init; }

    public bool IsQueued =>
        string.Equals(Disposition, "queued", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates a convenience result for local short-circuit decisions.</summary>
    public static HpdAgentInputSubmissionResult NoActiveExecution() =>
        new() { Disposition = "no_active_execution" };
}
