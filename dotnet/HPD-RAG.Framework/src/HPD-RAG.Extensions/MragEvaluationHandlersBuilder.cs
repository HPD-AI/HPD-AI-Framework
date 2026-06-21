using HPD.RAG.Extensions.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder for configuring evaluation handlers.
/// </summary>
public sealed class MragEvaluationHandlersBuilder
{
    private readonly IServiceCollection _services;
    private Func<IServiceProvider, IChatClient>? _judgeClientFactory;
    private CacheOptions? _cacheOptions;

    internal MragEvaluationHandlersBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Registers an <c>IChatClient</c> keyed <c>"mrag:judge"</c> used by all LLM-based evaluators.
    /// </summary>
    /// <param name="factory">Factory that creates the <c>IChatClient</c> from the service provider.</param>
    public MragEvaluationHandlersBuilder UseJudgeChatClient(Func<IServiceProvider, IChatClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _judgeClientFactory = factory;
        return this;
    }

    /// <summary>
    /// Wraps the judge chat client with <see cref="IDistributedCache"/>-based caching using
    /// ME.AI's <c>DistributedCachingChatClient</c>.
    /// <para>
    /// Requires <c>IDistributedCache</c> to be registered in the container before it is built
    /// (e.g., via <c>services.AddDistributedMemoryCache()</c>).
    /// </para>
    /// </summary>
    /// <param name="configure">Optional callback to customize cache entry options.</param>
    public MragEvaluationHandlersBuilder WithJudgeCaching(Action<CacheOptions>? configure = null)
    {
        _cacheOptions = new CacheOptions();
        configure?.Invoke(_cacheOptions);
        return this;
    }

    /// <summary>Called by <see cref="MragBuilder"/> to finalize handler registration.</summary>
    internal void Complete()
    {
        if (_judgeClientFactory != null)
        {
            var factory = _judgeClientFactory;
            var cacheOptions = _cacheOptions;

            _services.AddKeyedSingleton<IChatClient>("mrag:judge", (sp, _) =>
            {
                IChatClient client = factory(sp);

                if (cacheOptions != null)
                {
                    // Wrap with DistributedCachingChatClient — ME.AI's standard
                    // IDistributedCache-based caching middleware.
                    // The IDistributedCache is resolved from the service provider.
                    var cache = sp.GetRequiredService<IDistributedCache>();
                    client = new DistributedCachingChatClient(client, cache);
                }

                return client;
            });
        }

        MragHandlerRegistrar.RegisterEvaluationHandlers(_services);
    }
}

/// <summary>
/// Options for the judge chat client distributed cache layer.
/// </summary>
public sealed class CacheOptions
{
    /// <summary>
    /// How long a cache entry should be kept active after the last access (sliding expiration).
    /// Note: <c>DistributedCachingChatClient</c> uses a fixed expiry; for fine-grained control
    /// configure <c>IDistributedCache</c> options at the cache provider level.
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>
    /// Absolute expiration relative to now for each cache entry.
    /// </summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
}
