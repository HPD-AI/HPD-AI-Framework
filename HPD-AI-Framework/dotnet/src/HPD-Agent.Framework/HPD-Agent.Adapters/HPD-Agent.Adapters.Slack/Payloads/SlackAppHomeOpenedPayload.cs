using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.Payloads;

[WebhookPayload]
public record SlackAppHomeOpenedPayload(
    [property: JsonPropertyName("type")]     string Type,
    [property: JsonPropertyName("user")]     string User,
    [property: JsonPropertyName("channel")]  string Channel,
    [property: JsonPropertyName("tab")]      string Tab,
    [property: JsonPropertyName("event_ts")] string EventTs
);
