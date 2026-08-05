using HPD.Agent.AspNetCore.Serialization;
using HPD.Agent.ClientTools;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Hosting.Serialization;
using HPD.Agent.Serialization;
using HPD.Agent.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;

namespace HPD.Agent.AspNetCore;

/// <summary>
/// Extension methods for registering HPD Agent services.
/// </summary>
public static class HPDAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers a default (unnamed) HPD Agent.
    /// </summary>
    public static IServiceCollection AddHPDAgent(
        this IServiceCollection services,
        Action<HPDAgentConfig>? configure = null)
        => services.AddHPDAgent(Options.DefaultName, configure);

    /// <summary>
    /// Registers a default HPD Agent using a JSON or YAML agent config file.
    /// </summary>
    public static IServiceCollection AddHPDAgentFromConfigFile(
        this IServiceCollection services,
        string configPath,
        Action<HPDAgentConfig>? configure = null)
        => services.AddHPDAgentFromConfigFile(Options.DefaultName, configPath, configure);

    /// <summary>
    /// Registers a named HPD Agent using a JSON or YAML agent config file.
    /// </summary>
    public static IServiceCollection AddHPDAgentFromConfigFile(
        this IServiceCollection services,
        string name,
        string configPath,
        Action<HPDAgentConfig>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        return services.AddHPDAgent(name, options =>
        {
            options.DefaultAgentPath = configPath;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers a default HPD Agent using a configuration section containing AgentConfig data.
    /// </summary>
    public static IServiceCollection AddHPDAgentFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<HPDAgentConfig>? configure = null)
        => services.AddHPDAgentFromConfiguration(Options.DefaultName, configuration, configure);

    /// <summary>
    /// Registers a named HPD Agent using a configuration section containing AgentConfig data.
    /// </summary>
    public static IServiceCollection AddHPDAgentFromConfiguration(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<HPDAgentConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddHPDAgent(name, options =>
        {
            options.DefaultAgentDocument = ConfigurationToJson(configuration);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers a named HPD Agent. Call multiple times with different names
    /// to host multiple agents at different route prefixes.
    /// </summary>
    public static IServiceCollection AddHPDAgent(
        this IServiceCollection services,
        string name,
        Action<HPDAgentConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        // Allow empty string for Options.DefaultName

        // Register named options (each agent name gets its own HPDAgentConfig)
        if (configure != null)
            services.Configure(name, configure);

        // Register the agent registry (one per app, manages all named agent pairs)
        services.TryAddSingleton<DependencyInjection.HPDAgentRegistry>();
        services.TryAddSingleton<IHPDAgentHostingServicesProvider, DependencyInjection.HPDAgentHostingServicesProvider>();
        services.TryAddSingleton<IClientToolProviderRegistry, InMemoryClientToolProviderRegistry>();

        // Register AgentManager and SessionManager so tests and adapters can inject them directly.
        services.TryAddSingleton<HPD.Agent.Hosting.Lifecycle.AgentManager>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).AgentManager);
        services.TryAddSingleton<HPD.Agent.Hosting.Lifecycle.SessionManager>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).SessionManager);
        services.TryAddSingleton<IAgentRuntimeResolver>(sp =>
            new HostedAgentRuntimeResolver(sp.GetRequiredService<HPD.Agent.Hosting.Lifecycle.AgentManager>()));
        services.TryAddSingleton<IAgentSessionService>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Sessions);
        services.TryAddSingleton<IAgentThreadService>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Threads);
        services.TryAddSingleton<IAgentThreadExecutionService>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.ThreadExecutions);
        services.TryAddSingleton<IAgentContentService>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Content);
        services.TryAddSingleton<IAgentDefinitionService>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Agents);
        services.TryAddSingleton<IAgentMiddlewareResponseService>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.MiddlewareResponses);
        services.TryAddSingleton<IAgentStreamingService>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Streaming);
        services.TryAddSingleton<IThreadJournalRebaseSeedProvider>(sp =>
            new HostedThreadJournalRebaseSeedProvider(
                sp.GetRequiredService<HPD.Agent.Hosting.Lifecycle.SessionManager>()));

        // Register JSON serialization context for AOT (once)
        services.AddOptions<JsonOptions>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>,
                HPDAgentApiJsonOptionsSetup>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<JsonOptions>,
                HPDJsonOptionsReadOnlyPostConfigure>());

        return services;
    }

    private static string ConfigurationToJson(IConfiguration configuration)
        => ConfigurationToJsonNode(configuration).ToJsonString();

    private static JsonNode ConfigurationToJsonNode(IConfiguration configuration)
    {
        var children = configuration.GetChildren().ToArray();
        if (children.Length == 0)
            return JsonValue.Create((configuration as IConfigurationSection)?.Value) ?? JsonValue.Create(string.Empty)!;

        if (children.All(static child => int.TryParse(child.Key, out _)))
        {
            var array = new JsonArray();
            foreach (var child in children.OrderBy(static child => int.Parse(child.Key)))
                array.Add(ConfigurationToJsonNode(child));
            return array;
        }

        var obj = new JsonObject();
        foreach (var child in children)
            obj[child.Key] = ConfigurationToJsonNode(child);
        return obj;
    }
}

/// <summary>
/// Configures JSON serialization for HPD Agent API DTOs.
/// </summary>
internal class HPDAgentApiJsonOptionsSetup(IServiceProvider services) : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        options.SerializerOptions.Converters.Add(new HPD.Agent.Serialization.AgentEventJsonConverter());
        if (services.GetService<ProviderComposition>() is { } composition)
            options.SerializerOptions.Converters.Add(new AgentRunConfigJsonConverter(composition));

        // Internal endpoint types (WriteScoreRequest, etc.)
        options.SerializerOptions.TypeInfoResolverChain.Insert(0,
            HPDAgentAspNetCoreJsonSerializerContext.Default);
        // Web API-specific DTOs (from HPD-Agent.Hosting)
        options.SerializerOptions.TypeInfoResolverChain.Insert(1,
            HPDAgentApiJsonSerializerContext.Default);
        // Agent event types (PermissionResponseEvent, etc.)
        options.SerializerOptions.TypeInfoResolverChain.Insert(2,
            HPD.Agent.Serialization.AgentEventJsonContext.Default);
        // Core types including AgentConfig (from HPD-Agent core)
        options.SerializerOptions.TypeInfoResolverChain.Insert(3,
            HPDJsonContext.Default);

    }
}

/// <summary>
/// Freezes HTTP JSON options after every library has had a chance to register
/// its source-generated contexts.
/// </summary>
internal sealed class HPDJsonOptionsReadOnlyPostConfigure : IPostConfigureOptions<JsonOptions>
{
    public void PostConfigure(string? name, JsonOptions options)
    {
        // Make options read-only to enforce source-gen-only JSON serialization when IsAotCompatible is true.
        // Post-configure keeps this composable with other HPD packages that add their own contexts.
        if (!options.SerializerOptions.IsReadOnly)
            options.SerializerOptions.MakeReadOnly();
    }
}
