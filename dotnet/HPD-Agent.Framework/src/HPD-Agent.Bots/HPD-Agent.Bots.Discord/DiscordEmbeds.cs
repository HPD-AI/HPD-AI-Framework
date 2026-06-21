using System.Text.Json.Serialization;

namespace HPD.Agent.Bots.Discord;

public record DiscordEmbed(
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("color")] int? Color = null,
    [property: JsonPropertyName("image")] DiscordEmbedMedia? Image = null,
    [property: JsonPropertyName("thumbnail")] DiscordEmbedMedia? Thumbnail = null,
    [property: JsonPropertyName("footer")] DiscordEmbedFooter? Footer = null,
    [property: JsonPropertyName("fields")] List<DiscordEmbedField>? Fields = null);

public record DiscordEmbedMedia(
    [property: JsonPropertyName("url")] string Url);

public record DiscordEmbedFooter(
    [property: JsonPropertyName("text")] string Text);

public record DiscordEmbedField(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("inline")] bool Inline = true);

public record DiscordActionRow(
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("components")] List<DiscordButton> Components)
{
    public DiscordActionRow(List<DiscordButton> components)
        : this(1, components)
    {
    }
}

public record DiscordButton(
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("style")] int Style,
    [property: JsonPropertyName("label")] string? Label = null,
    [property: JsonPropertyName("custom_id")] string? CustomId = null,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("disabled")] bool? Disabled = null)
{
    public DiscordButton(int style, string? label = null, string? customId = null, string? url = null, bool? disabled = null)
        : this(2, style, label, customId, url, disabled)
    {
    }
}

public record DiscordMessagePayload(
    [property: JsonPropertyName("content")] string? Content = null,
    [property: JsonPropertyName("embeds")] List<DiscordEmbed>? Embeds = null,
    [property: JsonPropertyName("components")] List<DiscordActionRow>? Components = null);

public record DiscordInteractionResponse(
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("data")] DiscordMessagePayload? Data = null);

public record DiscordPageResult<T>(
    IReadOnlyList<T> Items,
    string? Before = null,
    string? After = null);

public record DiscordChannelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] int Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("parent_id")] string? ParentId);

public record DiscordThreadSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("parent_id")] string ParentId,
    [property: JsonPropertyName("name")] string? Name);

public record DiscordUserProfile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("global_name")] string? GlobalName,
    [property: JsonPropertyName("bot")] bool? Bot);

public record DiscordFileUpload(string FileName, Stream Content, string? ContentType = null);
