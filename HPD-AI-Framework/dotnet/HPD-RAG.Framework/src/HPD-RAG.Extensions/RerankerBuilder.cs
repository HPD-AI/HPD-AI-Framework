using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.Extensions.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder for configuring a reranker.
/// Registers <c>IReranker</c> keyed <c>"mrag:rerank"</c>.
///
/// <para>
/// Two strategies:
/// <list type="bullet">
/// <item><see cref="UseProvider"/> — resolves via <see cref="RerankerDiscovery"/> (dedicated reranking API like Cohere, Jina).</item>
/// <item><see cref="UseChatClient"/> — wraps an <see cref="IChatClient"/> in a chat-based reranker (LLM-based scoring).</item>
/// </list>
/// </para>
/// </summary>
public sealed class RerankerBuilder
{
    private readonly IServiceCollection _services;
    private Action<IServiceCollection>? _registration;

    internal RerankerBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Selects a dedicated reranker backend by its provider key
    /// (e.g. <c>"cohere"</c>, <c>"jina"</c>, <c>"huggingface"</c>, <c>"onnxruntime"</c>).
    /// The backend must be registered via <c>RerankerDiscovery</c> (automatic via the provider package's
    /// <c>[ModuleInitializer]</c>).
    /// </summary>
    /// <param name="providerKey">Provider key registered by the corresponding <c>HPD.RAG.RerankerProviders.*</c> package.</param>
    /// <param name="configure">Optional callback to customize <see cref="RerankerConfig"/>.</param>
    public RerankerBuilder UseProvider(string providerKey, Action<RerankerConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(providerKey);

        _registration = svc =>
        {
            svc.AddKeyedSingleton<IReranker>("mrag:rerank", (sp, _) =>
            {
                var features = RerankerDiscovery.GetProvider(providerKey)
                    ?? throw new InvalidOperationException(
                        $"No reranker provider registered for key '{providerKey}'. " +
                        $"Ensure the corresponding HPD.RAG.RerankerProviders.* package is referenced.");

                var config = new RerankerConfig { ProviderKey = providerKey };
                configure?.Invoke(config);

                return features.CreateReranker(config, sp);
            });
        };

        return this;
    }

    /// <summary>
    /// Uses an <see cref="IChatClient"/> as a reranker by wrapping it in a chat-based
    /// reranker that scores each (query, passage) pair on a 0–10 scale via LLM prompt.
    /// The resulting <c>IReranker</c> is registered keyed <c>"mrag:rerank"</c>.
    /// </summary>
    /// <param name="factory">Factory that creates the <see cref="IChatClient"/> from the service provider.</param>
    public RerankerBuilder UseChatClient(Func<IServiceProvider, IChatClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _registration = svc =>
        {
            svc.AddKeyedSingleton<IReranker>("mrag:rerank", (sp, _) =>
            {
                var chatClient = factory(sp);
                return new MragChatClientReranker(chatClient);
            });
        };

        return this;
    }

    /// <summary>Called by the parent builder after the configure action returns.</summary>
    internal void Register()
    {
        if (_registration == null)
            throw new InvalidOperationException(
                $"No reranker was configured. Call UseProvider(...) or UseChatClient(...) on the {nameof(RerankerBuilder)}.");

        _registration(_services);
    }
}
