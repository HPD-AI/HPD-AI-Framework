using System;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Secrets;

/// <summary>
/// Wraps an existing service provider and adds additional singleton services.
/// Used to inject ISecretResolver into the provider resolution chain without
/// replacing the user's service provider.
/// </summary>
internal class CompositeServiceProvider : IServiceProvider
{
    private readonly IServiceProvider _inner;
    private readonly ISecretResolver _secretResolver;

    public CompositeServiceProvider(IServiceProvider? inner, ISecretResolver secretResolver)
    {
        _inner = inner ?? new ServiceCollection().BuildServiceProvider();
        _secretResolver = secretResolver;
    }

    public object? GetService(Type serviceType)
    {
        // If requesting ISecretResolver, return our instance
        if (serviceType == typeof(ISecretResolver))
            return _secretResolver;

        // Otherwise delegate to inner provider
        return _inner.GetService(serviceType);
    }
}
