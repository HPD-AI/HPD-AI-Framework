using HPD.RAG.Core.Retrieval;
using HPD.RAG.Pipeline.Tests.Shared;
using HPDAgent.Graph.Abstractions.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.RAG.Pipeline.Tests.Pipeline;

/// <summary>
/// M3 Typed pipeline tests — T-135 through T-136.
/// </summary>
public sealed class TypedPipelineTests
{
    // T-135: MragRetrievalPipeline implements IMragRetriever
    [Fact]
    public async Task MragRetrievalPipeline_ImplementsIMragRetriever()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("retriever-check")
            .AddHandler("embed", MragHandlerNames.EmbedQuery)
            .AddHandler("format", MragHandlerNames.FormatContext)
            .From("START").To("embed").To("format").To("END").Done()
            .BuildRetrievalAsync();

        Assert.NotNull(pipeline);
        Assert.True(pipeline is IMragRetriever,
            "MragRetrievalPipeline should implement IMragRetriever");
    }

    // T-136: MragRetrievalPipeline.RetrieveAsync executes the graph and returns the
    // "Context" socket value produced by the FormatContext-named handler.
    //
    // The GraphOrchestrator resolves IGraphNodeHandler<MragPipelineContext> from the services
    // argument passed to RetrieveAsync — so stub handlers registered there with matching
    // HandlerNames are sufficient for unit-level execution coverage.
    [Fact]
    public async Task MragRetrievalPipeline_RetrieveAsync_ReturnsFormattedContext()
    {
        // Arrange: register stub handlers in the user-supplied services.
        // GraphOrchestrator calls services.GetServices<IGraphNodeHandler<MragPipelineContext>>()
        // and matches by HandlerName — these stubs will be found and executed.
        const string expectedContext = "retrieved context text";

        var embedStub = new StubGraphHandler(
            MragHandlerNames.EmbedQuery,
            new Dictionary<string, object> { ["embedding"] = new float[] { 0.1f } });

        var formatStub = new StubGraphHandler(
            MragHandlerNames.FormatContext,
            new Dictionary<string, object>
            {
                ["Context"] = expectedContext,
                ["Format"] = "plain",
                ["TokenEstimate"] = 3
            });

        var services = new ServiceCollection()
            .AddSingleton<IGraphNodeHandler<HPD.RAG.Core.Context.MragPipelineContext>>(embedStub)
            .AddSingleton<IGraphNodeHandler<HPD.RAG.Core.Context.MragPipelineContext>>(formatStub)
            .BuildServiceProvider();

        var pipeline = await MragPipeline.Create()
            .WithName("retriever-exec")
            .AddHandler("embed", MragHandlerNames.EmbedQuery)
            .AddHandler("format", MragHandlerNames.FormatContext)
            .From("START").To("embed").To("format").To("END").Done()
            .BuildRetrievalAsync();

        // Act
        var result = await pipeline.RetrieveAsync("test query", null, services);

        // Assert
        Assert.Equal(expectedContext, result);
        Assert.Equal(1, embedStub.CallCount);
        Assert.Equal(1, formatStub.CallCount);
    }
}
