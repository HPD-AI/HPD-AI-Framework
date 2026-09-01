using System;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Secrets;

/// <summary>
/// Wraps an existing service provider with exact singleton overrides owned by an agent build.
/// </summary>
internal class CompositeServiceProvider : IServiceProvider
{
    private readonly IServiceProvider _inner;
    private readonly IReadOnlyDictionary<Type, object> _overrides;

    /// <summary>Creates a provider overlay from the supplied singleton instances.</summary>
    public CompositeServiceProvider(IServiceProvider? inner, params object[] services)
    {
        _inner = inner ?? new ServiceCollection().BuildServiceProvider();
        ArgumentNullException.ThrowIfNull(services);
        var overrides = new Dictionary<Type, object>();
        foreach (var service in services)
        {
            ArgumentNullException.ThrowIfNull(service);
            foreach (var contract in service.GetType().GetInterfaces().Append(service.GetType()))
                overrides[contract] = service;
        }
        _overrides = overrides;
    }

    public object? GetService(Type serviceType)
    {
        return _overrides.TryGetValue(serviceType, out var service)
            ? service
            : _inner.GetService(serviceType);
    }
}
