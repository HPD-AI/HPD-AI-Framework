// HPD-Agent/Providers/IProviderRegistry.cs
using System.Collections.Generic;

namespace HPD.Agent.Providers;

/// <summary>
/// Registry for provider features. Instance-based for testability.
/// </summary>
public interface IProviderRegistry
{
    /// <summary>
    /// Register a provider's features.
    /// </summary>
    /// <param name="features">Provider features implementation</param>
    void Register(IProviderFeatures features);

    /// <summary>
    /// Get provider features by key (case-insensitive).
    /// </summary>
    /// <param name="providerKey">Provider identifier (e.g., "openai")</param>
    /// <returns>Provider features, or null if not registered</returns>
    IProviderFeatures? GetProvider(string providerKey);

    /// <summary>
    /// Check if a provider is registered.
    /// </summary>
    bool IsRegistered(string providerKey);

    /// <summary>
    /// Get all registered provider keys.
    /// </summary>
    IReadOnlyCollection<string> GetRegisteredProviders();

    /// <summary>
    /// Clear all registrations (for testing only).
    /// </summary>
    void Clear();
}
