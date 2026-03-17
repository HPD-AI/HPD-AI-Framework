using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.SocketMode;

/// <summary>
/// Envelope received over a Slack Socket Mode WebSocket connection.
/// Every frame from Slack's socket is one of these.
/// </summary>
public record SlackSocketEnvelope(
    [property: JsonPropertyName("envelope_id")] string EnvelopeId,
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("payload")]     JsonElement? Payload,
    [property: JsonPropertyName("retry_attempt")] int? RetryAttempt,
    [property: JsonPropertyName("retry_reason")]  string? RetryReason
);
