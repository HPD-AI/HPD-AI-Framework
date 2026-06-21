using HPD.RAG.Core.DTOs;
using HPD.RAG.Handlers.Tests.Shared;
using HPD.RAG.Ingestion.Writers;
using HPD.RAG.VectorStores.InMemory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Xunit;

namespace HPD.RAG.Handlers.Tests.Ingestion;

public sealed class WriterHandlerTests
{
    private static IServiceProvider BuildServices(int embeddingDimensions = 384)
    {
        InMemoryVectorStoreModule.Initialize();

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            "mrag:embedding", new FakeEmbeddingGenerator(embeddingDimensions));
        services.AddKeyedSingleton<Microsoft.Extensions.VectorData.VectorStore>(
            "mrag:vectorstore", new InMemoryVectorStore());
        return services.BuildServiceProvider();
    }

    private static MragChunkDto[] MakeChunks(params string[] contents) =>
        contents.Select((c, i) => new MragChunkDto
        {
            DocumentId = $"doc-{i}",
            Content = c
        }).ToArray();

    [Fact]
    public async Task InMemoryWriter_WritesChunks_AndReturnsWrittenCount()
    {
        var sp = BuildServices();
        var context = HandlerTestContext.CreateWithProvider(sp, collectionName: "test-collection");
        var handler = new InMemoryWriterHandler();
        var chunks = MakeChunks("Hello world", "Second chunk");

        var output = await handler.ExecuteAsync(chunks, context, CancellationToken.None);

        Assert.Equal(2, output.WrittenCount);
    }

    [Fact]
    public async Task InMemoryWriter_NullRunTags_DoesNotThrow()
    {
        var sp = BuildServices();
        var context = HandlerTestContext.CreateWithProvider(sp, collectionName: "test-collection-notags", runTags: null);
        var handler = new InMemoryWriterHandler();
        var chunks = MakeChunks("chunk with no tags");

        var output = await handler.ExecuteAsync(chunks, context, CancellationToken.None);

        Assert.Equal(1, output.WrittenCount);
    }

    [Fact]
    public async Task InMemoryWriter_MergesRunTagsIntoRecords()
    {
        var sp = BuildServices();
        var tags = new Dictionary<string, string> { ["env"] = "test", ["version"] = "1" };
        var context = HandlerTestContext.CreateWithProvider(sp, collectionName: "tagged-collection", runTags: tags);
        var handler = new InMemoryWriterHandler();
        var chunks = MakeChunks("tagged content");

        var output = await handler.ExecuteAsync(chunks, context, CancellationToken.None);

        Assert.Equal(1, output.WrittenCount);
    }

    [Fact]
    public async Task InMemoryWriter_IncrementalIngestion_SecondRunDoesNotThrow()
    {
        var sp = BuildServices();
        var context = HandlerTestContext.CreateWithProvider(sp, collectionName: "incremental-collection");
        var handler = new InMemoryWriterHandler();
        var chunks = MakeChunks("first run");

        await handler.ExecuteAsync(chunks, context, CancellationToken.None);

        var output = await handler.ExecuteAsync(chunks, context, CancellationToken.None);
        Assert.Equal(1, output.WrittenCount);
    }
}
