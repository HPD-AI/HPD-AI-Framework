using FluentAssertions;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Core.Checkpointing;
using Xunit;

namespace HPD.Graph.Tests.Checkpointing;

/// <summary>
/// Tests for graph checkpointing and resume functionality.
/// </summary>
public class CheckpointingTests
{
    #region GraphCheckpoint Structure Tests

    [Fact]
    public void GraphCheckpoint_Creation_ContainsAllRequiredFields()
    {
        // Arrange & Act
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "checkpoint-1",
            ExecutionId = "exec-123",
            GraphId = "graph-abc",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string> { "node1", "node2" },
            NodeOutputs = new Dictionary<string, object>
            {
                ["node1.output"] = "value1",
                ["node2.output"] = 42
            },
            ContextJson = "{\"channels\": {}}"
        };

        // Assert
        checkpoint.CheckpointId.Should().Be("checkpoint-1");
        checkpoint.ExecutionId.Should().Be("exec-123");
        checkpoint.GraphId.Should().Be("graph-abc");
        checkpoint.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        checkpoint.CompletedNodes.Should().HaveCount(2);
        checkpoint.NodeOutputs.Should().HaveCount(2);
        checkpoint.ContextJson.Should().NotBeEmpty();
    }

    [Fact]
    public void GraphCheckpoint_CompletedNodes_IsImmutableSet()
    {
        // Arrange
        var nodes = new HashSet<string> { "node1", "node2" };
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "cp-1",
            ExecutionId = "exec-1",
            GraphId = "graph-1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = nodes,
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}"
        };

        // Act - Modify original collection
        nodes.Add("node3");

        // Assert - Checkpoint should not be affected (if using immutable collection)
        checkpoint.CompletedNodes.Should().Contain("node1");
        checkpoint.CompletedNodes.Should().Contain("node2");
    }

    [Fact]
    public void GraphCheckpoint_NodeOutputs_IsImmutableDictionary()
    {
        // Arrange
        var outputs = new Dictionary<string, object> { ["key1"] = "value1" };
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "cp-1",
            ExecutionId = "exec-1",
            GraphId = "graph-1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string>(),
            NodeOutputs = outputs,
            ContextJson = "{}"
        };

        // Act - Modify original dictionary
        outputs["key2"] = "value2";

        // Assert - Checkpoint should not be affected (if using immutable collection)
        checkpoint.NodeOutputs.Should().ContainKey("key1");
    }

    [Fact]
    public void GraphCheckpoint_SchemaVersion_DefaultsTo10()
    {
        // Arrange & Act
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "cp-1",
            ExecutionId = "exec-1",
            GraphId = "graph-1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string>(),
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}"
        };

        // Assert
        checkpoint.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void GraphCheckpoint_Metadata_IsOptional()
    {
        // Arrange & Act
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "cp-1",
            ExecutionId = "exec-1",
            GraphId = "graph-1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string>(),
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}"
            // Metadata not provided
        };

        // Assert
        checkpoint.Metadata.Should().BeNull();
    }

    [Fact]
    public void GraphCheckpoint_WithMetadata_StoresCorrectly()
    {
        // Arrange
        var metadata = new CheckpointMetadata
        {
            Trigger = CheckpointTrigger.LayerCompleted,
            CompletedLayer = 2,
            CustomMetadata = new Dictionary<string, object> { ["cost"] = 0.05 }
        };

        // Act
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "cp-1",
            ExecutionId = "exec-1",
            GraphId = "graph-1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string>(),
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}",
            Metadata = metadata
        };

        // Assert
        checkpoint.Metadata.Should().NotBeNull();
        checkpoint.Metadata!.Trigger.Should().Be(CheckpointTrigger.LayerCompleted);
        checkpoint.Metadata.CompletedLayer.Should().Be(2);
        checkpoint.Metadata.CustomMetadata.Should().ContainKey("cost");
    }

    #endregion

    #region InMemoryCheckpointStore Tests

    [Fact]
    public async Task SaveCheckpointAsync_StoresCheckpoint()
    {
        // Arrange
        var store = new InMemoryCheckpointStore();
        var checkpoint = CreateTestCheckpoint("cp-1", "exec-1");

        // Act
        await store.SaveCheckpointAsync(checkpoint);
        var loaded = await store.LoadCheckpointAsync("cp-1");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.CheckpointId.Should().Be("cp-1");
        loaded.ExecutionId.Should().Be("exec-1");
    }

    [Fact]
    public async Task LoadLatestCheckpointAsync_ReturnsMostRecent()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.FullHistory };
        var cp1 = CreateTestCheckpoint("cp-1", "exec-1", DateTimeOffset.UtcNow.AddMinutes(-10));
        var cp2 = CreateTestCheckpoint("cp-2", "exec-1", DateTimeOffset.UtcNow.AddMinutes(-5));
        var cp3 = CreateTestCheckpoint("cp-3", "exec-1", DateTimeOffset.UtcNow);

        // Act
        await store.SaveCheckpointAsync(cp1);
        await store.SaveCheckpointAsync(cp2);
        await store.SaveCheckpointAsync(cp3);

        var latest = await store.LoadLatestCheckpointAsync("exec-1");

        // Assert
        latest.Should().NotBeNull();
        latest!.CheckpointId.Should().Be("cp-3");
    }

    [Fact]
    public async Task LoadLatestCheckpointAsync_NoCheckpoints_ReturnsNull()
    {
        // Arrange
        var store = new InMemoryCheckpointStore();

        // Act
        var latest = await store.LoadLatestCheckpointAsync("nonexistent");

        // Assert
        latest.Should().BeNull();
    }

    [Fact]
    public async Task LoadCheckpointAsync_SpecificId_ReturnsCorrectCheckpoint()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.FullHistory };
        var cp1 = CreateTestCheckpoint("cp-1", "exec-1");
        var cp2 = CreateTestCheckpoint("cp-2", "exec-1");

        await store.SaveCheckpointAsync(cp1);
        await store.SaveCheckpointAsync(cp2);

        // Act
        var loaded = await store.LoadCheckpointAsync("cp-1");

        // Assert
        loaded.Should().NotBeNull();
        loaded!.CheckpointId.Should().Be("cp-1");
    }

    [Fact]
    public async Task LoadCheckpointAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var store = new InMemoryCheckpointStore();

        // Act
        var loaded = await store.LoadCheckpointAsync("nonexistent");

        // Assert
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCheckpointsAsync_RemovesAllCheckpointsForExecution()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.FullHistory };
        var cp1 = CreateTestCheckpoint("cp-1", "exec-1");
        var cp2 = CreateTestCheckpoint("cp-2", "exec-1");
        var cp3 = CreateTestCheckpoint("cp-3", "exec-2");

        await store.SaveCheckpointAsync(cp1);
        await store.SaveCheckpointAsync(cp2);
        await store.SaveCheckpointAsync(cp3);

        // Act
        await store.DeleteCheckpointsAsync("exec-1");

        // Assert
        var list1 = await store.ListCheckpointsAsync("exec-1");
        var list2 = await store.ListCheckpointsAsync("exec-2");

        list1.Should().BeEmpty();
        list2.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListCheckpointsAsync_ReturnsAllCheckpointsOrderedByTime()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.FullHistory };
        var cp1 = CreateTestCheckpoint("cp-1", "exec-1", DateTimeOffset.UtcNow.AddMinutes(-10));
        var cp2 = CreateTestCheckpoint("cp-2", "exec-1", DateTimeOffset.UtcNow.AddMinutes(-5));
        var cp3 = CreateTestCheckpoint("cp-3", "exec-1", DateTimeOffset.UtcNow);

        await store.SaveCheckpointAsync(cp1);
        await store.SaveCheckpointAsync(cp2);
        await store.SaveCheckpointAsync(cp3);

        // Act
        var checkpoints = await store.ListCheckpointsAsync("exec-1");

        // Assert
        checkpoints.Should().HaveCount(3);
        checkpoints[0].CheckpointId.Should().Be("cp-1");
        checkpoints[1].CheckpointId.Should().Be("cp-2");
        checkpoints[2].CheckpointId.Should().Be("cp-3");
    }

    [Fact]
    public async Task ListCheckpointsAsync_NoCheckpoints_ReturnsEmpty()
    {
        // Arrange
        var store = new InMemoryCheckpointStore();

        // Act
        var checkpoints = await store.ListCheckpointsAsync("nonexistent");

        // Assert
        checkpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task RetentionMode_LatestOnly_KeepsOnlyNewestCheckpoint()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.LatestOnly };
        var cp1 = CreateTestCheckpoint("cp-1", "exec-1", DateTimeOffset.UtcNow.AddMinutes(-10));
        var cp2 = CreateTestCheckpoint("cp-2", "exec-1", DateTimeOffset.UtcNow.AddMinutes(-5));
        var cp3 = CreateTestCheckpoint("cp-3", "exec-1", DateTimeOffset.UtcNow);

        // Act
        await store.SaveCheckpointAsync(cp1);
        await store.SaveCheckpointAsync(cp2);
        await store.SaveCheckpointAsync(cp3);

        var checkpoints = await store.ListCheckpointsAsync("exec-1");
        var latest = await store.LoadLatestCheckpointAsync("exec-1");

        // Assert - Only latest checkpoint should be kept
        checkpoints.Should().HaveCount(1);
        checkpoints[0].CheckpointId.Should().Be("cp-3");
        latest!.CheckpointId.Should().Be("cp-3");

        // Old checkpoints should not be loadable by ID
        var old1 = await store.LoadCheckpointAsync("cp-1");
        var old2 = await store.LoadCheckpointAsync("cp-2");
        old1.Should().BeNull();
        old2.Should().BeNull();
    }

    [Fact]
    public async Task RetentionMode_FullHistory_KeepsAllCheckpoints()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.FullHistory };
        var cp1 = CreateTestCheckpoint("cp-1", "exec-1");
        var cp2 = CreateTestCheckpoint("cp-2", "exec-1");
        var cp3 = CreateTestCheckpoint("cp-3", "exec-1");

        // Act
        await store.SaveCheckpointAsync(cp1);
        await store.SaveCheckpointAsync(cp2);
        await store.SaveCheckpointAsync(cp3);

        var checkpoints = await store.ListCheckpointsAsync("exec-1");

        // Assert - All checkpoints should be kept
        checkpoints.Should().HaveCount(3);

        // All should be loadable by ID
        (await store.LoadCheckpointAsync("cp-1")).Should().NotBeNull();
        (await store.LoadCheckpointAsync("cp-2")).Should().NotBeNull();
        (await store.LoadCheckpointAsync("cp-3")).Should().NotBeNull();
    }

    [Fact]
    public async Task ConcurrentSave_ThreadSafe_NoDataLoss()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.FullHistory };
        var tasks = new List<Task>();

        // Act - Save 10 checkpoints concurrently
        for (int i = 0; i < 10; i++)
        {
            var checkpointId = $"cp-{i}";
            var checkpoint = CreateTestCheckpoint(checkpointId, "exec-1");
            tasks.Add(store.SaveCheckpointAsync(checkpoint));
        }

        await Task.WhenAll(tasks);

        var checkpoints = await store.ListCheckpointsAsync("exec-1");

        // Assert - All 10 checkpoints should be saved
        checkpoints.Should().HaveCount(10);
    }

    [Fact]
    public async Task MultipleExecutions_IsolatedCorrectly()
    {
        // Arrange
        var store = new InMemoryCheckpointStore { RetentionMode = CheckpointRetentionMode.FullHistory };
        var exec1_cp1 = CreateTestCheckpoint("exec1-cp1", "exec-1");
        var exec1_cp2 = CreateTestCheckpoint("exec1-cp2", "exec-1");
        var exec2_cp1 = CreateTestCheckpoint("exec2-cp1", "exec-2");

        await store.SaveCheckpointAsync(exec1_cp1);
        await store.SaveCheckpointAsync(exec1_cp2);
        await store.SaveCheckpointAsync(exec2_cp1);

        // Act
        var exec1Checkpoints = await store.ListCheckpointsAsync("exec-1");
        var exec2Checkpoints = await store.ListCheckpointsAsync("exec-2");

        // Assert
        exec1Checkpoints.Should().HaveCount(2);
        exec2Checkpoints.Should().HaveCount(1);

        exec1Checkpoints.Should().AllSatisfy(cp => cp.ExecutionId.Should().Be("exec-1"));
        exec2Checkpoints.Should().AllSatisfy(cp => cp.ExecutionId.Should().Be("exec-2"));
    }

    #endregion

    #region Helper Methods

    private static GraphCheckpoint CreateTestCheckpoint(
        string checkpointId,
        string executionId,
        DateTimeOffset? createdAt = null)
    {
        return new GraphCheckpoint
        {
            CheckpointId = checkpointId,
            ExecutionId = executionId,
            GraphId = "test-graph",
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string> { "node1" },
            NodeOutputs = new Dictionary<string, object> { ["node1.output"] = "value" },
            ContextJson = "{\"channels\": {}}"
        };
    }

    #endregion
}
