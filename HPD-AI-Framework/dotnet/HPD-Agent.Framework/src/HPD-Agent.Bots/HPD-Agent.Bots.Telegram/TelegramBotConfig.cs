using Telegram.Bot.Types.Enums;

namespace HPD.Agent.Bots.Telegram;

public sealed class TelegramBotConfig
{
    public string? BotToken { get; set; }

    public string? SecretToken { get; set; }

    public string? UserName { get; set; }

    public string? ApiBaseUrl { get; set; }

    public string? AgentId { get; set; }

    public TelegramBotMode Mode { get; set; } = TelegramBotMode.Auto;

    public TelegramLongPollingConfig? LongPolling { get; set; }

    public int? StreamingDebounceMs { get; set; }

    internal string ResolveBotToken()
        => FirstNonWhiteSpace(BotToken, System.Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"))
            ?? throw new InvalidOperationException(
                "TelegramBotConfig.BotToken is required. Set TELEGRAM_BOT_TOKEN or configure it explicitly.");

    internal string? ResolveSecretToken()
        => FirstNonWhiteSpace(SecretToken, System.Environment.GetEnvironmentVariable("TELEGRAM_WEBHOOK_SECRET_TOKEN"));

    internal string ResolveApiBaseUrl()
        => (FirstNonWhiteSpace(ApiBaseUrl, System.Environment.GetEnvironmentVariable("TELEGRAM_API_BASE_URL"))
            ?? "https://api.telegram.org").TrimEnd('/');

    internal string? ResolveUserName()
        => FirstNonWhiteSpace(UserName, System.Environment.GetEnvironmentVariable("TELEGRAM_BOT_USERNAME"));

    internal string ResolveAgentId()
        => FirstNonWhiteSpace(AgentId)
            ?? throw new InvalidOperationException("TelegramBotConfig.AgentId is required.");

    private static string? FirstNonWhiteSpace(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}

public enum TelegramBotMode
{
    Auto,
    Webhook,
    Polling,
}

public sealed class TelegramLongPollingConfig
{
    public int Timeout { get; set; } = 30;

    public int Limit { get; set; } = 100;

    public UpdateType[]? AllowedUpdates { get; set; }

    public bool DeleteWebhook { get; set; } = true;

    public bool DropPendingUpdates { get; set; }
}
