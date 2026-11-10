using System.Collections.Concurrent;
using HPD.Agent.Bots.Contracts;
using HPD.Agent.Bots.AspNetCore;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Streaming;
using Microsoft.Agents.Authentication;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Graph;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace HPD.Agent.Bots.Teams;

public static class TeamsBotServiceCollectionExtensions
{
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>>
        OpenIdMetadataCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers the Teams bot and the M365 Agents SDK agent application.
    /// Registers default Bot Service JWT validation unless
    /// <paramref name="configureAuthentication"/> is supplied.
    /// Endpoint mapping stays with <see cref="TeamsBotEndpointRouteBuilderExtensions.MapTeamsBot"/>.
    /// </summary>
    public static WebApplicationBuilder AddTeamsBot(
        this WebApplicationBuilder builder,
        Action<TeamsBotConfig> configure,
        bool registerInfrastructure = true,
        Action<IServiceCollection>? configureAuthentication = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        AddTeamsBotCore(
            builder.Services,
            configure,
            registerInfrastructure,
            configureAuthentication,
            builder.Configuration);

        builder.AddAgent<TeamsAgent>();
        if (registerInfrastructure)
            builder.Services.AddTeamsM365AttachmentDownloaders();

        return builder;
    }

    /// <summary>
    /// Registers HPD-owned Teams services and default Bot Service JWT validation.
    /// This overload is useful for tests and hosts that register
    /// <see cref="TeamsAgent"/> themselves.
    /// </summary>
    public static IServiceCollection AddTeamsBot(
        this IServiceCollection services,
        Action<TeamsBotConfig> configure,
        bool registerInfrastructure = true,
        Action<IServiceCollection>? configureAuthentication = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        AddTeamsBotCore(
            services,
            configure,
            registerInfrastructure,
            configureAuthentication,
            configuration: null);

        return services;
    }

    private static void AddTeamsBotCore(
        IServiceCollection services,
        Action<TeamsBotConfig> configure,
        bool registerInfrastructure,
        Action<IServiceCollection>? configureAuthentication,
        IConfiguration? configuration)
    {
        var teamsConfig = CaptureTeamsBotConfig(configure);
        services.Configure(configure);
        services.AddOptions<TeamsBotConfig>()
            .Validate(config =>
            {
                config.Validate();
                return true;
            });

        if (configureAuthentication is null)
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options => ApplyTeamsTokenValidation(
                    options,
                    configuration,
                    teamsConfig));
            services.AddAuthorization();
        }
        else
        {
            configureAuthentication(services);
            services.AddAuthorization();
        }

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
    }

    private static TeamsBotConfig CaptureTeamsBotConfig(Action<TeamsBotConfig> configure)
    {
        var config = new TeamsBotConfig();
        configure(config);
        config.Validate();
        return config;
    }

    private static void ApplyTeamsTokenValidation(
        JwtBearerOptions options,
        IConfiguration? configuration,
        TeamsBotConfig teamsConfig)
    {
        var tokenValidation = configuration?.GetSection("TokenValidation");
        var isGov = bool.TryParse(configuration?["TokenValidation:IsGov"], out var configuredIsGov)
            && configuredIsGov;
        var audiences = tokenValidation?.GetSection("Audiences")
            .GetChildren()
            .Select(item => item.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray() ?? [];
        if (audiences.Length == 0)
            audiences = [teamsConfig.AppId];

        var tenantId = tokenValidation?["TenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
            tenantId = teamsConfig.AppTenantId;
        Guid? tenantGuid = Guid.TryParse(tenantId, out var parsedTenantId)
            ? parsedTenantId
            : null;

        var issuers = tokenValidation?.GetSection("ValidIssuers")
            .GetChildren()
            .Select(item => item.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList() ?? [];
        if (issuers.Count == 0)
        {
            issuers.Add(isGov
                ? AuthenticationConstants.GovBotFrameworkTokenIssuer
                : AuthenticationConstants.BotFrameworkTokenIssuer);

            if (tenantGuid is not null)
            {
                var tenant = tenantGuid.Value.ToString("D");
                if (isGov)
                {
                    issuers.Add($"https://sts.windows.net/{tenant}/");
                    issuers.Add($"https://login.microsoftonline.us/{tenant}/v2.0");
                }
                else
                {
                    issuers.Add($"https://sts.windows.net/{tenant}/");
                    issuers.Add($"https://login.microsoftonline.com/{tenant}/v2.0");
                }
            }
        }

        options.SaveToken = true;
        var botMetadataAddress = tokenValidation?["AzureBotServiceOpenIdMetadataUrl"]
            ?? (isGov
                ? AuthenticationConstants.GovAzureBotServiceOpenIdMetadataUrl
                : AuthenticationConstants.PublicAzureBotServiceOpenIdMetadataUrl);
        var entraMetadataAddress = tokenValidation?["OpenIdMetadataUrl"]
            ?? (tenantGuid is not null
                ? isGov
                    ? $"https://login.microsoftonline.us/{tenantGuid.Value:D}/v2.0/.well-known/openid-configuration"
                    : $"https://login.microsoftonline.com/{tenantGuid.Value:D}/v2.0/.well-known/openid-configuration"
                : null);
        options.MetadataAddress = botMetadataAddress;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            ValidAudiences = audiences,
            ValidIssuers = issuers,
        };
        options.Events ??= new JwtBearerEvents();
        var priorOnMessageReceived = options.Events.OnMessageReceived;
        options.Events.OnMessageReceived = async context =>
        {
            if (priorOnMessageReceived is not null)
                await priorOnMessageReceived(context);

            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return;

            string issuer;
            try
            {
                issuer = new JsonWebToken(authorization["Bearer ".Length..].Trim()).Issuer;
            }
            catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
            {
                return;
            }

            var isBotFrameworkIssuer =
                string.Equals(issuer, AuthenticationConstants.BotFrameworkTokenIssuer, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(issuer, AuthenticationConstants.GovBotFrameworkTokenIssuer, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(issuer, AuthenticationConstants.ChinaBotFrameworkTokenIssuer, StringComparison.OrdinalIgnoreCase);

            var metadataAddress = isBotFrameworkIssuer || entraMetadataAddress is null
                ? botMetadataAddress
                : entraMetadataAddress;
            context.Options.TokenValidationParameters.ConfigurationManager =
                OpenIdMetadataCache.GetOrAdd(metadataAddress, static address =>
                    new ConfigurationManager<OpenIdConnectConfiguration>(
                        address,
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpClient()));
        };
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
