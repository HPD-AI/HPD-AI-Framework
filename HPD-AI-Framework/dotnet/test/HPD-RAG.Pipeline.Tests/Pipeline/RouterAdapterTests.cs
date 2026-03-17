using HPD.RAG.Pipeline.Tests.Shared;
using Xunit;

namespace HPD.RAG.Pipeline.Tests.Pipeline;

/// <summary>
/// M3 Router adapter tests — T-132 through T-134.
/// </summary>
public sealed class RouterAdapterTests
{
    // T-132: Router routing to port 0 — skip.
    // Same DeferredAdapterHandler/_pipelineServices constraint as T-130/T-131:
    // AddRouter<T> registers a DeferredAdapterHandler in _pipelineServices but
    // RunStreamingAsync gives the orchestrator the caller's services, so the handler is
    // never resolved. Covered by M6 IntegrationTests.
    [Fact(Skip = "DeferredAdapterHandler is internal and lives in _pipelineServices; orchestrator uses caller services — execution requires M6 integration host")]
    public Task AddRouter_Port0_RoutesToCorrectDownstreamNode()
    {
        return Task.CompletedTask;
    }

    // T-133: Same constraint as T-132.
    [Fact(Skip = "DeferredAdapterHandler is internal and lives in _pipelineServices; orchestrator uses caller services — execution requires M6 integration host")]
    public Task AddRouter_Port1_RoutesToCorrectDownstreamNode()
    {
        return Task.CompletedTask;
    }

    // T-134: MragEdgeBuilder.Port() maps to the FromPort on the built graph edge
    // Verified indirectly: AddRouter with port-based wiring compiles and builds
    [Fact]
    public async Task AddRouter_WithPortWiring_BuildsSuccessfully()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("router-test")
            .AddRouter<StubRouter>("route", ports: 2)
            .AddHandler("branch-a", MragHandlerNames.ChunkByHeader)
            .AddHandler("branch-b", MragHandlerNames.ChunkByToken)
            .From("START").To("route").Done()
            .From("route").Port(0).To("branch-a").To("END").Done()
            .From("route").Port(1).To("branch-b").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("router-test", pipeline.PipelineName);
    }

    // Verify AddRouter requires at least 2 ports
    [Fact]
    public void AddRouter_LessThan2Ports_ThrowsArgumentOutOfRange()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MragPipeline.Create()
                .WithName("bad-router")
                .AddRouter<StubRouter>("route", ports: 1));

        Assert.Equal("ports", ex.ParamName);
    }
}
