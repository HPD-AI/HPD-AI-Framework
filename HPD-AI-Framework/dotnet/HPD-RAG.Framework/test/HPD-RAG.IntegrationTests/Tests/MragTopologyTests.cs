using HPD.RAG.Pipeline;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 7: Topology tests — verify pipeline graph topologies compile correctly.
///
/// NOTE: Full end-to-end execution tests (which call RunAsync) require a live graph
/// orchestrator with actual handler implementations loaded. Tests that attempt execution
/// are skipped here — the focus is on compile-time topology construction and build success.
/// </summary>
public sealed class MragTopologyTests
{
    // T-065
    [Fact]
    public async Task SingleNodePipeline_CanBuildIngestionPipeline()
    {
        // A pipeline with one handler node builds without exception.
        var pipeline = await MragPipeline.Create()
            .WithName("single-node")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .From("START").To("read").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("single-node", pipeline.PipelineName);
    }

    // T-066
    [Fact]
    public async Task LinearPipeline_TwoNodes_BuildsSuccessfully()
    {
        // Two-node linear pipeline: START → read → chunk → END
        var pipeline = await MragPipeline.Create()
            .WithName("linear-two-node")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .AddHandler("chunk", MragHandlerNames.ChunkByHeader)
            .From("START").To("read").To("chunk").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("linear-two-node", pipeline.PipelineName);
    }

    // T-067
    [Fact]
    public async Task LinearPipeline_FullIngestion_BuildsSuccessfully()
    {
        // Full three-stage ingestion pipeline: read → chunk → write
        var pipeline = await MragPipeline.Create()
            .WithName("full-ingestion")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .AddHandler("chunk", MragHandlerNames.ChunkByHeader)
            .AddHandler("write", MragHandlerNames.WriteInMemory)
            .From("START").To("read").To("chunk").To("write").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
    }

    // T-068
    [Fact]
    public async Task RetrievalPipeline_FullRetrieval_BuildsSuccessfully()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("full-retrieval")
            .AddHandler("embed", MragHandlerNames.EmbedQuery)
            .AddHandler("search", MragHandlerNames.VectorSearch)
            .AddHandler("rerank", MragHandlerNames.Rerank)
            .AddHandler("format", MragHandlerNames.FormatContext)
            .From("START").To("embed").To("search").To("rerank").To("format").To("END").Done()
            .BuildRetrievalAsync();

        Assert.NotNull(pipeline);
        Assert.IsAssignableFrom<HPD.RAG.Core.Retrieval.IMragRetriever>(pipeline);
    }

    // T-069
    [Fact]
    public async Task ConditionalEdge_WhenEquals_PipelineBuildsSuccessfully()
    {
        // Router pipeline with conditional branching — tests that WhenEquals edge compiles.
        var pipeline = MragPipeline.Create()
            .WithName("conditional-route")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .AddHandler("chunk-text", MragHandlerNames.ChunkByHeader)
            .AddHandler("chunk-token", MragHandlerNames.ChunkByToken)
            .From("START").To("read").Done()
            .From("read").WhenEquals("type", "text").To("chunk-text").To("END").Done()
            .From("read").WhenEquals("type", "token").To("chunk-token").To("END").Done();

        var built = await pipeline.BuildIngestionAsync();
        Assert.NotNull(built);
    }

    // T-070
    [Fact]
    public async Task MapStage_InnerPipelineCompiles_NoThrow()
    {
        // Build inner sub-pipeline first
        var inner = await MragPipeline.Create()
            .WithName("inner-chunker")
            .AddHandler("chunk", MragHandlerNames.ChunkByHeader)
            .From("START").To("chunk").To("END").Done()
            .BuildSubPipelineAsync();

        // Outer pipeline references the compiled inner pipeline via AddMapStage
        var outer = await MragPipeline.Create()
            .WithName("outer-map")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .AddMapStage("map-chunk", inner)
            .AddHandler("write", MragHandlerNames.WriteInMemory)
            .From("START").To("read").To("map-chunk").To("write").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(outer);
    }

    // T-071
    [Fact]
    public void AddMapStage_WithUncompiledSubPipeline_ThrowsInvalidOperationException()
    {
        // If BuildSubPipelineAsync has NOT been called, AddMapStage must throw.
        var uncompiled = MragPipeline.Create()
            .WithName("uncompiled-inner")
            .AddHandler("chunk", MragHandlerNames.ChunkByHeader);

        var outer = MragPipeline.Create().WithName("outer");

        Assert.Throws<InvalidOperationException>(() =>
            outer.AddMapStage("map", uncompiled));
    }

    // T-072
    [Fact]
    public async Task PipelineWithTimeout_BuildsSuccessfully()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("timeout-pipeline")
            .WithTimeout(TimeSpan.FromMinutes(5))
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .From("START").To("read").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
    }
}
