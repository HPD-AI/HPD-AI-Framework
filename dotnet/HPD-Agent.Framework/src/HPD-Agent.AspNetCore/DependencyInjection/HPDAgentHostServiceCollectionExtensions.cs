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
/// Extension methods for registering HPD agent host services.
/// </summary>
public static class HPDAgentHostServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default HPD agent host profile.
    /// </summary>
    public static IServiceCollection AddHPDAgentHost(
        this IServiceCollection services,
        Action<HPDAgentHostOptions>? configure = null)
        => services.AddHPDAgentHost(Options.DefaultName, configure);

    /// <summary>
    /// Registers the default HPD agent host using a JSON or YAML agent config file.
    /// </summary>
    public static IServiceCollection AddHPDAgentHostFromConfigFile(
        this IServiceCollection services,
        string configPath,
        Action<HPDAgentHostOptions>? configure = null)
        => services.AddHPDAgentHostFromConfigFile(Options.DefaultName, configPath, configure);

    /// <summary>
    /// Registers a named HPD agent host using a JSON or YAML agent config file.
    /// </summary>
    public static IServiceCollection AddHPDAgentHostFromConfigFile(
        this IServiceCollection services,
        string name,
        string configPath,
        Action<HPDAgentHostOptions>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        return services.AddHPDAgentHost(name, options =>
        {
            options.DefaultAgentPath = configPath;
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers the default HPD agent host using a configuration section containing agent definition data.
    /// </summary>
    public static IServiceCollection AddHPDAgentHostFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<HPDAgentHostOptions>? configure = null)
        => services.AddHPDAgentHostFromConfiguration(Options.DefaultName, configuration, configure);

    /// <summary>
    /// Registers a named HPD agent host using a configuration section containing agent definition data.
    /// </summary>
    public static IServiceCollection AddHPDAgentHostFromConfiguration(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<HPDAgentHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddHPDAgentHost(name, options =>
        {
            options.DefaultAgentDocument = ConfigurationToJson(configuration);
            configure?.Invoke(options);
        });
    }

    /// <summary>
    /// Registers a named HPD agent host profile. Call multiple times with different names
    /// to host independent profiles at different route prefixes.
    /// </summary>
    /// <remarks>
    /// Call <see cref="HPDApplicationServiceCollectionExtensions.AddHPD"/> first to finalize the
    /// provider, output-event, and input-event composition consumed by every host profile.
    /// </remarks>
    public static IServiceCollection AddHPDAgentHost(
        this IServiceCollection services,
        string name,
        Action<HPDAgentHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);
        // Allow empty string for Options.DefaultName

        // Register named options (each host profile gets its own HPDAgentHostOptions)
        if (configure != null)
            services.Configure(name, configure);

        // Register the host registry (one per app, manages all named host pairs)
        services.TryAddSingleton<DependencyInjection.HPDAgentRegistry>();
        services.TryAddSingleton<IHPDAgentHostingServicesProvider, DependencyInjection.HPDAgentHostingServicesProvider>();
        services.TryAddSingleton<IClientToolProviderRegistry, InMemoryClientToolProviderRegistry>();
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<HPDApplicationComposition>().Providers);

        // Register AgentManager and SessionManager so tests and adapters can inject them directly.
        services.TryAddSingleton<HPD.Agent.Hosting.Lifecycle.AgentManager>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).AgentManager);
        services.TryAddSingleton<HPD.Agent.Hosting.Lifecycle.SessionManager>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).SessionManager);
        services.TryAddKeyedSingleton<HPD.Agent.Hosting.Lifecycle.AgentManager>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).AgentManager);
        services.TryAddKeyedSingleton<HPD.Agent.Hosting.Lifecycle.SessionManager>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).SessionManager);
        services.TryAddSingleton<IAgentRuntimeResolver>(sp =>
            new HostedAgentRuntimeResolver(sp.GetRequiredService<HPD.Agent.Hosting.Lifecycle.AgentManager>()));
        services.TryAddKeyedSingleton<IAgentRuntimeResolver>(name, (sp, _) =>
            new HostedAgentRuntimeResolver(
                sp.GetRequiredKeyedService<HPD.Agent.Hosting.Lifecycle.AgentManager>(name)));
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
        services.TryAddKeyedSingleton<IThreadJournalRebaseSeedProvider>(name, (sp, _) =>
            new HostedThreadJournalRebaseSeedProvider(
                sp.GetRequiredKeyedService<HPD.Agent.Hosting.Lifecycle.SessionManager>(name)));
        services.TryAddKeyedSingleton<IAgentSessionService>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Sessions);
        services.TryAddKeyedSingleton<IAgentThreadService>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Threads);
        services.TryAddKeyedSingleton<IAgentThreadExecutionService>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.ThreadExecutions);
        services.TryAddKeyedSingleton<IAgentContentService>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Content);
        services.TryAddKeyedSingleton<IAgentDefinitionService>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Agents);
        services.TryAddKeyedSingleton<IAgentMiddlewareResponseService>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.MiddlewareResponses);
        services.TryAddKeyedSingleton<IAgentStreamingService>(name, (sp, _) =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).HostingServices.Streaming);

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
