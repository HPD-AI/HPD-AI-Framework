// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Eot;

/// <summary>
/// Global registry for EOT provider factories.
/// </summary>
public static class EotProviderDiscovery
{
    private static readonly Dictionary<string, Func<IEotProviderFactory>> _factories = new();
    private static readonly Dictionary<string, Type> _configTypes = new();

    /// <summary>Registers an EOT provider factory.</summary>
    public static void RegisterFactory(string providerKey, Func<IEotProviderFactory> factory)
    {
        _factories[providerKey.ToLowerInvariant()] = factory;
    }

    /// <summary>Registers a provider-specific config type for JSON deserialization.</summary>
    public static void RegisterConfigType<TConfig>(string providerKey) where TConfig : class
    {
        _configTypes[providerKey.ToLowerInvariant()] = typeof(TConfig);
    }

    /// <summary>Gets an EOT provider factory by key.</summary>
    public static IEotProviderFactory GetFactory(string providerKey)
    {
        if (!_factories.TryGetValue(providerKey.ToLowerInvariant(), out var factory))
            throw new InvalidOperationException($"EOT provider '{providerKey}' not found. Available: {string.Join(", ", _factories.Keys)}");

        return factory();
    }

    /// <summary>Gets all registered EOT provider keys.</summary>
    public static IEnumerable<string> GetAvailableProviders() => _factories.Keys;

    /// <summary>Gets the registered config type for a provider, if any.</summary>
    public static Type? GetConfigType(string providerKey)
    {
        _configTypes.TryGetValue(providerKey.ToLowerInvariant(), out var type);
        return type;
    }
}
