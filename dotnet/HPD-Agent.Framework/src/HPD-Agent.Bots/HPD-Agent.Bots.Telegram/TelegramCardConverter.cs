using System.Text;
using System.Text.Json;
using HPD.Agent.Bots.Cards;
using HPD.Agent.Bots.Contracts;
using Telegram.Bot.Types.ReplyMarkups;

namespace HPD.Agent.Bots.Telegram;

public static class TelegramCardConverter
{
    private const int CallbackDataMaxBytes = 64;
    private const string CallbackPrefix = "chat:";

    public static InlineKeyboardMarkup? ToInlineKeyboard(CardElement card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var rows = new List<InlineKeyboardButton[]>();
        CollectRows(card.Children ?? [], rows);
        return rows.Count == 0 ? null : new InlineKeyboardMarkup(rows);
    }

    public static InlineKeyboardMarkup EmptyKeyboard()
        => new(Array.Empty<InlineKeyboardButton[]>());

    public static string EncodeCallbackData(string actionId, string? value = null)
    {
        var json = JsonSerializer.Serialize(
            new TelegramCallbackPayload(actionId, value),
            TelegramBotJsonContext.Default.TelegramCallbackPayload);
        var payload = CallbackPrefix + json;

        if (Encoding.UTF8.GetByteCount(payload) > CallbackDataMaxBytes)
            throw new BotValidationException(
                $"Telegram callback data exceeds {CallbackDataMaxBytes} bytes. Keep ActionId and Value short.");

        return payload;
    }

    public static (string ActionId, string? Value) DecodeCallbackData(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return ("telegram_callback", null);

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

    private static void CollectRows(IReadOnlyList<CardChild> children, List<InlineKeyboardButton[]> rows)
    {
        foreach (var child in children)
        {
            switch (child)
            {
                case CardActions actions:
                    var row = actions.Actions
                        .Select(ToButton)
                        .Where(button => button is not null)
                        .Cast<InlineKeyboardButton>()
                        .ToArray();
                    if (row.Length > 0)
                        rows.Add(row);
                    break;

                case CardSection section:
                    CollectRows(section.Children ?? [], rows);
                    break;
            }
        }
    }

    private static InlineKeyboardButton? ToButton(CardAction action)
        => action switch
        {
            CardButton { Url: { } url } link => InlineKeyboardButton.WithUrl(ConvertEmojiPlaceholders(link.Label), url),
            CardButton button => InlineKeyboardButton.WithCallbackData(
                ConvertEmojiPlaceholders(button.Label),
                EncodeCallbackData(button.ActionId, button.Value)),
            _ => null,
        };

    internal static string ConvertEmojiPlaceholders(string text)
        => BotEmojiResolver.ConvertPlaceholders(text, BotEmojiFormat.Unicode);

    internal static bool TryResolveEmojiName(string name, out string emoji)
        => BotEmojiResolver.TryToUnicode(name, out emoji);
}
