using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.Payloads;

/// <summary>
/// Outer envelope for all Slack <c>event_callback</c> payloads.
/// The inner <c>event</c> field is kept as <see cref="JsonElement"/> so each handler
/// can deserialize it to the specific event type it expects.
/// </summary>
[WebhookPayload]
public record SlackEventEnvelope(
    [property: JsonPropertyName("type")]      string Type,
    [property: JsonPropertyName("team_id")]   string? TeamId,
    [property: JsonPropertyName("event_id")]  string? EventId,
    [property: JsonPropertyName("event")]     JsonElement? Event,
    [property: JsonPropertyName("challenge")] string? Challenge
);
