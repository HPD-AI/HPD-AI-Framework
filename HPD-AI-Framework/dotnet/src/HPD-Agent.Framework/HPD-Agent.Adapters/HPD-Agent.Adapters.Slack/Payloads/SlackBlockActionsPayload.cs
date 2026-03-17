using System.Text.Json.Serialization;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Slack.Payloads;

// Arrives as form-urlencoded with a JSON `payload` field.
// The generator detects Content-Type and routes accordingly.
[WebhookPayload]
public record SlackBlockActionsPayload(
    [property: JsonPropertyName("type")]         string Type,
    [property: JsonPropertyName("trigger_id")]   string TriggerId,
    [property: JsonPropertyName("user")]         SlackUser User,
    [property: JsonPropertyName("channel")]      SlackChannelRef? Channel,
    [property: JsonPropertyName("message")]      SlackMessageRef? Message,
    [property: JsonPropertyName("container")]    SlackContainer? Container,
    [property: JsonPropertyName("response_url")] string? ResponseUrl,
    [property: JsonPropertyName("actions")]      SlackAction[] Actions
);

public record SlackUser(
    [property: JsonPropertyName("id")]       string Id,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("name")]     string? Name,
    [property: JsonPropertyName("team_id")]  string? TeamId
);

public record SlackChannelRef(
    [property: JsonPropertyName("id")]   string Id,
    [property: JsonPropertyName("name")] string? Name
);

public record SlackMessageRef(
    [property: JsonPropertyName("ts")]   string Ts,
    [property: JsonPropertyName("text")] string? Text
);

public record SlackContainer(
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("message_ts")]  string? MessageTs,
    [property: JsonPropertyName("channel_id")]  string? ChannelId
);

public record SlackAction(
    [property: JsonPropertyName("action_id")]   string ActionId,
    [property: JsonPropertyName("block_id")]    string BlockId,
    [property: JsonPropertyName("type")]        string Type,
    [property: JsonPropertyName("value")]       string? Value,
    [property: JsonPropertyName("action_ts")]   string ActionTs,
    [property: JsonPropertyName("selected_option")] SlackSelectedOption? SelectedOption = null
);

public record SlackSelectedOption(
    [property: JsonPropertyName("text")]  SlackTextObject Text,
    [property: JsonPropertyName("value")] string Value
);

