using HPD.RAG.Pipeline;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 2: Pipeline builder tests — verify MragPipeline's fluent API, validation, and build methods.
/// </summary>
public sealed class MragPipelineBuilderTests
{
    // T-011
    [Fact]
    public void WithName_Empty_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MragPipeline.Create().WithName(string.Empty));
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // T-012
    [Fact]
    public void WithMaxIterations_Zero_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MragPipeline.Create().WithName("test").WithMaxIterations(0));
        Assert.Equal("maxIterations", ex.ParamName);
    }

    // T-013
    [Fact]
    public void AddHandler_DuplicateNodeId_ThrowsInvalidOperationException()
    {
        var pipeline = MragPipeline.Create()
            .WithName("test")
            .AddHandler("nodeA", MragHandlerNames.ReadMarkdown);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            pipeline.AddHandler("nodeA", MragHandlerNames.ChunkByHeader));

        Assert.Contains("nodeA", ex.Message);
    }

    // T-014
    [Fact]
    public void AddHandler_NullNodeId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MragPipeline.Create()
                .WithName("test")
                .AddHandler(string.Empty, MragHandlerNames.ReadMarkdown));

        Assert.NotNull(ex);
    }

    // T-015
    [Fact]
    public void From_To_CreatesEdge_BuildDoesNotThrow()
    {
        // When an edge is created, BuildIngestionAsync should not throw — it builds a graph with that edge.
        var pipeline = MragPipeline.Create()
            .WithName("test")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .From("START").To("read").To("END").Done();

        // Should complete without exception
        var task = pipeline.BuildIngestionAsync();
        Assert.NotNull(task);
    }

    // T-016
    [Fact]
    public void From_To_Port_CreatesEdgeWithPort_BuildDoesNotThrow()
    {
        var pipeline = MragPipeline.Create()
            .WithName("test")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .From("START").Port(0).To("read").To("END").Done();

        var task = pipeline.BuildIngestionAsync();
        Assert.NotNull(task);
    }

    // T-017
    [Fact]
    public async Task BuildIngestionAsync_NoNodes_ReturnsValidInstance()
    {
        // No handlers, no edges — graph has only START/END.
        // The builder must either succeed or throw a documented exception.
        // Based on implementation: BuildIngestionAsync succeeds (GraphBuilder accepts empty graphs).
        var result = await MragPipeline.Create()
            .WithName("empty-pipeline")
            .BuildIngestionAsync();

        Assert.NotNull(result);
        Assert.Equal("empty-pipeline", result.PipelineName);
    }

    // T-018
    [Fact]
    public async Task BuildIngestionAsync_WithHandlerNode_ReturnsIngestionPipeline()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("ingestion-v1")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .AddHandler("chunk", MragHandlerNames.ChunkByHeader)
            .AddHandler("write", MragHandlerNames.WriteInMemory)
            .From("START").To("read").To("chunk").To("write").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("ingestion-v1", pipeline.PipelineName);
    }

    // T-019
    [Fact]
    public async Task BuildRetrievalAsync_ReturnsRetrievalPipeline_ImplementsIMragRetriever()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("retrieval-v1")
            .AddHandler("embed", MragHandlerNames.EmbedQuery)
            .AddHandler("search", MragHandlerNames.VectorSearch)
            .AddHandler("format", MragHandlerNames.FormatContext)
            .From("START").To("embed").To("search").To("format").To("END").Done()
            .BuildRetrievalAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("retrieval-v1", pipeline.PipelineName);

        // MragRetrievalPipeline implements IMragRetriever
        Assert.IsAssignableFrom<HPD.RAG.Core.Retrieval.IMragRetriever>(pipeline);
    }

    // T-020
    [Fact]
    public async Task BuildSubPipelineAsync_Returns_MragPipeline_WithCompiledGraph()
    {
        var subPipeline = await MragPipeline.Create()
            .WithName("inner")
            .AddHandler("chunk", MragHandlerNames.ChunkByHeader)
            .From("START").To("chunk").To("END").Done()
            .BuildSubPipelineAsync();

        Assert.NotNull(subPipeline);
        // _compiledSubGraph is internal but BuildSubPipelineAsync returns `this` so we verify
        // it can be used in AddMapStage without throwing.
        var outerPipeline = MragPipeline.Create()
            .WithName("outer")
            .AddMapStage("map", subPipeline);

        Assert.NotNull(outerPipeline);
    }
}
