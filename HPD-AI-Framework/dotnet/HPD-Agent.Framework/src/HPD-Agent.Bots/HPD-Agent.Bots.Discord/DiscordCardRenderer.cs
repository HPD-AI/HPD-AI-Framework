using System.Text;
using HPD.Agent.Bots.Cards;

namespace HPD.Agent.Bots.Discord;

/// <summary>
/// Converts HPD card elements to Discord embeds and message components.
/// </summary>
public sealed class DiscordCardRenderer
{
    private const int DiscordBlurple = 0x5865F2;
    private const int MaxDescriptionLength = 4096;
    private const int MaxFieldValueLength = 1024;
    private const int MaxFields = 25;
    private const int MaxButtonsPerRow = 5;

    public (DiscordEmbed Embed, DiscordActionRow[] ActionRows) Render(CardElement card)
    {
        var description = new StringBuilder();
        var fields = new List<DiscordEmbedField>();
        var actionRows = new List<DiscordActionRow>();
        DiscordEmbedMedia? image = card.ImageUrl is not null ? new DiscordEmbedMedia(card.ImageUrl) : null;

        if (!string.IsNullOrWhiteSpace(card.Subtitle))
            AppendLine(description, card.Subtitle);

        foreach (var child in card.Children ?? [])
            RenderChild(child, description, fields, actionRows, ref image);

        var embed = new DiscordEmbed(
            Title: Truncate(card.Title, 256),
            Description: Truncate(description.ToString().Trim(), MaxDescriptionLength),
            Color: DiscordBlurple,
            Image: image,
            Fields: fields.Count > 0 ? fields : null);

        return (embed, [.. actionRows]);
    }

    private static void RenderChild(
        CardChild child,
        StringBuilder description,
        List<DiscordEmbedField> fields,
        List<DiscordActionRow> actionRows,
        ref DiscordEmbedMedia? image)
    {
        switch (child)
        {
            case CardText text:
                AppendLine(description, text.Style == "muted" ? $"*{text.Text}*" : text.Text);
                break;

            case CardImage cardImage:
                image ??= new DiscordEmbedMedia(cardImage.Url);
                break;

            case CardDivider:
                AppendLine(description, "---------------");
                break;

            case CardFields cardFields:
                foreach (var field in cardFields.Fields)
                {
                    if (fields.Count >= MaxFields) break;
                    fields.Add(new DiscordEmbedField(
                        TruncateRequired(field.Label, 256),
                        TruncateRequired(string.IsNullOrWhiteSpace(field.Value) ? "-" : field.Value, MaxFieldValueLength)));
                }
                break;

            case CardLink link:
                AppendLine(description, $"[{link.Label}]({link.Url})");
                break;

            case CardSection section:
                if (!string.IsNullOrWhiteSpace(section.Title))
                    AppendLine(description, $"**{section.Title}**");
                foreach (var sectionChild in section.Children ?? [])
                    RenderChild(sectionChild, description, fields, actionRows, ref image);
                break;

            case CardActions actions:
                RenderActions(actions, actionRows);
                break;
        }
    }

    private static void RenderActions(CardActions actions, List<DiscordActionRow> actionRows)
    {
        var buttons = actions.Actions
            .OfType<CardButton>()
            .Select(RenderButton)
            .ToList();

        for (var i = 0; i < buttons.Count; i += MaxButtonsPerRow)
            actionRows.Add(new DiscordActionRow(buttons.Skip(i).Take(MaxButtonsPerRow).ToList()));
    }

    private static DiscordButton RenderButton(CardButton button)
    {
        if (!string.IsNullOrWhiteSpace(button.Url))
            return new DiscordButton(style: 5, label: button.Label, url: button.Url);

        var style = button.Style switch
        {
            "primary" => 1,
            "danger" => 4,
            _ => 2,
        };

        return new DiscordButton(
            style: style,
            label: button.Label,
            customId: button.ActionId);
    }

    private static void AppendLine(StringBuilder sb, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (sb.Length > 0) sb.AppendLine();
        sb.Append(value);
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";

    private static string TruncateRequired(string value, int maxLength)
        => Truncate(value, maxLength) ?? "";
}
