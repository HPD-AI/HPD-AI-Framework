using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Retrieval;
using HPD.RAG.Pipeline.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.RAG.Pipeline.Tests.Pipeline;

/// <summary>
/// M3 Builder tests — T-117 through T-124.
/// </summary>
public sealed class MragPipelineBuilderTests
{
    // T-117: MragPipeline.Create() returns non-null builder
    [Fact]
    public void Create_ReturnsNonNull()
    {
        var builder = MragPipeline.Create();
        Assert.NotNull(builder);
    }

    // T-118: WithName("test") sets name on built pipeline
    [Fact]
    public async Task WithName_SetsNameOnBuiltPipeline()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("Test")
            .BuildIngestionAsync();

        Assert.Equal("Test", pipeline.PipelineName);
    }

    // T-119: AddHandler accepts any non-empty string as a handler name at build time.
    // Validation of whether a matching IGraphNodeHandler<> is registered is deferred to
    // execution time (when GraphOrchestrator.ResolveHandler runs). Confirming that build
    // does NOT throw for an unknown name is the correct assertion.
    [Fact]
    public async Task AddHandler_UnknownHandlerName_DoesNotThrowAtBuildTime()
    {
        // Unknown handler names are silently accepted at build time.
        var pipeline = await MragPipeline.Create()
            .WithName("test")
            .AddHandler("node", "UNKNOWN.Handler")
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("test", pipeline.PipelineName);
    }

    // T-120: BuildIngestionAsync() returns non-null MragIngestionPipeline
    [Fact]
    public async Task BuildIngestionAsync_ReturnsIngestionPipeline()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("ingestion-test")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .AddHandler("chunk", MragHandlerNames.ChunkByHeader)
            .AddHandler("write", MragHandlerNames.WriteInMemory)
            .From("START").To("read").To("chunk").To("write").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.IsType<MragIngestionPipeline>(pipeline);
    }

    // T-121: BuildRetrievalAsync() returns non-null MragRetrievalPipeline
    [Fact]
    public async Task BuildRetrievalAsync_ReturnsRetrievalPipeline()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("retrieval-test")
            .AddHandler("embed", MragHandlerNames.EmbedQuery)
            .AddHandler("search", MragHandlerNames.VectorSearch)
            .AddHandler("format", MragHandlerNames.FormatContext)
            .From("START").To("embed").To("search").To("format").To("END").Done()
            .BuildRetrievalAsync();

        Assert.NotNull(pipeline);
        Assert.IsType<MragRetrievalPipeline>(pipeline);
    }

    // T-122: BuildEvaluationAsync() returns non-null MragEvaluationPipeline
    [Fact]
    public async Task BuildEvaluationAsync_ReturnsEvaluationPipeline()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("eval-test")
            .AddHandler("eval", MragHandlerNames.EvalRelevance)
            .From("START").To("eval").To("END").Done()
            .BuildEvaluationAsync();

        Assert.NotNull(pipeline);
        Assert.IsType<MragEvaluationPipeline>(pipeline);
    }

    // T-123: Builder with no handlers still builds (empty graph is valid)
    [Fact]
    public async Task Build_WithNoHandlers_Succeeds()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("empty-pipeline")
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("empty-pipeline", pipeline.PipelineName);
    }

    // T-124: Duplicate node ID detection — the builder throws at AddHandler time (not build time)
    [Fact]
    public void AddHandler_DuplicateNodeId_ThrowsInvalidOperationException()
    {
        var builder = MragPipeline.Create()
            .WithName("test")
            .AddHandler("nodeA", MragHandlerNames.ReadMarkdown);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            builder.AddHandler("nodeA", MragHandlerNames.ChunkByHeader));

        Assert.Contains("nodeA", ex.Message);
    }
}
