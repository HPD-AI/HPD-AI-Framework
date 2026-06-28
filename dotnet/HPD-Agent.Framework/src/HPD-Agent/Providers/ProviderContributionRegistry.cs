// HPD-Agent/Providers/ProviderContributionRegistry.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;

namespace HPD.Agent.Providers;

/// <summary>
/// Build-time provider contribution bridge.
/// Module initializers register owned provider contributions here; AgentBuilder snapshots
/// the effective contributions into its instance provider registry.
/// </summary>
public static class ProviderContributionRegistry
{
    private static readonly ProviderContributionStore s_store = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Called by provider package ModuleInitializers to register a provider.
    /// </summary>
    public static void RegisterProviderFactory(Func<IProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        s_store.AddProviderFactory(
            CreateProviderFactoryKey(factory),
            _ => factory(),
            HpdContributionOwner.Framework);
    }

    public static void RegisterProviderContributor(
        IProviderContributor contributor,
        HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(owner);
        contributor.ConfigureProviders(
            new ProviderContributionBuilder(s_store, owner),
            new HpdProviderContributionContext
            {
                Owner = owner,
                Services = EmptyServiceProvider.Instance
            });
    }

    internal static void ApplyTo(
        IProviderRegistry registry,
        IServiceProvider? services = null)
        => s_store.ApplyTo(registry, services);

    /// <summary>
    /// For testing: clear provider contributions.
    /// </summary>
    internal static void ClearForTesting()
    {
        lock (_lock)
        {
            foreach (var owner in s_store.Owners)
            {
                s_store.RemoveOwner(owner);
            }
        }
    }

    /// <summary>
    /// Explicitly loads a provider package to trigger its ModuleInitializer.
    /// Required for Native AOT or PublishSingleFile scenarios where automatic assembly loading is not available.
    /// In non-AOT scenarios, AgentBuilder automatically discovers and loads provider assemblies.
    /// </summary>
    /// <typeparam name="TProviderModule">The provider module type (e.g., HPD.Agent.Providers.OpenRouter.OpenRouterProviderModule)</typeparam>
    /// <example>
    /// <code>
    /// // Native AOT or PublishSingleFile: Explicitly load providers before creating AgentBuilder
    /// ProviderContributionRegistry.LoadProvider&lt;HPD.Agent.Providers.OpenRouter.OpenRouterProviderModule&gt;();
    /// var agent = new AgentBuilder(config).Build();
    /// </code>
    /// </example>
    public static void LoadProvider<TProviderModule>() where TProviderModule : class
    {
        RuntimeHelpers.RunModuleConstructor(typeof(TProviderModule).Module.ModuleHandle);
    }

    /// <summary>
    /// Automatically discovers and loads all HPD-Agent provider assemblies from the entry assembly's references.
    /// Call this at application startup for PublishSingleFile deployments to ensure all providers are registered.
    /// This method is safe to call multiple times.
    /// Uses minimal reflection with known type names, making it AOT-compatible when provider assemblies are preserved.
    /// </summary>
    /// <example>
    /// <code>
    /// // At application startup (especially for PublishSingleFile):
    /// ProviderContributionRegistry.LoadAllProviders();
    /// var agent = new AgentBuilder(config).Build();
    /// </code>
    /// </example>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Provider assembly discovery uses Assembly.GetType to find provider modules. Requires provider module types to be preserved during AOT compilation.")]
    public static void LoadAllProviders()
    {
        try
        {
            // First, try to trigger module initializers for already-loaded provider assemblies
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in loadedAssemblies)
            {
                var assemblyName = assembly.GetName().Name;
                if (IsProviderAssemblyName(assemblyName))
                {
                    TriggerModuleInitializer(assembly);
                }
            }

            // Then try to load provider assemblies that are referenced but not yet loaded
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                var referencedAssemblies = entryAssembly.GetReferencedAssemblies();
                foreach (var assemblyName in referencedAssemblies)
                {
                    if (IsProviderAssemblyName(assemblyName.Name))
                    {
                        try
                        {
                            var assembly = Assembly.Load(assemblyName);
                            TriggerModuleInitializer(assembly);
                        }
                        catch
                        {
                            // Ignore - assembly may not be available in this deployment
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently continue - providers may be registered via other means
        }
    }

    private static bool IsProviderAssemblyName(string? assemblyName) =>
        assemblyName != null &&
        (assemblyName.StartsWith("HPD-Agent.Providers.", StringComparison.OrdinalIgnoreCase) ||
         assemblyName.StartsWith("HPD.Agent.Providers.Audio.", StringComparison.OrdinalIgnoreCase));

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Uses RuntimeHelpers.RunModuleConstructor which requires the module to be preserved.")]
    private static void TriggerModuleInitializer(Assembly assembly)
    {
        try
        {
            // Use RuntimeHelpers.RunModuleConstructor - most reliable way to trigger ModuleInitializers
            // This is AOT-safe as it doesn't require reflection on types, just the module handle
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        }
        catch
        {
            // Ignore errors - some assemblies may have already been initialized
        }
    }

    //     
    // PROVIDER CONFIG TYPE REGISTRATION (For FFI/JSON serialization)
    //     

    /// <summary>
    /// Registers a provider-specific configuration type for FFI serialization.
    /// Called by provider package ModuleInitializers alongside RegisterProviderFactory.
    /// </summary>
    /// <typeparam name="TConfig">The provider-specific config type (e.g., AnthropicProviderConfig)</typeparam>
    /// <param name="providerKey">Provider key (e.g., "anthropic")</param>
    /// <param name="deserializer">Function to deserialize JSON to the config type</param>
    /// <param name="serializer">Function to serialize the config type to JSON</param>
    /// <example>
    /// <code>
    /// // In AnthropicProviderModule.Initialize():
    /// ProviderContributionRegistry.RegisterProviderConfigType&lt;AnthropicProviderConfig&gt;(
    ///     "anthropic",
    ///     json => JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicProviderConfig),
    ///     config => JsonSerializer.Serialize(config, AnthropicJsonContext.Default.AnthropicProviderConfig));
    /// </code>
    /// </example>
    public static void RegisterProviderConfigType<TConfig>(
        string providerKey,
        Func<string, TConfig?> deserializer,
        Func<TConfig, string> serializer) where TConfig : class
    {
        RegisterProviderConfigType(providerKey, ProviderClientFamily.Chat, deserializer, serializer);
    }

    /// <summary>
    /// Registers a provider-specific configuration type for a specific client family.
    /// Use this when one provider key exposes multiple MEAI/HPD client families with different option shapes.
    /// </summary>
    public static void RegisterProviderConfigType<TConfig>(
        string providerKey,
        ProviderClientFamily family,
        Func<string, TConfig?> deserializer,
        Func<TConfig, string> serializer) where TConfig : class
    {
        lock (_lock)
        {
            s_store.AddProviderConfigSerializer(
                providerKey,
                family,
                new ProviderConfigRegistration(
                    typeof(TConfig),
                    json => deserializer(json),
                    obj => serializer((TConfig)obj)),
                HpdContributionOwner.Framework);
        }
    }

    public static void RegisterSecretAlias(
        string secretKey,
        params string[] environmentVariableNames)
    {
        lock (_lock)
        {
            s_store.AddSecretAlias(secretKey, environmentVariableNames, HpdContributionOwner.Framework);
        }
    }

    public static void RegisterModelCatalog(IProviderModelCatalog catalog)
    {
        lock (_lock)
        {
            s_store.AddModelCatalog(catalog, HpdContributionOwner.Framework);
        }
    }

    public static IReadOnlyList<ProviderContribution<IProviderModelCatalog>> GetModelCatalogs()
    {
        lock (_lock)
        {
            return s_store.ModelCatalogs;
        }
    }

    public static IProviderModelCatalog? GetModelCatalog(string providerKey)
    {
        lock (_lock)
        {
            return s_store.GetModelCatalog(providerKey);
        }
    }

    /// <summary>
    /// Gets the registered config type for a provider.
    /// </summary>
    /// <param name="providerKey">Provider key (e.g., "anthropic")</param>
    /// <returns>Registration info, or null if not registered</returns>
    public static ProviderConfigRegistration? GetProviderConfigType(string providerKey)
    {
        return GetProviderConfigType(providerKey, ProviderClientFamily.Chat);
    }

    /// <summary>
    /// Gets the registered config type for a provider client family.
    /// </summary>
    public static ProviderConfigRegistration? GetProviderConfigType(string providerKey, ProviderClientFamily family)
    {
        lock (_lock)
        {
            return s_store.GetProviderConfigSerializer(providerKey, family);
        }
    }

    /// <summary>
    /// Deserializes provider-specific config from JSON using the registered deserializer.
    /// </summary>
    /// <param name="providerKey">Provider key (e.g., "anthropic")</param>
    /// <param name="json">JSON string to deserialize</param>
    /// <returns>Deserialized config object, or null if provider not registered or JSON is empty</returns>
    public static object? DeserializeProviderConfig(string providerKey, string? json)
    {
        return DeserializeProviderConfig(providerKey, ProviderClientFamily.Chat, json);
    }

    /// <summary>
    /// Deserializes provider-specific client-family config from JSON using the registered deserializer.
    /// </summary>
    public static object? DeserializeProviderConfig(string providerKey, ProviderClientFamily family, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var registration = GetProviderConfigType(providerKey, family);
        return registration?.Deserialize(json);
    }

    /// <summary>
    /// Serializes provider-specific config to JSON using the registered serializer.
    /// </summary>
    /// <param name="providerKey">Provider key (e.g., "anthropic")</param>
    /// <param name="config">Config object to serialize</param>
    /// <returns>JSON string, or null if provider not registered or config is null</returns>
    public static string? SerializeProviderConfig(string providerKey, object? config)
    {
        return SerializeProviderConfig(providerKey, ProviderClientFamily.Chat, config);
    }

    /// <summary>
    /// Serializes provider-specific client-family config to JSON using the registered serializer.
    /// </summary>
    public static string? SerializeProviderConfig(string providerKey, ProviderClientFamily family, object? config)
    {
        if (config == null)
            return null;

        var registration = GetProviderConfigType(providerKey, family);
        return registration?.Serialize(config);
    }

    /// <summary>
    /// Gets all registered provider config types.
    /// Used by FFI layer for schema discovery.
    /// </summary>
    public static IReadOnlyDictionary<string, ProviderConfigRegistration> GetAllConfigTypes()
    {
        lock (_lock)
        {
            return s_store.GetProviderConfigSerializers()
                .Where(pair => pair.Key.Family == ProviderClientFamily.Chat)
                .ToDictionary(pair => pair.Key.ProviderKey, pair => pair.Value.Value, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Gets all registered provider config types keyed by provider and client family.
    /// </summary>
    public static IReadOnlyDictionary<(string ProviderKey, ProviderClientFamily Family), ProviderConfigRegistration> GetAllClientFamilyConfigTypes()
    {
        lock (_lock)
        {
            return s_store.GetProviderConfigSerializers()
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Value);
        }
    }

    private static string CreateProviderFactoryKey(Func<IProvider> factory)
    {
        var method = factory.Method;
        var declaringType = method.DeclaringType?.FullName ?? "provider";
        return $"{declaringType}.{method.Name}.{Guid.NewGuid():N}";
    }
}

internal readonly record struct ProviderConfigKey(string ProviderKey, ProviderClientFamily Family)
{
    public bool Equals(ProviderConfigKey other) =>
        Family == other.Family &&
        string.Equals(ProviderKey, other.ProviderKey, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.Ordinal.GetHashCode(ProviderKey), Family);
}

/// <summary>
/// Registration info for a provider-specific configuration type.
/// Enables type-safe serialization/deserialization without core knowing the concrete type.
/// </summary>
public class ProviderConfigRegistration
{
    /// <summary>
    /// The CLR type of the provider config (e.g., typeof(AnthropicProviderConfig)).
    /// </summary>
    public Type ConfigType { get; }

    private readonly Func<string, object?> _deserializer;
    private readonly Func<object, string> _serializer;

    public ProviderConfigRegistration(
        Type configType,
        Func<string, object?> deserializer,
        Func<object, string> serializer)
    {
        ConfigType = configType;
        _deserializer = deserializer;
        _serializer = serializer;
    }

    /// <summary>
    /// Deserializes JSON to the provider config type.
    /// </summary>
    public object? Deserialize(string json) => _deserializer(json);

    /// <summary>
    /// Serializes the provider config to JSON.
    /// </summary>
    public string Serialize(object config) => _serializer(config);
}
