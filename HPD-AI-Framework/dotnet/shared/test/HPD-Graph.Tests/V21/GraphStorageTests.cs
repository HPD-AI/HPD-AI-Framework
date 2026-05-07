using FluentAssertions;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Storage;

namespace HPD.Graph.Tests.V21;

public sealed class GraphStorageTests
{
    [Fact]
    public async Task InMemoryGraphDefinitionStore_SaveLoadListDelete_RoundTripsDefinition()
    {
        var store = new InMemoryGraphDefinitionStore();
        var graph = CreateStoredGraph("workflow-b");
        await store.SaveAsync(CreateStoredGraph("workflow-a"));
        await store.SaveAsync(graph);

        var loaded = await store.LoadAsync("workflow-b");
        var list = await store.ListAsync();

        loaded.Should().BeEquivalentTo(graph);
        list.Select(item => item.GraphId).Should().Equal("workflow-a", "workflow-b");
        list.Single(item => item.GraphId == "workflow-b").Name.Should().Be("Workflow workflow-b");

        await store.DeleteAsync("workflow-b");

        (await store.LoadAsync("workflow-b")).Should().BeNull();
        (await store.ListAsync()).Select(item => item.GraphId).Should().Equal("workflow-a");
    }

    [Fact]
    public async Task InMemoryGraphStore_PreservesCheckpointRetentionSemantics()
    {
        var store = new InMemoryGraphStore(CheckpointRetentionMode.LatestOnly);

        await store.SaveAsync(CreateStoredGraph("workflow"));
        await store.SaveCheckpointAsync(CreateCheckpoint("cp-1", "exec", createdAt: DateTimeOffset.UnixEpoch));
        await store.SaveCheckpointAsync(CreateCheckpoint("cp-2", "exec", createdAt: DateTimeOffset.UnixEpoch.AddSeconds(1)));

        (await store.LoadAsync("workflow")).Should().NotBeNull();
        (await store.LoadLatestCheckpointAsync("exec"))!.CheckpointId.Should().Be("cp-2");
        (await store.LoadCheckpointAsync("cp-1")).Should().BeNull();
        (await store.ListCheckpointsAsync("exec")).Should().ContainSingle()
            .Which.CheckpointId.Should().Be("cp-2");
    }

    [Fact]
    public async Task InMemoryScheduledGraphStore_SaveLoadListDelete_RoundTripsSchedule()
    {
        var store = new InMemoryScheduledGraphStore();
        var schedule = CreateScheduledGraph("daily-b");
        await store.SaveAsync(CreateScheduledGraph("daily-a"));
        await store.SaveAsync(schedule);

        var loaded = await store.LoadAsync("daily-b");
        var list = await store.ListAsync();

        loaded.Should().BeEquivalentTo(schedule);
        list.Select(item => item.GraphId).Should().Equal("daily-a", "daily-b");

        await store.DeleteAsync("daily-b");

        (await store.LoadAsync("daily-b")).Should().BeNull();
        (await store.ListAsync()).Select(item => item.GraphId).Should().Equal("daily-a");
    }

    [Fact]
    public async Task InMemoryWorkflowExecutionStore_SaveLoadListUpdate_RoundTripsStatus()
    {
        var store = new InMemoryWorkflowExecutionStore();
        await store.SaveAsync(CreateExecution("workflow", "exec-b", WorkflowExecutionStatus.Created, DateTimeOffset.UnixEpoch.AddSeconds(2)));
        await store.SaveAsync(CreateExecution("workflow", "exec-a", WorkflowExecutionStatus.Running, DateTimeOffset.UnixEpoch.AddSeconds(1)));
        await store.SaveAsync(CreateExecution("other", "exec-c", WorkflowExecutionStatus.Completed, DateTimeOffset.UnixEpoch.AddSeconds(3)));

        await store.SaveAsync(CreateExecution("workflow", "exec-a", WorkflowExecutionStatus.Suspended, DateTimeOffset.UnixEpoch.AddSeconds(1)) with
        {
            CurrentNodeId = "approval",
            SuspendedNodeId = "approval",
            SuspendToken = "token"
        });

        var loaded = await store.LoadAsync("workflow", "exec-a");
        var list = await store.ListAsync("workflow");

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(WorkflowExecutionStatus.Suspended);
        loaded.SuspendedNodeId.Should().Be("approval");
        loaded.SuspendToken.Should().Be("token");
        list.Select(item => item.ExecutionId).Should().Equal("exec-a", "exec-b");
        (await store.LoadAsync("workflow", "missing")).Should().BeNull();
    }

    [Fact]
    public async Task InMemoryWorkflowExecutionStore_ClaimsRenewsAndReleasesExecutionLeases()
    {
        var store = new InMemoryWorkflowExecutionStore();
        var now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        await store.SaveAsync(CreateExecution("workflow", "exec", WorkflowExecutionStatus.Created, DateTimeOffset.UnixEpoch));

        var claimed = await store.TryClaimAsync("workflow", "exec", "worker-a", now, TimeSpan.FromSeconds(30));
        var competingClaim = await store.TryClaimAsync("workflow", "exec", "worker-b", now.AddSeconds(1), TimeSpan.FromSeconds(30));
        var renewed = await store.RenewLeaseAsync("workflow", "exec", "worker-a", now.AddSeconds(10), TimeSpan.FromSeconds(30));

        claimed.Should().NotBeNull();
        claimed!.Status.Should().Be(WorkflowExecutionStatus.Running);
        claimed.ClaimedBy.Should().Be("worker-a");
        claimed.AttemptCount.Should().Be(1);
        competingClaim.Should().BeNull();
        renewed!.LeaseUntil.Should().Be(now.AddSeconds(40));

        await store.ReleaseClaimAsync("workflow", "exec", "worker-a");

        var released = await store.LoadAsync("workflow", "exec");
        released!.ClaimedBy.Should().BeNull();
        released.LeaseUntil.Should().BeNull();
    }

    [Fact]
    public async Task InMemoryWorkflowExecutionStore_ExpiredLeaseCanBeReclaimed()
    {
        var store = new InMemoryWorkflowExecutionStore();
        var now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        await store.SaveAsync(CreateExecution("workflow", "exec", WorkflowExecutionStatus.Running, DateTimeOffset.UnixEpoch) with
        {
            ClaimedBy = "worker-a",
            ClaimedAt = now.AddMinutes(-2),
            LeaseUntil = now.AddSeconds(-1),
            AttemptCount = 1
        });

        var claimed = await store.TryClaimAsync("workflow", "exec", "worker-b", now, TimeSpan.FromSeconds(30));

        claimed.Should().NotBeNull();
        claimed!.ClaimedBy.Should().Be("worker-b");
        claimed.AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task JsonGraphDefinitionStore_PersistsDefinitionsAcrossInstances()
    {
        using var temp = TempDirectory.Create();
        var graph = CreateStoredGraph("tenant/workflow");
        var first = new JsonGraphDefinitionStore(temp.Path);
        await first.SaveAsync(graph);

        var second = new JsonGraphDefinitionStore(temp.Path);
        var loaded = await second.LoadAsync("tenant/workflow");
        var list = await second.ListAsync();

        loaded.Should().NotBeNull();
        loaded!.GraphId.Should().Be("tenant/workflow");
        loaded.Config.GraphId.Should().Be("tenant/workflow");
        list.Should().ContainSingle()
            .Which.GraphId.Should().Be("tenant/workflow");
    }

    [Fact]
    public async Task JsonGraphDefinitionStore_SaveOverwritesExistingDefinition()
    {
        using var temp = TempDirectory.Create();
        var store = new JsonGraphDefinitionStore(temp.Path);

        await store.SaveAsync(CreateStoredGraph("workflow") with { Name = "Old" });
        await store.SaveAsync(CreateStoredGraph("workflow") with { Name = "New" });

        (await store.LoadAsync("workflow"))!.Name.Should().Be("New");
        (await store.ListAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task JsonScheduledGraphStore_PersistsSchedulesAcrossInstances()
    {
        using var temp = TempDirectory.Create();
        var schedule = CreateScheduledGraph("daily/workflow");
        var first = new JsonScheduledGraphStore(temp.Path);
        await first.SaveAsync(schedule);

        var second = new JsonScheduledGraphStore(temp.Path);
        var loaded = await second.LoadAsync("daily/workflow");

        loaded.Should().NotBeNull();
        loaded!.GraphId.Should().Be("daily/workflow");
        loaded.Schedule.CronExpression.Should().Be("0 3 * * *");
        loaded.Schedule.MisfirePolicy.Should().Be(ScheduleMisfirePolicyConfig.RunOnce);
    }

    [Fact]
    public async Task JsonWorkflowExecutionStore_PersistsExecutionsAcrossInstances()
    {
        using var temp = TempDirectory.Create();
        var first = new JsonWorkflowExecutionStore(temp.Path);
        await first.SaveAsync(CreateExecution("workflow/a", "exec-2", WorkflowExecutionStatus.Failed, DateTimeOffset.UnixEpoch.AddSeconds(2)) with
        {
            ErrorMessage = "boom"
        });
        await first.SaveAsync(CreateExecution("workflow/a", "exec-1", WorkflowExecutionStatus.Completed, DateTimeOffset.UnixEpoch.AddSeconds(1)) with
        {
            CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(9)
        });
        await first.SaveAsync(CreateExecution("workflow/b", "exec-3", WorkflowExecutionStatus.Running, DateTimeOffset.UnixEpoch.AddSeconds(3)));

        var second = new JsonWorkflowExecutionStore(temp.Path);
        var loaded = await second.LoadAsync("workflow/a", "exec-2");
        var list = await second.ListAsync("workflow/a");

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(WorkflowExecutionStatus.Failed);
        loaded.ErrorMessage.Should().Be("boom");
        list.Select(item => item.ExecutionId).Should().Equal("exec-1", "exec-2");
        (await second.LoadAsync("workflow/a", "missing")).Should().BeNull();
    }

    [Fact]
    public async Task JsonWorkflowExecutionStore_SaveOverwritesExistingExecution()
    {
        using var temp = TempDirectory.Create();
        var store = new JsonWorkflowExecutionStore(temp.Path);

        await store.SaveAsync(CreateExecution("workflow", "exec", WorkflowExecutionStatus.Running, DateTimeOffset.UnixEpoch));
        await store.SaveAsync(CreateExecution("workflow", "exec", WorkflowExecutionStatus.Cancelled, DateTimeOffset.UnixEpoch) with
        {
            CompletedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
            ErrorMessage = "cancelled by user"
        });

        var loaded = await store.LoadAsync("workflow", "exec");

        loaded!.Status.Should().Be(WorkflowExecutionStatus.Cancelled);
        loaded.CompletedAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(1));
        loaded.ErrorMessage.Should().Be("cancelled by user");
        (await store.ListAsync("workflow")).Should().ContainSingle();
    }

    [Fact]
    public async Task JsonWorkflowExecutionStore_ClaimLeasePersistsAcrossInstances()
    {
        using var temp = TempDirectory.Create();
        var now = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var first = new JsonWorkflowExecutionStore(temp.Path);
        await first.SaveAsync(CreateExecution("workflow", "exec", WorkflowExecutionStatus.Created, DateTimeOffset.UnixEpoch));

        var claimed = await first.TryClaimAsync("workflow", "exec", "worker-a", now, TimeSpan.FromSeconds(30));

        var second = new JsonWorkflowExecutionStore(temp.Path);
        var loaded = await second.LoadAsync("workflow", "exec");
        var competingClaim = await second.TryClaimAsync("workflow", "exec", "worker-b", now.AddSeconds(1), TimeSpan.FromSeconds(30));

        claimed.Should().NotBeNull();
        loaded!.ClaimedBy.Should().Be("worker-a");
        loaded.LeaseUntil.Should().Be(now.AddSeconds(30));
        loaded.AttemptCount.Should().Be(1);
        competingClaim.Should().BeNull();
    }

    [Fact]
    public async Task JsonCheckpointStore_FullHistory_PersistsAndListsOrderedCheckpoints()
    {
        using var temp = TempDirectory.Create();
        var first = new JsonCheckpointStore(temp.Path, CheckpointRetentionMode.FullHistory);
        await first.SaveCheckpointAsync(CreateCheckpoint("cp-2", "exec", createdAt: DateTimeOffset.UnixEpoch.AddSeconds(2)));
        await first.SaveCheckpointAsync(CreateCheckpoint("cp-1", "exec", createdAt: DateTimeOffset.UnixEpoch.AddSeconds(1)));

        var second = new JsonCheckpointStore(temp.Path, CheckpointRetentionMode.FullHistory);
        var latest = await second.LoadLatestCheckpointAsync("exec");
        var checkpoints = await second.ListCheckpointsAsync("exec");

        latest!.CheckpointId.Should().Be("cp-2");
        checkpoints.Select(item => item.CheckpointId).Should().Equal("cp-1", "cp-2");
        (await second.LoadCheckpointAsync("cp-1"))!.CheckpointId.Should().Be("cp-1");
    }

    [Fact]
    public async Task JsonCheckpointStore_LatestOnly_DeletesOlderCheckpoints()
    {
        using var temp = TempDirectory.Create();
        var store = new JsonCheckpointStore(temp.Path, CheckpointRetentionMode.LatestOnly);
        await store.SaveCheckpointAsync(CreateCheckpoint("cp-1", "exec", createdAt: DateTimeOffset.UnixEpoch.AddSeconds(1)));
        await store.SaveCheckpointAsync(CreateCheckpoint("cp-2", "exec", createdAt: DateTimeOffset.UnixEpoch.AddSeconds(2)));

        (await store.ListCheckpointsAsync("exec")).Should().ContainSingle()
            .Which.CheckpointId.Should().Be("cp-2");
        (await store.LoadCheckpointAsync("cp-1")).Should().BeNull();
    }

    [Fact]
    public async Task JsonGraphStore_CombinesDefinitionsAndCheckpointHistory()
    {
        using var temp = TempDirectory.Create();
        var first = new JsonGraphStore(temp.Path, CheckpointRetentionMode.FullHistory);
        await first.SaveAsync(CreateStoredGraph("workflow"));
        await first.SaveCheckpointAsync(CreateCheckpoint("cp-1", "exec", createdAt: DateTimeOffset.UnixEpoch.AddSeconds(1)));
        await first.SaveCheckpointAsync(CreateCheckpoint("cp-2", "exec", createdAt: DateTimeOffset.UnixEpoch.AddSeconds(2)));

        var second = new JsonGraphStore(temp.Path, CheckpointRetentionMode.FullHistory);

        (await second.LoadAsync("workflow"))!.Name.Should().Be("Workflow workflow");
        (await second.LoadLatestCheckpointAsync("exec"))!.CheckpointId.Should().Be("cp-2");
        (await second.ListCheckpointsAsync("exec")).Should().HaveCount(2);
    }

    private static StoredGraph CreateStoredGraph(string graphId) => new()
    {
        GraphId = graphId,
        Name = $"Workflow {graphId}",
        GraphVersion = "1.0.0",
        Config = GraphConfigSerializationTests.CreateMinimalGraphConfig(graphId),
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
        Description = "Stored graph",
        Metadata = new Dictionary<string, string> { ["owner"] = "tests" }
    };

    private static ScheduledGraph CreateScheduledGraph(string graphId) => new()
    {
        GraphId = graphId,
        Enabled = true,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
        Schedule = new GraphScheduleConfig
        {
            CronExpression = "0 3 * * *",
            TimeZoneId = "UTC",
            MisfirePolicy = ScheduleMisfirePolicyConfig.RunOnce,
            ConcurrencyPolicy = ScheduleConcurrencyPolicyConfig.SkipIfRunning
        }
    };

    private static GraphCheckpoint CreateCheckpoint(
        string checkpointId,
        string executionId,
        DateTimeOffset createdAt) => new()
    {
        CheckpointId = checkpointId,
        ExecutionId = executionId,
        GraphId = "workflow",
        CreatedAt = createdAt,
        CompletedNodes = new HashSet<string> { "START" },
        NodeOutputs = new Dictionary<string, object>(),
        ContextJson = "{}",
        Metadata = new CheckpointMetadata
        {
            Trigger = CheckpointTrigger.LayerCompleted,
            CompletedLayer = 1
        }
    };

    private static WorkflowExecution CreateExecution(
        string graphId,
        string executionId,
        WorkflowExecutionStatus status,
        DateTimeOffset createdAt) => new()
    {
        GraphId = graphId,
        ExecutionId = executionId,
        Status = status,
        CreatedAt = createdAt,
        StartedAt = status == WorkflowExecutionStatus.Created ? null : createdAt.AddSeconds(1),
        CompletedAt = status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled
            ? createdAt.AddSeconds(2)
            : null,
        CurrentNodeId = "node"
    };

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TempDirectory Create() =>
            new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hpd-graph-tests-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
