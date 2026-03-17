using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.Payloads;

/// <summary>
/// Slash command payload. Arrives as form-urlencoded — NOT inside an <c>event_callback</c> envelope.
/// The generator detects form-urlencoded bodies with a <c>command</c> field and routes here.
/// </summary>
[WebhookPayload]
public record SlackSlashCommandPayload(
    [property: JsonPropertyName("command")]      string Command,     // e.g. "/ask"
    [property: JsonPropertyName("text")]         string Text,        // everything after the command
    [property: JsonPropertyName("user_id")]      string UserId,
    [property: JsonPropertyName("channel_id")]   string ChannelId,
    [property: JsonPropertyName("team_id")]      string? TeamId,
    [property: JsonPropertyName("trigger_id")]   string TriggerId,   // valid for 3s — use for modals
    [property: JsonPropertyName("response_url")] string ResponseUrl  // valid for 30min
);
