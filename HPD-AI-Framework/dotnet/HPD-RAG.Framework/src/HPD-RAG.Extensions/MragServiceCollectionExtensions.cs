using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Extensions;

/// <summary>
/// Entry point extension method for wiring HPD-RAG into an <see cref="IServiceCollection"/>.
///
/// <para>Usage:</para>
/// <code>
/// services.AddHPDMrag()
///     .AddIngestionHandlers(b => b
///         .UseMarkdownReader()
///         .UseHeaderChunker()
///         .UseEmbeddingProvider("openai", "text-embedding-3-small", apiKey: "sk-...")
///         .RegisterVectorStore(vs => vs.UseProvider("inmemory")))
///     .AddRetrievalHandlers(b => b
///         .UseEmbeddingProvider("openai", "text-embedding-3-small", apiKey: "sk-...")
///         .UseVectorStore(vs => vs.UseProvider("inmemory")))
///     .AddEvaluationHandlers(b => b
///         .UseJudgeChatClient(sp => sp.GetRequiredService&lt;IChatClient&gt;()));
/// </code>
/// </summary>
public static class MragServiceCollectionExtensions
{
    // Marker service type used for idempotency detection.
    private sealed class MragRegistrationMarker { }

    /// <summary>
    /// Registers HPD-RAG services. Idempotent — calling twice has no effect.
    /// </summary>
    public static MragBuilder AddHPDMrag(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Idempotency guard: if already registered, return a builder over the same services
        // without re-running any configuration. The caller's configure lambdas will still run
        // on the builder, but the marker prevents double-registration of the marker itself.
        if (!services.Any(d => d.ServiceType == typeof(MragRegistrationMarker)))
        {
            services.AddSingleton<MragRegistrationMarker>();
        }

        return new MragBuilder(services);
    }
}
