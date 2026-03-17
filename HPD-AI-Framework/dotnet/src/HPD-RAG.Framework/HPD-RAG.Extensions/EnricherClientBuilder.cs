using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder for configuring an <see cref="IChatClient"/> for a specific enricher handler.
/// The resulting client is registered as a keyed singleton on the service collection.
///
/// <para>
/// The builder accepts a factory <c>Func&lt;IServiceProvider, IChatClient&gt;</c>, giving callers
/// full control over client creation. This design avoids pulling HPD Agent's internal
/// <c>ProviderDiscovery.GetFactories()</c> (which is <c>internal</c>) into the Extensions assembly.
/// Provider-specific extension methods (e.g., <c>UseAnthropic</c>) can be added via
/// companion packages in the future.
/// </para>
/// </summary>
public sealed class EnricherClientBuilder
{
    private readonly string _serviceKey;
    private readonly IServiceCollection _services;
    private Func<IServiceProvider, IChatClient>? _factory;

    internal EnricherClientBuilder(string serviceKey, IServiceCollection services)
    {
        _serviceKey = serviceKey;
        _services = services;
    }

    /// <summary>
    /// Specifies an <see cref="IChatClient"/> factory for this enricher.
    /// </summary>
    /// <param name="factory">
    /// A factory that receives the service provider and returns an <see cref="IChatClient"/>.
    /// The factory is called once at DI resolve time.
    /// </param>
    public EnricherClientBuilder UseFactory(Func<IServiceProvider, IChatClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        return this;
    }

    /// <summary>Called by the parent builder after the configure action returns.</summary>
    internal void Register()
    {
        if (_factory == null)
            throw new InvalidOperationException(
                $"No IChatClient factory was configured for enricher key '{_serviceKey}'. " +
                $"Call UseFactory(...) on the {nameof(EnricherClientBuilder)}.");

        var factory = _factory;
        _services.AddKeyedSingleton<IChatClient>(_serviceKey, (sp, _) => factory(sp));
    }
}
