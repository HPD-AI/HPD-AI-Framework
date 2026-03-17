using HPD.Agent.AspNetCore.Serialization;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        // Register AgentManager and SessionManager so tests and adapters can inject them directly.
        services.TryAddSingleton<HPD.Agent.Hosting.Lifecycle.AgentManager>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).AgentManager);
        services.TryAddSingleton<HPD.Agent.Hosting.Lifecycle.SessionManager>(sp =>
            sp.GetRequiredService<DependencyInjection.HPDAgentRegistry>().Get(name).SessionManager);

        // Register JSON serialization context for AOT (once)
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<JsonOptions>,
                HPDAgentApiJsonOptionsSetup>());

        return services;
    }
}

/// <summary>
/// Configures JSON serialization for HPD Agent API DTOs.
/// </summary>
internal class HPDAgentApiJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        // Internal endpoint types (WriteScoreRequest, etc.)
        options.SerializerOptions.TypeInfoResolverChain.Insert(0,
            HPDAgentAspNetCoreJsonSerializerContext.Default);
        // Web API-specific DTOs (from HPD-Agent.Hosting)
        options.SerializerOptions.TypeInfoResolverChain.Insert(1,
            HPDAgentApiJsonSerializerContext.Default);
        // Core types including AgentConfig (from HPD-Agent core)
        options.SerializerOptions.TypeInfoResolverChain.Insert(2,
            HPDJsonContext.Default);
    }
}
