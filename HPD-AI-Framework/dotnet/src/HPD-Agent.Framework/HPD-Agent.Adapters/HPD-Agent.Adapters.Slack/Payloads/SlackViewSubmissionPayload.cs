using System.Text.Json.Serialization;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Slack.Payloads;

[WebhookPayload]
public record SlackViewSubmissionPayload(
    [property: JsonPropertyName("type")]       string Type,
    [property: JsonPropertyName("trigger_id")] string TriggerId,
    [property: JsonPropertyName("user")]       SlackUser User,
    [property: JsonPropertyName("view")]       SlackView View
);

[WebhookPayload]
public record SlackViewClosedPayload(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("user")] SlackUser User,
    [property: JsonPropertyName("view")] SlackViewClosed View
);

public record SlackView(
    [property: JsonPropertyName("id")]               string Id,
    [property: JsonPropertyName("type")]             string Type,
    [property: JsonPropertyName("callback_id")]      string? CallbackId,
    [property: JsonPropertyName("private_metadata")] string? PrivateMetadata,
    [property: JsonPropertyName("state")]            SlackViewState State,
    [property: JsonPropertyName("title")]            SlackTextObject? Title = null,
    [property: JsonPropertyName("submit")]           SlackTextObject? Submit = null,
    [property: JsonPropertyName("close")]            SlackTextObject? Close = null,
    [property: JsonPropertyName("blocks")]           object[]? Blocks = null
);

public record SlackViewClosed(
    [property: JsonPropertyName("id")]               string Id,
    [property: JsonPropertyName("type")]             string Type,
    [property: JsonPropertyName("callback_id")]      string? CallbackId,
    [property: JsonPropertyName("private_metadata")] string? PrivateMetadata
);

public record SlackViewState(
    [property: JsonPropertyName("values")]
    Dictionary<string, Dictionary<string, SlackViewStateValue>> Values
);

public record SlackViewStateValue(
    [property: JsonPropertyName("type")]            string Type,
    [property: JsonPropertyName("value")]           string? Value,
    [property: JsonPropertyName("selected_option")] SlackSelectedOption? SelectedOption = null
);
