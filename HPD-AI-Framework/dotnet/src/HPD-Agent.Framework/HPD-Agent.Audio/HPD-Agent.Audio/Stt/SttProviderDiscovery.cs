// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Stt;

/// <summary>
/// Global registry for STT provider factories.
/// Populated via module initializers.
/// </summary>
public static class SttProviderDiscovery
{
    private static readonly Dictionary<string, Func<ISttProviderFactory>> _factories = new();
    private static readonly Dictionary<string, Type> _configTypes = new();

    /// <summary>
    /// Registers an STT provider factory.
    /// Called from module initializers.
    /// </summary>
    public static void RegisterFactory(string providerKey, Func<ISttProviderFactory> factory)
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
    /// Gets an STT provider factory by key.
    /// </summary>
    public static ISttProviderFactory GetFactory(string providerKey)
    {
        if (!_factories.TryGetValue(providerKey.ToLowerInvariant(), out var factory))
            throw new InvalidOperationException($"STT provider '{providerKey}' not found. Available: {string.Join(", ", _factories.Keys)}");

        return factory();
    }

    /// <summary>
    /// Gets all registered STT provider keys.
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
