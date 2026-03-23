using HPD.RAG.Core.Pipeline;
using Xunit;

namespace HPD.RAG.Pipeline.Tests.Pipeline;

/// <summary>
/// M3 Resilience options tests — T-125 through T-127.
/// These tests verify that resilience options are accepted by the builder without error.
/// The built graph's internal retry policy is not directly observable from outside the graph,
/// so we verify that building with options succeeds and PipelineName is preserved.
/// </summary>
public sealed class ResilienceOptionTests
{
    // T-125: AddHandler with RetryPolicy override builds successfully (options flow through)
    [Fact]
    public async Task AddHandler_WithRetryOverride_BuildsSuccessfully()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("retry-test")
            .AddHandler("read", MragHandlerNames.ReadMarkdown,
                options: o => o.RetryPolicy = MragRetryPolicy.Attempts(6))
            .From("START").To("read").To("END").Done()
            .BuildIngestionAsync();

        // The pipeline built without throwing — retry option was accepted
        Assert.NotNull(pipeline);
        Assert.Equal("retry-test", pipeline.PipelineName);
    }

    // T-126: Handler without options override uses default; builder accepts no-options call
    [Fact]
    public async Task AddHandler_DefaultRetryPolicy_BuildsSuccessfully()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("default-retry-test")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .From("START").To("read").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
    }

    // T-127: ProducesArtifact flows through builder without error
    [Fact]
    public async Task AddHandler_WithProducesArtifact_BuildsSuccessfully()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("artifact-test")
            .AddHandler("write", MragHandlerNames.WriteInMemory,
                options: o => o.ProducesArtifact = HPDAgent.Graph.Abstractions.Artifacts.ArtifactKey.Parse("mrag/corpus/docs"))
            .From("START").To("write").To("END").Done()
            .BuildIngestionAsync();

        Assert.NotNull(pipeline);
        Assert.Equal("artifact-test", pipeline.PipelineName);
    }

    // MragRetryPolicy.Attempts factory sets MaxAttempts correctly (pure data test)
    [Fact]
    public void MragRetryPolicy_Attempts_SetsMaxAttempts()
    {
        var policy = MragRetryPolicy.Attempts(6);
        Assert.Equal(6, policy.MaxAttempts);
        Assert.Equal(MragBackoffStrategy.JitteredExponential, policy.Strategy);
    }

    // MragRetryPolicy.None has MaxAttempts == 1
    [Fact]
    public void MragRetryPolicy_None_HasMaxAttempts1()
    {
        var policy = MragRetryPolicy.None;
        Assert.Equal(1, policy.MaxAttempts);
    }
}
