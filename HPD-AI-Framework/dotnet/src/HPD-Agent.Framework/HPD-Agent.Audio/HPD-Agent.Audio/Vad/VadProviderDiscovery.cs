// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Vad;

/// <summary>
/// Global registry for VAD provider factories.
/// Populated via module initializers.
/// </summary>
public static class VadProviderDiscovery
{
    private static readonly Dictionary<string, Func<IVadProviderFactory>> _factories = new();
    private static readonly Dictionary<string, Type> _configTypes = new();

    /// <summary>
    /// Registers a VAD provider factory.
    /// Called from module initializers.
    /// </summary>
    public static void RegisterFactory(string providerKey, Func<IVadProviderFactory> factory)
    {
        _factories[providerKey.ToLowerInvariant()] = factory;
    }

    /// <summary>
    /// Registers a provider-specific config type for JSON deserialization.
    /// Enables AOT-friendly JSON serialization.
    /// </summary>
    public static void RegisterConfigType<TConfig>(string providerKey) where TConfig : class
    {
        _configTypes[providerKey.ToLowerInvariant()] = typeof(TConfig);
    }

    /// <summary>
    /// Gets a VAD provider factory by key.
    /// </summary>
    public static IVadProviderFactory GetFactory(string providerKey)
    {
        if (!_factories.TryGetValue(providerKey.ToLowerInvariant(), out var factory))
            throw new InvalidOperationException($"VAD provider '{providerKey}' not found. Available: {string.Join(", ", _factories.Keys)}");

        return factory();
    }

    /// <summary>
    /// Gets all registered VAD provider keys.
    /// </summary>
    public static IEnumerable<string> GetAvailableProviders() => _factories.Keys;

    /// <summary>
    /// Gets the registered config type for a provider (if any).
    /// </summary>
    public static Type? GetConfigType(string providerKey)
    {
        _configTypes.TryGetValue(providerKey.ToLowerInvariant(), out var type);
        return type;
    }
}
