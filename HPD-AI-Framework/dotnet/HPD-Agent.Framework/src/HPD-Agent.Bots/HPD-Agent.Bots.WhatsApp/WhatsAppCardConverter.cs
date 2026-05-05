using System.Text.Json;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Contracts;

namespace HPD.Agent.Bots.WhatsApp;

public abstract record WhatsAppCardResult
{
    public sealed record Interactive(WhatsAppInteractiveMessage Message) : WhatsAppCardResult;

    public sealed record Text(string Body) : WhatsAppCardResult;
}

public sealed record WhatsAppInteractiveMessage(
    string Body,
    IReadOnlyList<WhatsAppInteractiveButton> Buttons,
    string? Header = null,
    string? Footer = null);

public sealed record WhatsAppInteractiveButton(
    string Id,
    string Title);

public static class WhatsAppCardConverter
{
    private const string CallbackPrefix = "chat:";
    private const int MaxButtons = 3;
    private const int ButtonTitleMax = 20;
    private const int HeaderMax = 60;
    private const int BodyMax = 1024;

    public static WhatsAppCardResult ToWhatsApp(CardElement card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var actions = FindActions(card.Children);
        var buttons = actions is null ? [] : ExtractReplyButtons(actions);
        return buttons.Count is > 0 and <= MaxButtons
            ? new WhatsAppCardResult.Interactive(BuildInteractiveMessage(card, buttons))
            : new WhatsAppCardResult.Text(BuildTextFallback(card));
    }

    public static string EncodeCallbackData(string actionId, string? value = null)
    {
        var json = JsonSerializer.Serialize(
            new WhatsAppCallbackPayload(actionId, value),
            WhatsAppBotJsonContext.Default.WhatsAppCallbackPayload);
        return CallbackPrefix + json;
    }

    public static (string ActionId, string? Value) DecodeCallbackData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return ("whatsapp_callback", null);

        if (!data.StartsWith(CallbackPrefix, StringComparison.Ordinal))
            return (data, data);

        try
        {
            using var doc = JsonDocument.Parse(data[CallbackPrefix.Length..]);
            var actionId = doc.RootElement.TryGetProperty("a", out var actionElement)
                ? actionElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(actionId))
                return (data, data);

            var value = doc.RootElement.TryGetProperty("v", out var valueElement)
                ? valueElement.GetString()
                : null;
            return (actionId, value);
        }
        catch (JsonException)
        {
            return (data, data);
        }
    }

    private static WhatsAppInteractiveMessage BuildInteractiveMessage(
        CardElement card,
        IReadOnlyList<WhatsAppInteractiveButton> buttons)
    {
        var header = Truncate(string.IsNullOrWhiteSpace(card.Title) ? null : ConvertText(card.Title), HeaderMax);
        var body = Truncate(BuildBodyText(card), BodyMax);
        if (string.IsNullOrWhiteSpace(body))
            body = header ?? "Choose an option";

        return new WhatsAppInteractiveMessage(
            Body: body,
            Buttons: buttons,
            Header: header,
            Footer: string.IsNullOrWhiteSpace(card.Subtitle) ? null : ConvertText(card.Subtitle));
    }

    private static string BuildTextFallback(CardElement card)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(card.Title))
            parts.Add($"*{ConvertText(card.Title!)}*");
        if (!string.IsNullOrWhiteSpace(card.Subtitle))
            parts.Add(ConvertText(card.Subtitle!));
        if (!string.IsNullOrWhiteSpace(card.ImageUrl))
            parts.Add(card.ImageUrl!);

        AppendChildren(parts, card.Children);
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string BuildBodyText(CardElement card)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(card.Subtitle))
            parts.Add(ConvertText(card.Subtitle));
        AppendChildren(parts, card.Children, includeActions: false);
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static void AppendChildren(
        List<string> parts,
        IReadOnlyList<CardChild>? children,
        bool includeActions = true)
    {
        if (children is null)
            return;

        foreach (var child in children)
        {
            switch (child)
            {
                case CardText text:
                    parts.Add(text.Style == "muted"
                        ? $"_{ConvertText(text.Text)}_"
                        : ConvertText(text.Text));
                    break;
                case CardFields fields:
                    foreach (var field in fields.Fields)
                        parts.Add($"*{ConvertText(field.Label)}:* {ConvertText(field.Value)}");
                    break;
                case CardLink link:
                    parts.Add($"{ConvertText(link.Label)}: {link.Url}");
                    break;
                case CardImage image:
                    var label = image.AltText ?? image.Title;
                    parts.Add(string.IsNullOrWhiteSpace(label)
                        ? image.Url
                        : $"{ConvertText(label)}: {image.Url}");
                    break;
                case CardDivider:
                    parts.Add("---");
                    break;
                case CardTable table:
                    parts.Add(new WhatsAppFormatConverter().RenderTable(table.Columns, table.Rows));
                    break;
                case CardSection section:
                    if (!string.IsNullOrWhiteSpace(section.Title))
                        parts.Add($"*{ConvertText(section.Title)}*");
                    AppendChildren(parts, section.Children, includeActions);
                    break;
                case CardActions actions when includeActions:
                    var labels = actions.Actions
                        .OfType<CardButton>()
                        .Select(button => ConvertText(button.Label))
                        .Where(label => !string.IsNullOrWhiteSpace(label));
                    parts.Add(string.Join(" | ", labels));
                    break;
            }
        }
    }

    private static CardActions? FindActions(IReadOnlyList<CardChild>? children)
    {
        if (children is null)
            return null;

        foreach (var child in children)
        {
            if (child is CardActions actions)
                return actions;
            if (child is CardSection section && FindActions(section.Children) is { } nested)
                return nested;
        }

        return null;
    }

    private static IReadOnlyList<WhatsAppInteractiveButton> ExtractReplyButtons(CardActions actions)
    {
        var buttons = actions.Actions
            .OfType<CardButton>()
            .Where(button => !string.IsNullOrWhiteSpace(button.ActionId) && string.IsNullOrWhiteSpace(button.Url))
            .Take(MaxButtons + 1)
            .Select(button => new WhatsAppInteractiveButton(
                EncodeCallbackData(button.ActionId, button.Value),
                Truncate(ConvertText(button.Label), ButtonTitleMax) ?? button.ActionId))
            .ToList();

        if (buttons.Count > MaxButtons)
            return [];

        return buttons;
    }

    private static string ConvertText(string text)
        => WhatsAppFormatConverter.Escape(BotEmojiResolver.ConvertPlaceholders(text, BotEmojiFormat.Unicode));

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;

        return max <= 3
            ? value[..max]
            : string.Concat(value.AsSpan(0, max - 3), "...");
    }
}
