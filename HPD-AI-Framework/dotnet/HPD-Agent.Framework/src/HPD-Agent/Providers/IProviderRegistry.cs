// HPD-Agent/Providers/IProviderRegistry.cs
using System.Collections.Generic;

namespace HPD.Agent.Providers;

/// <summary>
/// Registry for client-family providers. Instance-based for testability.
/// </summary>
public interface IProviderRegistry
{
    /// <summary>
    /// Register a provider.
    /// </summary>
    /// <param name="provider">Provider implementation.</param>
    void Register(IProvider provider);

    /// <summary>
    /// Get provider by key (case-insensitive).
    /// </summary>
    /// <param name="providerKey">Provider identifier (e.g., "openai")</param>
    /// <returns>Provider, or null if not registered.</returns>
    IProvider? GetProvider(string providerKey);

    /// <summary>
    /// Get a provider by key and required family contract.
    /// </summary>
    TProvider? GetProvider<TProvider>(string providerKey) where TProvider : class, IProvider;

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
