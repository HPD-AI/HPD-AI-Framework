using HPD.RAG.Core.Events;
using Xunit;

namespace HPD.RAG.Pipeline.Tests.Events;

/// <summary>
/// M5 Evaluation event tests — T-152 through T-153.
///
/// T-152 and T-153 remain skipped.  MragEvaluationPipeline.BackfillAsync maps
/// BackfillStartedEvent / BackfillPartitionCompletedEvent / BackfillCompletedEvent
/// to MRAG evaluation events, but these graph-level events are only emitted by the
/// orchestrator's backfill artifact path (IBackfillArtifact + ArtifactKey), which
/// requires deep graph artifact and partition infrastructure.  There is no lightweight
/// way to exercise this path in a unit test.  Coverage is provided by M6 IntegrationTests.
/// </summary>
public sealed class EvaluationEventTests
{
    // T-152: BackfillAsync emits EvalStartedEvent and EvalCompletedEvent.
    // Blocked: BackfillStartedEvent / BackfillCompletedEvent are only emitted by
    // the orchestrator's artifact backfill subsystem (requires ArtifactKey, partition
    // infrastructure, and IBackfillArtifact registrations). Cannot be exercised without
    // the full HPD-RAG host wiring from M6 IntegrationTests.
    [Fact(Skip = "Requires graph artifact/backfill infrastructure — BackfillStartedEvent and BackfillCompletedEvent are only emitted by orchestrator backfill path; covered by M6 IntegrationTests")]
    public Task BackfillAsync_EmitsEvalStartedAndCompleted()
        => Task.CompletedTask;

    // T-153: BackfillAsync emits EvalPartitionCompletedEvent per partition.
    // Same constraint as T-152.
    [Fact(Skip = "Requires graph artifact/backfill infrastructure — BackfillPartitionCompletedEvent is only emitted by orchestrator backfill path; covered by M6 IntegrationTests")]
    public Task BackfillAsync_EmitsEvalPartitionCompletedEvent_PerPartition()
        => Task.CompletedTask;

    // Structural test: verify evaluation event types are MragEvent subtypes
    [Fact]
    public void EvaluationEventTypes_AreSubtypesOfMragEvent()
    {
        var mragEventType = typeof(MragEvent);

        Assert.True(mragEventType.IsAssignableFrom(typeof(EvalStartedEvent)));
        Assert.True(mragEventType.IsAssignableFrom(typeof(EvalCompletedEvent)));
        Assert.True(mragEventType.IsAssignableFrom(typeof(EvalPartitionCompletedEvent)));
    }

    [Fact]
    public void EvalStartedEvent_CanBeConstructed()
    {
        var evt = new EvalStartedEvent
        {
            PipelineName = "eval-pipeline",
            ScenarioCount = 3
        };

        Assert.Equal(3, evt.ScenarioCount);
        Assert.Equal("eval-pipeline", evt.PipelineName);
    }

    [Fact]
    public void EvalPartitionCompletedEvent_CanBeConstructed()
    {
        var evt = new EvalPartitionCompletedEvent
        {
            PipelineName = "eval-pipeline",
            ScenarioName = "scenario-1",
            IterationName = "iter-1",
            Scores = new Dictionary<string, double> { ["Relevance"] = 0.85 }
        };

        Assert.Equal("scenario-1", evt.ScenarioName);
        Assert.Equal("iter-1", evt.IterationName);
        Assert.Equal(0.85, evt.Scores["Relevance"]);
    }
}
