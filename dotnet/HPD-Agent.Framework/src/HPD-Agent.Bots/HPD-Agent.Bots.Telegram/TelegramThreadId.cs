using HPD.Agent.Bots;

namespace HPD.Agent.Bots.Telegram;

[ThreadId("telegram:{ChatId}:{MessageThreadId}")]
public partial record TelegramThreadId(string ChatId, string? MessageThreadId)
{
    public bool IsDM => !ChatId.StartsWith("-", StringComparison.Ordinal);

    public string ChannelId => FormatChat(ChatId);

    public static string FormatChat(long chatId) => FormatChat(chatId.ToString());

    public static string FormatChat(string chatId) => $"telegram:{chatId}";

    public static string FormatThread(long chatId, int? messageThreadId)
        => FormatThread(chatId.ToString(), messageThreadId?.ToString());

    public static string FormatThread(string chatId, string? messageThreadId)
        => string.IsNullOrWhiteSpace(messageThreadId)
            ? FormatChat(chatId)
            : Format(chatId, messageThreadId);

    public static TelegramThreadId ParseFlexible(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var parts = value.Split([':'], 3);
        if (parts.Length is not (2 or 3) || parts[0] != "telegram" || string.IsNullOrWhiteSpace(parts[1]))
            throw new FormatException($"Invalid Telegram thread ID '{value}'.");

        return new TelegramThreadId(
            parts[1],
            parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : null);
    }
}
