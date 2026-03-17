using FluentAssertions;
using HPDAgent.Graph.Abstractions.Checkpointing;
using HPDAgent.Graph.Abstractions.Graph;
using Xunit;

namespace HPD.Graph.Tests.Checkpointing;

/// <summary>
/// Tests for node versioning in checkpoints (Primitive 3).
/// </summary>
public class NodeVersioningTests
{
    #region Node Version Tests

    [Fact]
    public void Node_DefaultVersion_ShouldBe1_0()
    {
        // Arrange & Act
        var node = new Node
        {
            Id = "test",
            Name = "Test Node",
            Type = NodeType.Handler,
            HandlerName = "TestHandler"
        };

        // Assert
        node.Version.Should().Be("1.0");
    }

    [Fact]
    public void Node_CustomVersion_ShouldBeSet()
    {
        // Arrange & Act
        var node = new Node
        {
            Id = "test",
            Name = "Test Node",
            Type = NodeType.Handler,
            HandlerName = "TestHandler",
            Version = "2.5"
        };

        // Assert
        node.Version.Should().Be("2.5");
    }

    [Fact]
    public void Node_VersionCanBeUpdated()
    {
        // Arrange
        var nodeV1 = new Node
        {
            Id = "test",
            Name = "Test Node",
            Type = NodeType.Handler,
            HandlerName = "TestHandler",
            Version = "1.0"
        };

        // Act
        var nodeV2 = nodeV1 with { Version = "2.0" };

        // Assert
        nodeV1.Version.Should().Be("1.0");
        nodeV2.Version.Should().Be("2.0");
        nodeV2.Id.Should().Be(nodeV1.Id); // Other properties preserved
    }

    #endregion

    #region GraphCheckpoint NodeStateMetadata Tests

    [Fact]
    public void GraphCheckpoint_NodeStateMetadata_ShouldDefaultToEmpty()
    {
        // Arrange & Act
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "test",
            ExecutionId = "exec1",
            GraphId = "graph1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string>(),
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}"
        };

        // Assert
        checkpoint.NodeStateMetadata.Should().NotBeNull();
        checkpoint.NodeStateMetadata.Should().BeEmpty();
    }

    [Fact]
    public void GraphCheckpoint_NodeStateMetadata_CanBePopulated()
    {
        // Arrange
        var metadata = new Dictionary<string, NodeStateMetadata>
        {
            ["node1"] = new NodeStateMetadata
            {
                NodeId = "node1",
                Version = "2.0",
                StateJson = "{\"output\":\"value\"}",
                CapturedAt = DateTimeOffset.UtcNow
            }
        };

        // Act
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "test",
            ExecutionId = "exec1",
            GraphId = "graph1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string> { "node1" },
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}",
            NodeStateMetadata = metadata
        };

        // Assert
        checkpoint.NodeStateMetadata.Should().HaveCount(1);
        checkpoint.NodeStateMetadata["node1"].Version.Should().Be("2.0");
        checkpoint.NodeStateMetadata["node1"].NodeId.Should().Be("node1");
    }

    #endregion

    #region NodeStateMetadata Tests

    [Fact]
    public void NodeStateMetadata_ShouldStoreVersionInformation()
    {
        // Arrange & Act
        var metadata = new NodeStateMetadata
        {
            NodeId = "node1",
            Version = "2.0",
            StateJson = "{\"output\":\"value\"}",
            CapturedAt = DateTimeOffset.UtcNow
        };

        // Assert
        metadata.NodeId.Should().Be("node1");
        metadata.Version.Should().Be("2.0");
        metadata.StateJson.Should().NotBeNullOrEmpty();
        metadata.CapturedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void NodeStateMetadata_DifferentVersions_ShouldBeDistinct()
    {
        // Arrange & Act
        var metadataV1 = new NodeStateMetadata
        {
            NodeId = "node1",
            Version = "1.0",
            StateJson = "{}",
            CapturedAt = DateTimeOffset.UtcNow
        };

        var metadataV2 = new NodeStateMetadata
        {
            NodeId = "node1",
            Version = "2.0",
            StateJson = "{}",
            CapturedAt = DateTimeOffset.UtcNow
        };

        // Assert
        metadataV1.Version.Should().NotBe(metadataV2.Version);
        metadataV1.NodeId.Should().Be(metadataV2.NodeId);
    }

    [Fact]
    public void NodeStateMetadata_CanStoreComplexStateJson()
    {
        // Arrange
        var complexJson = @"{
            ""outputs"": {
                ""result"": ""success"",
                ""count"": 42,
                ""items"": [1, 2, 3]
            }
        }";

        // Act
        var metadata = new NodeStateMetadata
        {
            NodeId = "node1",
            Version = "1.0",
            StateJson = complexJson,
            CapturedAt = DateTimeOffset.UtcNow
        };

        // Assert
        metadata.StateJson.Should().Contain("outputs");
        metadata.StateJson.Should().Contain("result");
        metadata.StateJson.Should().Contain("count");
    }

    #endregion

    #region Integration Scenarios

    [Fact]
    public void Checkpoint_WithMultipleNodeVersions_ShouldTrackAll()
    {
        // Arrange
        var metadata = new Dictionary<string, NodeStateMetadata>
        {
            ["node1"] = new NodeStateMetadata
            {
                NodeId = "node1",
                Version = "1.0",
                StateJson = "{}",
                CapturedAt = DateTimeOffset.UtcNow
            },
            ["node2"] = new NodeStateMetadata
            {
                NodeId = "node2",
                Version = "2.0",
                StateJson = "{}",
                CapturedAt = DateTimeOffset.UtcNow
            },
            ["node3"] = new NodeStateMetadata
            {
                NodeId = "node3",
                Version = "1.5",
                StateJson = "{}",
                CapturedAt = DateTimeOffset.UtcNow
            }
        };

        // Act
        var checkpoint = new GraphCheckpoint
        {
            CheckpointId = "test",
            ExecutionId = "exec1",
            GraphId = "graph1",
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedNodes = new HashSet<string> { "node1", "node2", "node3" },
            NodeOutputs = new Dictionary<string, object>(),
            ContextJson = "{}",
            NodeStateMetadata = metadata
        };

        // Assert
        checkpoint.NodeStateMetadata.Should().HaveCount(3);
        checkpoint.NodeStateMetadata["node1"].Version.Should().Be("1.0");
        checkpoint.NodeStateMetadata["node2"].Version.Should().Be("2.0");
        checkpoint.NodeStateMetadata["node3"].Version.Should().Be("1.5");
    }

    [Fact]
    public void Checkpoint_VersionMismatchDetection_CanBeSimulated()
    {
        // Arrange - Saved checkpoint with version 1.0
        var savedMetadata = new NodeStateMetadata
        {
            NodeId = "handler1",
            Version = "1.0",
            StateJson = "{\"result\":\"old\"}",
            CapturedAt = DateTimeOffset.UtcNow
        };

        // Current node with version 2.0
        var currentNode = new Node
        {
            Id = "handler1",
            Name = "Handler",
            Type = NodeType.Handler,
            HandlerName = "TestHandler",
            Version = "2.0"
        };

        // Act - Check for version mismatch
        var versionMatches = savedMetadata.Version == currentNode.Version;

        // Assert
        versionMatches.Should().BeFalse("versions should not match");
        savedMetadata.Version.Should().Be("1.0");
        currentNode.Version.Should().Be("2.0");
    }

    #endregion
}
