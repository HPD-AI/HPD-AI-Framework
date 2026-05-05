using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;

namespace HPD.Agent.Bots.Telegram;

public sealed class TelegramPollingService(
    ITelegramBotClient bot,
    TelegramBot telegramBot,
    IOptions<TelegramBotConfig> options,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    private readonly TelegramBotConfig _config = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await ShouldPollAsync(bot, _config, logger, stoppingToken))
            return;

        var polling = _config.LongPolling ?? new TelegramLongPollingConfig();
        bot.Timeout = TimeSpan.FromSeconds(Math.Max(1, polling.Timeout));

        if (polling.DeleteWebhook)
            await bot.DeleteWebhook(polling.DropPendingUpdates, stoppingToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = polling.AllowedUpdates,
            Limit = Math.Max(1, Math.Min(polling.Limit, 100)),
            DropPendingUpdates = polling.DropPendingUpdates,
        };

        await bot.ReceiveAsync(
            updateHandler: async (_, update, ct) => await telegramBot.ProcessUpdateAsync(update, ct),
            errorHandler: (_, exception, _) =>
            {
                logger.LogWarning(exception, "Telegram polling error");
                return Task.CompletedTask;
            },
            receiverOptions,
            stoppingToken);
    }

    internal static async Task<bool> ShouldPollAsync(
        ITelegramBotClient bot,
        TelegramBotConfig config,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (config.Mode == TelegramBotMode.Webhook)
            return false;

        if (config.Mode == TelegramBotMode.Polling)
            return true;

        try
        {
            var webhook = await bot.GetWebhookInfo(ct);
            if (!string.IsNullOrWhiteSpace(webhook.Url))
                return false;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Telegram polling auto-mode detection failed; assuming webhook mode");
            return false;
        }

        return !IsLikelyServerlessRuntime();
    }

    internal static bool IsLikelyServerlessRuntime()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VERCEL")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")) ||
            (Environment.GetEnvironmentVariable("AWS_EXECUTION_ENV")?.Contains("AWS_Lambda", StringComparison.OrdinalIgnoreCase) == true) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NETLIFY")) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("K_SERVICE"));
}
