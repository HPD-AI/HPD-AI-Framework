using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent.Bots.Discord.Payloads;

[HpdBotPayload]
public record DiscordInteraction(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("application_id")] string ApplicationId,
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("data")] JsonElement? Data,
    [property: JsonPropertyName("guild_id")] string? GuildId,
    [property: JsonPropertyName("channel_id")] string? ChannelId,
    [property: JsonPropertyName("channel")] DiscordChannel? Channel,
    [property: JsonPropertyName("member")] DiscordMember? Member,
    [property: JsonPropertyName("user")] DiscordUser? User,
    [property: JsonPropertyName("message")] DiscordMessage? Message);

[HpdBotPayload]
public record DiscordCommandOption(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("value")] JsonElement? Value,
    [property: JsonPropertyName("options")] DiscordCommandOption[]? Options);

public record DiscordUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("global_name")] string? GlobalName,
    [property: JsonPropertyName("bot")] bool? Bot);

public record DiscordMember(
    [property: JsonPropertyName("user")] DiscordUser User,
    [property: JsonPropertyName("nick")] string? Nick,
    [property: JsonPropertyName("roles")] string[] Roles);

public record DiscordChannel(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("parent_id")] string? ParentId);

public record DiscordMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channel_id")] string ChannelId,
    [property: JsonPropertyName("author")] DiscordUser Author,
    [property: JsonPropertyName("content")] string Content);

public record DiscordEmoji(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("name")] string? Name);

public record DiscordAttachment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("content_type")] string? ContentType,
    [property: JsonPropertyName("size")] int Size);

public record DiscordThreadRef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("parent_id")] string ParentId);
