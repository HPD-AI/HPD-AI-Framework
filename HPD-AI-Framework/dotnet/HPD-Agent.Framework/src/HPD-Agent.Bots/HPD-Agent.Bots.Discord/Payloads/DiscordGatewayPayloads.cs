using System.Text.Json.Serialization;

namespace HPD.Agent.Bots.Discord.Payloads;

[HpdBotPayload]
public record DiscordGatewayMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channel_id")] string ChannelId,
    [property: JsonPropertyName("guild_id")] string? GuildId,
    [property: JsonPropertyName("author")] DiscordUser Author,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("timestamp")] string Timestamp,
    [property: JsonPropertyName("mentions")] DiscordUser[] Mentions,
    [property: JsonPropertyName("mention_roles")] string[]? MentionRoles,
    [property: JsonPropertyName("attachments")] DiscordAttachment[] Attachments,
    [property: JsonPropertyName("channel_type")] int? ChannelType,
    [property: JsonPropertyName("thread")] DiscordThreadRef? Thread,
    [property: JsonPropertyName("is_mention")] bool? IsMention);

[HpdBotPayload]
public record DiscordGatewayReaction(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("channel_id")] string ChannelId,
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("guild_id")] string? GuildId,
    [property: JsonPropertyName("emoji")] DiscordEmoji Emoji,
    [property: JsonPropertyName("member")] DiscordMember? Member,
    [property: JsonPropertyName("user")] DiscordUser? User,
    [property: JsonPropertyName("channel_type")] int? ChannelType);
