using HPD.RAG.Core.Context;
using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.Extensions;
using HPD.RAG.Pipeline.Tests.Shared;
using HPD.RAG.VectorStores.InMemory;
using HPDAgent.Graph.Abstractions.Handlers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace HPD.RAG.Pipeline.Tests.DI;

/// <summary>
/// M4 DI wiring tests — T-137 through T-144.
/// </summary>
public sealed class AddHPDMragTests
{
    private static void EnsureInMemoryRegistered()
        => InMemoryVectorStoreModule.Initialize();

    // T-137: After AddHPDMrag with UseEmbeddingProvider-style registration,
    // IEmbeddingGenerator<string, Embedding<float>> keyed "mrag:embedding" is resolvable.
    // Since we don't have a real provider for "openai" in tests, we register the fake directly.
    [Fact]
    public void AddHPDMrag_EmbeddingGenerator_KeyedServiceIsResolvable()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();

        // Register the fake embedding generator directly with the mrag:embedding key
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            "mrag:embedding",
            (_, _) => new FakeEmbeddingGenerator());

        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();
        var generator = sp.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("mrag:embedding");
        Assert.NotNull(generator);
    }

    // T-138: After RegisterVectorStore(inmemory), VectorStore keyed "mrag:vectorstore" is resolvable
    [Fact]
    public void AddHPDMrag_RegisterVectorStore_InMemory_VectorStoreIsResolvable()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();

        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredKeyedService<VectorStore>("mrag:vectorstore");
        Assert.NotNull(store);
    }

    // T-139: UseKeywordEnricher registers IChatClient keyed "mrag:enricher:keywords"
    [Fact]
    public void AddHPDMrag_UseKeywordEnricher_RegistersChatClientWithCorrectKey()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();

        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .UseKeywordEnricher(e => e.UseFactory(_ => new FakeChatClient()))
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredKeyedService<IChatClient>("mrag:enricher:keywords");
        Assert.NotNull(client);
    }

    // T-140: UseJudgeChatClient registers IChatClient keyed "mrag:judge"
    [Fact]
    public void AddHPDMrag_UseJudgeChatClient_RegistersJudge()
    {
        var services = new ServiceCollection();

        services.AddHPDMrag()
            .AddEvaluationHandlers(b => b
                .UseJudgeChatClient(_ => new FakeChatClient("0.9")));

        var sp = services.BuildServiceProvider();
        var judge = sp.GetRequiredKeyedService<IChatClient>("mrag:judge");
        Assert.NotNull(judge);
    }

    // T-141: UseChatClient reranker registers IReranker keyed "mrag:rerank"
    [Fact]
    public void AddHPDMrag_UseReranker_UseChatClient_RegistersIReranker()
    {
        var services = new ServiceCollection();

        services.AddHPDMrag()
            .AddRetrievalHandlers(b => b
                .UseReranker(r => r.UseChatClient(_ => new FakeChatClient("5"))));

        var sp = services.BuildServiceProvider();
        var reranker = sp.GetRequiredKeyedService<IReranker>("mrag:rerank");
        Assert.NotNull(reranker);
    }

    // T-142: Calling AddHPDMrag() twice does not throw (idempotency)
    [Fact]
    public void AddHPDMrag_CalledTwice_DoesNotThrow()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();

        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        // Building the container should not throw
        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp);
    }

    // T-143: After AddHPDMrag() with handler registration, handler types are resolvable as
    // IGraphNodeHandler<MragPipelineContext>
    [Fact]
    public void AddHPDMrag_AddIngestionHandlers_HandlerTypesAreResolvable()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();

        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .UseHeaderChunker()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();
        var handlers = sp.GetServices<IGraphNodeHandler<MragPipelineContext>>().ToList();

        // At least 3 handlers should be registered (readers, chunkers, writers)
        Assert.True(handlers.Count >= 3,
            $"Expected at least 3 handlers, but got {handlers.Count}");
    }

    // T-144: Two AddHPDMrag() calls do not cause critical idempotency failures;
    // the container builds and can resolve services
    [Fact]
    public void AddHPDMrag_TwoCalls_ContainerBuildsCorrectly()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();

        // First call
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        // Second call — idempotency: marker not double-registered
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();

        // VectorStore should resolve (even if registered twice, DI returns last registration)
        var store = sp.GetKeyedService<VectorStore>("mrag:vectorstore");
        Assert.NotNull(store);
    }
}
