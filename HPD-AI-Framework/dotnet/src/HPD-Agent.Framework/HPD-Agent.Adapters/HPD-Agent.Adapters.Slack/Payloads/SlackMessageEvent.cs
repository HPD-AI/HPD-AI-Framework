using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.Payloads;

[WebhookPayload]
public record SlackMessageEvent(
    [property: JsonPropertyName("type")]         string Type,
    [property: JsonPropertyName("user")]         string? User,
    [property: JsonPropertyName("bot_id")]       string? BotId,
    [property: JsonPropertyName("username")]     string? Username,
    [property: JsonPropertyName("subtype")]      string? Subtype,
    [property: JsonPropertyName("text")]         string? Text,
    [property: JsonPropertyName("ts")]           string? Ts,
    [property: JsonPropertyName("thread_ts")]    string? ThreadTs,
    [property: JsonPropertyName("channel")]      string? Channel,
    [property: JsonPropertyName("channel_type")] string? ChannelType,
    [property: JsonPropertyName("edited")]       SlackEditedInfo? Edited,
    [property: JsonPropertyName("files")]        SlackFileInfo[]? Files
);

public record SlackEditedInfo(
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("ts")]   string Ts
);

public record SlackFileInfo(
    [property: JsonPropertyName("id")]        string Id,
    [property: JsonPropertyName("name")]      string? Name,
    [property: JsonPropertyName("mimetype")]  string? MimeType,
    [property: JsonPropertyName("url_private_download")] string? UrlPrivateDownload,
    [property: JsonPropertyName("size")]      long? Size
);
