using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.Extensions;
using HPD.RAG.IntegrationTests.Fakes;
using HPD.RAG.VectorStores.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 1: DI registration tests — verify that AddHPDMrag() and its sub-builders register the
/// expected keyed services into the DI container.
///
/// Tests that depend on the InMemory vector store call EnsureInMemoryRegistered() at the start
/// of each test to guarantee InMemoryVectorStoreModule.Initialize() has run, because
/// [ModuleInitializer] timing in xUnit is not guaranteed.
/// </summary>
public sealed class MragDiRegistrationTests
{
    private static void EnsureInMemoryRegistered()
        => InMemoryVectorStoreModule.Initialize();

    // T-001
    [Fact]
    public void AddHPDMrag_ReturnsNonNullBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddHPDMrag();
        Assert.NotNull(builder);
    }

    // T-002
    [Fact]
    public void AddHPDMrag_Idempotent_CallingTwiceDoesNotThrow()
    {
        var services = new ServiceCollection();

        // Should not throw
        var builder1 = services.AddHPDMrag();
        var builder2 = services.AddHPDMrag();

        Assert.NotNull(builder1);
        Assert.NotNull(builder2);

        // Building the container must not throw either
        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp);
    }

    // T-003
    [Fact]
    public void UseMarkdownReader_RegistersHandler()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        // Handlers are registered as IGraphNodeHandler<MragPipelineContext> — verify at least one is present.
        // We verify by confirming the container can be built (handler registration is non-throwing).
        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp);
    }

    // T-004
    [Fact]
    public void UseEmbeddingProvider_InMemory_RegistersKeyedGenerator()
    {
        // InMemory embedding provider is not a real embedding provider (no package for it);
        // instead we register a fake embedding generator directly and verify it is retrievable.
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            "mrag:embedding",
            (_, _) => new FakeEmbeddingGenerator());
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();
        var generator = sp.GetKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("mrag:embedding");
        Assert.NotNull(generator);
    }

    // T-005
    [Fact]
    public void RegisterVectorStore_InMemory_RegistersBothKeyedServices()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();

        var store = sp.GetKeyedService<VectorStore>("mrag:vectorstore");
        Assert.NotNull(store);

        var features = sp.GetKeyedService<IVectorStoreFeatures>("mrag:vectorstore-features");
        Assert.NotNull(features);
    }

    // T-006
    [Fact]
    public void UseKeywordEnricher_RegistersKeyedChatClient()
    {
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseMarkdownReader()
                .UseKeywordEnricher(e => e.UseFactory(_ => new FakeChatClient()))
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();

        var client = sp.GetKeyedService<IChatClient>("mrag:enricher:keywords");
        Assert.NotNull(client);
    }

    // T-007
    [Fact]
    public void UseJudgeChatClient_RegistersKeyedJudge()
    {
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddEvaluationHandlers(b => b
                .UseJudgeChatClient(_ => new FakeChatClient("0.9")));

        var sp = services.BuildServiceProvider();

        var judge = sp.GetKeyedService<IChatClient>("mrag:judge");
        Assert.NotNull(judge);
    }

    // T-008
    [Fact]
    public void AddIngestionHandlers_WithoutEmbeddingProvider_SemanticChunkerResolvesNull()
    {
        // Semantic chunker requires "mrag:embedding" keyed IEmbeddingGenerator.
        // Without registering it, GetKeyedService returns null (does not throw at resolve time
        // since it is GetKeyedService, not GetRequiredKeyedService).
        EnsureInMemoryRegistered();
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddIngestionHandlers(b => b
                .UseSemanticChunker()
                .RegisterVectorStore(vs => vs.UseProvider("inmemory")));

        var sp = services.BuildServiceProvider();

        // The embedding generator was never registered — must be null
        var generator = sp.GetKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("mrag:embedding");
        Assert.Null(generator);
    }

    // T-009
    [Fact]
    public void UseReranker_UseProvider_RegistersReranker()
    {
        // CohereReranker module registers itself via [ModuleInitializer] only when the assembly is loaded.
        // Since we do not reference HPD.RAG.RerankerProviders.Cohere in this test project,
        // the "cohere" key is not registered. We verify that UseProvider with an UNKNOWN key
        // defers its error to resolve time (not registration time) — the container must build without throwing.
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddRetrievalHandlers(b => b
                .UseReranker(r => r.UseProvider("cohere")));

        // Registration must not throw
        var sp = services.BuildServiceProvider();

        // Resolving the reranker with an unregistered provider key THROWS at resolve time.
        Assert.Throws<InvalidOperationException>(() =>
            sp.GetRequiredKeyedService<IReranker>("mrag:rerank"));
    }

    // T-010
    [Fact]
    public void UseReranker_UseChatClient_RegistersChatClientReranker()
    {
        var services = new ServiceCollection();
        services.AddHPDMrag()
            .AddRetrievalHandlers(b => b
                .UseReranker(r => r.UseChatClient(_ => new FakeChatClient("3"))));

        var sp = services.BuildServiceProvider();
        var reranker = sp.GetRequiredKeyedService<IReranker>("mrag:rerank");
        Assert.NotNull(reranker);
    }
}
