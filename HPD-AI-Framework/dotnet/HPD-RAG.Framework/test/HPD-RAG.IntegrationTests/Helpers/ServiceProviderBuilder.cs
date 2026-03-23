using HPD.RAG.Extensions;
using HPD.RAG.IntegrationTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.IntegrationTests.Helpers;

/// <summary>
/// Builds an <see cref="IServiceProvider"/> pre-configured with InMemory vector store
/// and fake embedding/chat clients for use in integration tests.
/// </summary>
internal static class ServiceProviderBuilder
{
    /// <summary>
    /// Builds a minimal service provider with the InMemory vector store and fake clients.
    /// </summary>
    public static IServiceProvider BuildMinimal()
    {
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .UseHeaderChunker()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a service provider with fake embedding generator and chat client registered
    /// under the keyed names expected by MRAG handlers.
    /// </summary>
    public static IServiceProvider BuildWithFakeClients()
    {
        var services = new ServiceCollection();

        // Fake embedding generator keyed as "mrag:embedding"
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            "mrag:embedding",
            (_, _) => new FakeEmbeddingGenerator());

        // Fake chat clients for enrichers
        services.AddKeyedSingleton<IChatClient>(
            "mrag:enricher:keywords",
            (_, _) => new FakeChatClient("keyword1, keyword2"));

        services.AddKeyedSingleton<IChatClient>(
            "mrag:enricher:summary",
            (_, _) => new FakeChatClient("A brief summary."));

        services.AddKeyedSingleton<IChatClient>(
            "mrag:judge",
            (_, _) => new FakeChatClient("0.9"));

        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .UseHeaderChunker()
                .UseKeywordEnricher(e => e.UseFactory(_ => new FakeChatClient("keyword1, keyword2")))
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")))
            .AddRetrievalHandlers(b => b
                .UseVectorStore(vs => vs.UseProvider("inmemory")))
            .AddEvaluationHandlers(b => b
                .UseJudgeChatClient(_ => new FakeChatClient("0.9")));

        return services.BuildServiceProvider();
    }
}
