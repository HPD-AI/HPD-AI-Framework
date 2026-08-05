// HPD-Agent/Providers/ProviderRegistry.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HPD.Agent.Providers;

/// <summary>
/// Default implementation of IProviderRegistry.
/// Thread-safe, instance-based for testability.
/// </summary>
public class ProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<string, IProvider> _providers = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _lock = new();

    /// <summary>Initializes an empty mutable provider registry.</summary>
    public ProviderRegistry()
    {
    }

    /// <summary>Initializes a registry associated with an immutable generated composition.</summary>
    public ProviderRegistry(ProviderComposition composition)
        => Composition = composition ?? throw new ArgumentNullException(nameof(composition));

    /// <summary>Gets the generated composition that owns this registry, when present.</summary>
    public ProviderComposition? Composition { get; }

    public void Register(IProvider provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ProviderKey))
            throw new ArgumentException("ProviderKey cannot be empty", nameof(provider));

        _lock.EnterWriteLock();
        try
        {
            if (_providers.TryGetValue(provider.ProviderKey, out var existing))
            {
                if (existing is CompositeProvider composite)
                {
                    composite.Add(provider);
                }
                else if (existing.GetType() == provider.GetType())
                {
                    _providers[provider.ProviderKey] = provider;
                }
                else
                {
                    _providers[provider.ProviderKey] = new CompositeProvider(existing, provider);
                }
            }
            else
            {
                _providers[provider.ProviderKey] = provider;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IProvider? GetProvider(string providerKey)
    {
        _lock.EnterReadLock();
        try
        {
            return _providers.TryGetValue(providerKey, out var provider) ? provider : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public TProvider? GetProvider<TProvider>(string providerKey) where TProvider : class, IProvider
    {
        _lock.EnterReadLock();
        try
        {
            if (!_providers.TryGetValue(providerKey, out var provider))
                return null;

            if (provider is CompositeProvider composite && !composite.Supports<TProvider>())
                return null;

            return provider as TProvider;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool IsRegistered(string providerKey)
    {
        _lock.EnterReadLock();
        try
        {
            return _providers.ContainsKey(providerKey);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyCollection<string> GetRegisteredProviders()
    {
        _lock.EnterReadLock();
        try
        {
            return _providers.Keys.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _providers.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}
