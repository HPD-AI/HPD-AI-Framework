using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.Payloads;

[WebhookPayload]
public record SlackReactionEvent(
    [property: JsonPropertyName("type")]      string Type,
    [property: JsonPropertyName("user")]      string User,
    [property: JsonPropertyName("reaction")]  string Reaction,
    [property: JsonPropertyName("item")]      SlackReactionItem Item,
    [property: JsonPropertyName("event_ts")]  string EventTs
);

public record SlackReactionItem(
    [property: JsonPropertyName("type")]    string Type,
    [property: JsonPropertyName("channel")] string? Channel,
    [property: JsonPropertyName("ts")]      string? Ts
);
