using HPD.Agent.Bots.Slack.SocketMode;
using HPD.Agent.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Agent.Bots.Slack;

public static partial class SlackBotServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Slack adapter with all supporting services and a default
    /// <see cref="ISecretResolver"/> chain (environment variables → appsettings.json).
    /// Call this instead of the generated single-parameter overload.
    /// </summary>
    public static IServiceCollection AddSlackBot(
        this IServiceCollection services,
        Action<SlackBotConfig> configure,
        bool registerDefaultSecretResolver)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Call the generated half: registers SlackBotConfig options,
        // SlackBot singleton, and PlatformSessionMapper.
        services.AddSlackBot(configure);

        // ── Default ISecretResolver chain ────────────────────────────────────────
        if (registerDefaultSecretResolver)
        {
            services.TryAddSingleton<ISecretResolver>(sp =>
                new ChainedSecretResolver(
                    new EnvironmentSecretResolver(),
                    new ConfigurationSecretResolver(
                        sp.GetRequiredService<IConfiguration>())));
        }

        // ── Slack infrastructure ─────────────────────────────────────────────────
        services.AddHttpClient();
        services.TryAddSingleton<SlackApiClient>();
        services.TryAddSingleton<SlackFormatConverter>();
        services.TryAddSingleton<SlackUserCache>();
        services.TryAddSingleton<SlackSocketModeClient>();

        return services;
    }
}
