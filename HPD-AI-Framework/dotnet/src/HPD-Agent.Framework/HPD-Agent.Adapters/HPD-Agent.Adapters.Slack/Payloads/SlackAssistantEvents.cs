using System.Text.Json.Serialization;

namespace HPD.Agent.Adapters.Slack.Payloads;

[WebhookPayload]
public record SlackAssistantThreadStartedEvent(
    [property: JsonPropertyName("type")]             string Type,
    [property: JsonPropertyName("assistant_thread")] SlackAssistantThread AssistantThread,
    [property: JsonPropertyName("event_ts")]         string EventTs
);

[WebhookPayload]
public record SlackAssistantContextChangedEvent(
    [property: JsonPropertyName("type")]             string Type,
    [property: JsonPropertyName("assistant_thread")] SlackAssistantThread AssistantThread,
    [property: JsonPropertyName("event_ts")]         string EventTs
);

public record SlackAssistantThread(
    [property: JsonPropertyName("user_id")]          string UserId,
    [property: JsonPropertyName("context")]          SlackAssistantContext? Context,
    [property: JsonPropertyName("channel_id")]       string ChannelId,
    [property: JsonPropertyName("thread_ts")]        string ThreadTs,
    // recipient_user_id + recipient_team_id are used by native streaming (chat.startStream).
    // Present only in assistant_thread_started events for Assistants threads.
    [property: JsonPropertyName("recipient_user_id")]  string? RecipientUserId = null,
    [property: JsonPropertyName("recipient_team_id")]  string? RecipientTeamId = null
);

public record SlackAssistantContext(
    [property: JsonPropertyName("channel_id")]  string? ChannelId,
    [property: JsonPropertyName("team_id")]     string? TeamId
);
