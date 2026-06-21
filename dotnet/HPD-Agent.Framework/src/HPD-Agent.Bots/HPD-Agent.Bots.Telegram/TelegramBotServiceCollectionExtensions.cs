using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace HPD.Agent.Bots.Telegram;

public static partial class TelegramBotServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramBotWithPolling(
        this IServiceCollection services,
        Action<TelegramBotConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddTelegramBot(configure, registerInfrastructure: true);
        services.AddHostedService<TelegramPollingService>();
        return services;
    }

    public static IServiceCollection AddTelegramBot(
        this IServiceCollection services,
        Action<TelegramBotConfig> configure,
        bool registerInfrastructure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddTelegramBot(configure);

        if (registerInfrastructure)
        {
            services.AddHttpClient("telegram")
                .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(60));

            services.TryAddSingleton<ITelegramBotClient>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<TelegramBotConfig>>().Value;
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var options = new TelegramBotClientOptions(config.ResolveBotToken(), config.ResolveApiBaseUrl());
                return new TelegramBotClient(options, httpClientFactory.CreateClient("telegram"));
            });

            services.TryAddSingleton<TelegramFormatConverter>();
        }

        return services;
    }
}
