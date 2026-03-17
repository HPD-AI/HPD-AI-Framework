using HPDAgent.Graph.Abstractions.Artifacts;
using HPD.RAG.Core.Pipeline;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-047 through T-052: Pipeline options tests.</summary>
public class PipelineOptionsTests
{
    // T-047
    [Fact]
    public void MragRetryPolicy_Attempts_Factory_SetsMaxAttempts()
    {
        var policy = MragRetryPolicy.Attempts(5);

        Assert.Equal(5, policy.MaxAttempts);
        Assert.Equal(MragBackoffStrategy.JitteredExponential, policy.Strategy);
    }

    // T-048
    [Fact]
    public void MragRetryPolicy_None_HasMaxAttempts1()
    {
        Assert.Equal(1, MragRetryPolicy.None.MaxAttempts);
    }

    // T-049
    [Fact]
    public void MragErrorPropagation_StopPipeline_IsStop()
    {
        var propagation = MragErrorPropagation.StopPipeline;
        Assert.NotNull(propagation);

        // Verify via reflection that Mode == PropagationMode.Stop
        var modeProperty = typeof(MragErrorPropagation)
            .GetProperty("Mode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(modeProperty);

        var mode = modeProperty.GetValue(propagation);
        Assert.NotNull(mode);

        var modeType = mode.GetType();
        var stopValue = Enum.Parse(modeType, "Stop");
        Assert.Equal(stopValue, mode);
    }

    // T-050
    [Fact]
    public void MragErrorPropagation_FallbackTo_SetsNodeId()
    {
        var propagation = MragErrorPropagation.FallbackTo("bm25");

        var fallbackNodeIdProp = typeof(MragErrorPropagation)
            .GetProperty("FallbackNodeId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(fallbackNodeIdProp);

        var nodeId = (string?)fallbackNodeIdProp.GetValue(propagation);
        Assert.Equal("bm25", nodeId);
    }

    // T-051
    [Fact]
    public void MragMapStageOptions_Defaults_AreCorrect()
    {
        var options = new MragMapStageOptions();

        Assert.Equal(4, options.MaxParallelTasks);
        Assert.Equal(MragMapErrorMode.ContinueOmitFailures, options.ErrorMode);
        Assert.Null(options.BatchTimeout);
    }

    // T-052
    [Fact]
    public void MragNodeOptions_ProducesArtifact_CanBeSet()
    {
        var artifactKey = ArtifactKey.Parse("mrag/corpus/docs");
        var options = new MragNodeOptions { ProducesArtifact = artifactKey };

        Assert.NotNull(options.ProducesArtifact);
    }
}
