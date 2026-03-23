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
    private readonly Dictionary<string, IProviderFeatures> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReaderWriterLockSlim _lock = new();

    public void Register(IProviderFeatures features)
    {
        if (string.IsNullOrWhiteSpace(features.ProviderKey))
            throw new ArgumentException("ProviderKey cannot be empty", nameof(features));

        _lock.EnterWriteLock();
        try
        {
            _providers[features.ProviderKey] = features;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IProviderFeatures? GetProvider(string providerKey)
    {
        _lock.EnterReadLock();
        try
        {
            return _providers.TryGetValue(providerKey, out var features) ? features : null;
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
