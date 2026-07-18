using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.AspNetCore;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Streaming;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Graph;

namespace HPD.Agent.Bots.Teams;

public static class TeamsBotServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Teams bot and the M365 Agents SDK agent application.
    /// Endpoint mapping stays with <c>MapAgentApplicationEndpoints</c>.
    /// </summary>
    public static WebApplicationBuilder AddTeamsBot(
        this WebApplicationBuilder builder,
        Action<TeamsBotConfig> configure,
        bool registerInfrastructure = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddTeamsBot(configure, registerInfrastructure);
        builder.AddAgent<TeamsAgent>();
        if (registerInfrastructure)
            builder.Services.AddTeamsM365AttachmentDownloaders();

        return builder;
    }

    /// <summary>
    /// Registers HPD-owned Teams services. This overload is useful for tests and
    /// hosts that register <see cref="TeamsAgent"/> themselves.
    /// </summary>
    public static IServiceCollection AddTeamsBot(
        this IServiceCollection services,
        Action<TeamsBotConfig> configure,
        bool registerInfrastructure = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.AddOptions<TeamsBotConfig>()
            .Validate(config =>
            {
                config.Validate();
                return true;
            });

        services.TryAddSingleton<TeamsBot>();
        services.TryAddSingleton<TeamsCardRenderer>();
        services.TryAddSingleton<TeamsFormatConverter>();
        services.TryAddSingleton<TeamsModalConverter>();
        services.TryAddSingleton<ITeamsHistoryService, NoopTeamsHistoryService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBotRegistryProvider, TeamsBotRegistryProvider>());
        services.TryAddPlatformSessionMapper<TeamsBotConfig>(config => config.AgentId ?? "teams");
        services.Configure<BotStreamingOptions>("teams", options =>
        {
            options.Strategy = StreamingStrategy.Native;
            options.DebounceMs = 0;
        });

        if (registerInfrastructure)
        {
            services.AddHttpClient();
            services.TryAddSingleton<IStorage, MemoryStorage>();
        }

        return services;
    }

    /// <summary>
    /// Enables Microsoft Graph backed Teams history APIs. The default Teams bot
    /// registration does not create Graph history services unless a host explicitly supplies a client.
    /// </summary>
    public static IServiceCollection AddTeamsGraphHistory(
        this IServiceCollection services,
        GraphServiceClient graphClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(graphClient);

        services.AddSingleton(graphClient);
        services.Replace(ServiceDescriptor.Singleton<ITeamsHistoryService, TeamsGraphService>());

        return services;
    }

    /// <summary>
    /// Registers the Microsoft 365 attachment downloader used by the Agents SDK
    /// to hydrate Teams file uploads into turn-state input files.
    /// </summary>
    public static IServiceCollection AddTeamsM365AttachmentDownloaders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();
        services.TryAddSingleton<IList<IInputFileDownloader>>(sp =>
        [
            new M365AttachmentDownloader(
                sp.GetRequiredService<IConnections>(),
                sp.GetRequiredService<IHttpClientFactory>())
        ]);

        return services;
    }
}
