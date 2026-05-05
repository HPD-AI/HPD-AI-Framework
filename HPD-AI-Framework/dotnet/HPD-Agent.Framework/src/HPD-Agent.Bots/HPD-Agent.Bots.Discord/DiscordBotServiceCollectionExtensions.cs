using HPD.Agent.Bots.Discord.Gateway;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent.Bots.Discord;

public static partial class DiscordBotServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Discord bot plus its supporting services. Calls the generated
    /// single-parameter overload for options, bot registration, and session mapping.
    /// </summary>
    public static IServiceCollection AddDiscordBot(
        this IServiceCollection services,
        Action<DiscordBotConfig> configure,
        bool registerInfrastructure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddDiscordBot(configure);

        if (registerInfrastructure)
        {
            services.AddHttpClient();
            services.TryAddSingleton<DiscordApiClient>();
            services.TryAddSingleton<DiscordFormatConverter>();
            services.TryAddSingleton<DiscordGatewayClient>();
        }

        return services;
    }
}
