using FluentAssertions;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Artifacts;
using Xunit;
using GraphType = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPD.Graph.Tests.Artifacts;

/// <summary>
/// Tests for ArtifactIndex - O(1) artifact producer lookup index.
/// </summary>
public class ArtifactIndexTests
{
    [Fact]
    public void BuildIndex_SingleProducer_IndexesCorrectly()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("database", "users");
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "start",
            ExitNodeId = "end",
            Nodes = new[]
            {
                new Node
                {
                    Id = "start",
                    Name = "Start",
                    Type = NodeType.Start
                },
                new Node
                {
                    Id = "extract",
                    Name = "Extract Users",
                    Type = NodeType.Handler,
                    HandlerName = "ExtractHandler",
                    ProducesArtifact = artifactKey
                },
                new Node
                {
                    Id = "end",
                    Name = "End",
                    Type = NodeType.End
                }
            },
            Edges = new[]
            {
                new Edge { From = "start", To = "extract" },
                new Edge { From = "extract", To = "end" }
            }
        };

        // Act
        index.BuildIndex(graph);

        // Assert
        var producers = index.GetProducers(artifactKey);
        producers.Should().ContainSingle()
            .Which.Should().Be("extract");
    }

    [Fact]
    public void BuildIndex_MultipleArtifacts_IndexesAll()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifact1 = ArtifactKey.FromPath("database", "users");
        var artifact2 = ArtifactKey.FromPath("database", "orders");

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "node1",
            ExitNodeId = "node2",
            Nodes = new[]
            {
                new Node
                {
                    Id = "node1",
                    Name = "Node 1",
                    Type = NodeType.Handler,
                    HandlerName = "Handler1",
                    ProducesArtifact = artifact1
                },
                new Node
                {
                    Id = "node2",
                    Name = "Node 2",
                    Type = NodeType.Handler,
                    HandlerName = "Handler2",
                    ProducesArtifact = artifact2
                }
            },
            Edges = Array.Empty<Edge>()
        };

        // Act
        index.BuildIndex(graph);

        // Assert
        index.GetProducers(artifact1).Should().ContainSingle().Which.Should().Be("node1");
        index.GetProducers(artifact2).Should().ContainSingle().Which.Should().Be("node2");
        index.ArtifactCount.Should().Be(2);
    }

    [Fact]
    public void BuildIndex_SubGraph_IndexesNestedArtifacts()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("processed", "data");

        var subGraph = new GraphType
        {
            Id = "sub-graph",
            Name = "Sub Graph",
            EntryNodeId = "processor",
            ExitNodeId = "processor",
            Nodes = new[]
            {
                new Node
                {
                    Id = "processor",
                    Name = "Processor",
                    Type = NodeType.Handler,
                    HandlerName = "ProcessorHandler",
                    ProducesArtifact = artifactKey
                }
            },
            Edges = Array.Empty<Edge>()
        };

        var graph = new GraphType
        {
            Id = "main-graph",
            Name = "Main Graph",
            EntryNodeId = "subgraph-node",
            ExitNodeId = "subgraph-node",
            Nodes = new[]
            {
                new Node
                {
                    Id = "subgraph-node",
                    Name = "SubGraph Node",
                    Type = NodeType.SubGraph,
                    SubGraph = subGraph
                }
            },
            Edges = Array.Empty<Edge>()
        };

        // Act
        index.BuildIndex(graph);

        // Assert
        var producers = index.GetProducers(artifactKey);
        producers.Should().ContainSingle()
            .Which.Should().Be("processor");
    }

    [Fact]
    public void BuildIndex_MapNode_IndexesProcessorGraphArtifacts()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("mapped", "result");

        var processorGraph = new GraphType
        {
            Id = "processor-graph",
            Name = "Processor Graph",
            EntryNodeId = "map-handler",
            ExitNodeId = "map-handler",
            Nodes = new[]
            {
                new Node
                {
                    Id = "map-handler",
                    Name = "Map Handler",
                    Type = NodeType.Handler,
                    HandlerName = "MapHandler",
                    ProducesArtifact = artifactKey
                }
            },
            Edges = Array.Empty<Edge>()
        };

        var graph = new GraphType
        {
            Id = "main-graph",
            Name = "Main Graph",
            EntryNodeId = "map-node",
            ExitNodeId = "map-node",
            Nodes = new[]
            {
                new Node
                {
                    Id = "map-node",
                    Name = "Map Node",
                    Type = NodeType.Map,
                    MapProcessorGraph = processorGraph
                }
            },
            Edges = Array.Empty<Edge>()
        };

        // Act
        index.BuildIndex(graph);

        // Assert
        var producers = index.GetProducers(artifactKey);
        producers.Should().ContainSingle()
            .Which.Should().Be("map-handler");
    }

    [Fact]
    public void GetProducers_NonExistentArtifact_ReturnsEmpty()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("nonexistent", "artifact");

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "node1",
            ExitNodeId = "node1",
            Nodes = new[]
            {
                new Node
                {
                    Id = "node1",
                    Name = "Node 1",
                    Type = NodeType.Handler,
                    HandlerName = "Handler1"
                    // No ProducesArtifact
                }
            },
            Edges = Array.Empty<Edge>()
        };

        index.BuildIndex(graph);

        // Act
        var producers = index.GetProducers(artifactKey);

        // Assert
        producers.Should().BeEmpty();
    }

    [Fact]
    public void HasProducers_ExistingArtifact_ReturnsTrue()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("database", "users");

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "node1",
            ExitNodeId = "node1",
            Nodes = new[]
            {
                new Node
                {
                    Id = "node1",
                    Name = "Node 1",
                    Type = NodeType.Handler,
                    HandlerName = "Handler1",
                    ProducesArtifact = artifactKey
                }
            },
            Edges = Array.Empty<Edge>()
        };

        index.BuildIndex(graph);

        // Act
        var hasProducers = index.HasProducers(artifactKey);

        // Assert
        hasProducers.Should().BeTrue();
    }

    [Fact]
    public void HasProducers_NonExistentArtifact_ReturnsFalse()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("nonexistent", "artifact");

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "dummy",
            ExitNodeId = "dummy",
            Nodes = Array.Empty<Node>(),
            Edges = Array.Empty<Edge>()
        };

        index.BuildIndex(graph);

        // Act
        var hasProducers = index.HasProducers(artifactKey);

        // Assert
        hasProducers.Should().BeFalse();
    }

    [Fact]
    public void GetAllArtifactKeys_MultipleArtifacts_ReturnsAll()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifact1 = ArtifactKey.FromPath("database", "users");
        var artifact2 = ArtifactKey.FromPath("database", "orders");

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "node1",
            ExitNodeId = "node2",
            Nodes = new[]
            {
                new Node
                {
                    Id = "node1",
                    Name = "Node 1",
                    Type = NodeType.Handler,
                    HandlerName = "Handler1",
                    ProducesArtifact = artifact1
                },
                new Node
                {
                    Id = "node2",
                    Name = "Node 2",
                    Type = NodeType.Handler,
                    HandlerName = "Handler2",
                    ProducesArtifact = artifact2
                }
            },
            Edges = Array.Empty<Edge>()
        };

        index.BuildIndex(graph);

        // Act
        var allKeys = index.GetAllArtifactKeys().ToList();

        // Assert
        allKeys.Should().HaveCount(2);
        allKeys.Should().Contain(artifact1);
        allKeys.Should().Contain(artifact2);
    }

    [Fact]
    public void Clear_AfterBuilding_RemovesAllEntries()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("database", "users");

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "node1",
            ExitNodeId = "node1",
            Nodes = new[]
            {
                new Node
                {
                    Id = "node1",
                    Name = "Node 1",
                    Type = NodeType.Handler,
                    HandlerName = "Handler1",
                    ProducesArtifact = artifactKey
                }
            },
            Edges = Array.Empty<Edge>()
        };

        index.BuildIndex(graph);

        // Act
        index.Clear();

        // Assert
        index.ArtifactCount.Should().Be(0);
        index.GetProducers(artifactKey).Should().BeEmpty();
    }

    [Fact]
    public void BuildIndex_Idempotent_CanCallMultipleTimes()
    {
        // Arrange
        var index = new ArtifactIndex();
        var artifactKey = ArtifactKey.FromPath("database", "users");

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Test Graph",
            EntryNodeId = "node1",
            ExitNodeId = "node1",
            Nodes = new[]
            {
                new Node
                {
                    Id = "node1",
                    Name = "Node 1",
                    Type = NodeType.Handler,
                    HandlerName = "Handler1",
                    ProducesArtifact = artifactKey
                }
            },
            Edges = Array.Empty<Edge>()
        };

        // Act
        index.BuildIndex(graph);
        index.BuildIndex(graph);  // Build again

        // Assert
        index.ArtifactCount.Should().Be(1);
        index.GetProducers(artifactKey).Should().ContainSingle();
    }
}
