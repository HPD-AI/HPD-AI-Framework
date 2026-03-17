using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Caching;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for partition-aware fingerprinting and incremental execution.
/// Verifies that partition changes are detected and trigger re-execution.
/// </summary>
public class PartitionFingerprintingTests
{
    private readonly HierarchicalFingerprintCalculator _fingerprintCalculator;
    private readonly AffectedNodeDetector _affectedNodeDetector;
    private readonly IServiceProvider _services;

    public PartitionFingerprintingTests()
    {
        _fingerprintCalculator = new HierarchicalFingerprintCalculator();
        _affectedNodeDetector = new AffectedNodeDetector(_fingerprintCalculator);
        _services = TestServiceProvider.Create();
    }

    #region Fingerprint Calculator Tests

    [Fact]
    public void FingerprintCalculator_WithPartitionHash_IncludesInFingerprint()
    {
        // Arrange
        var nodeId = "test-node";
        var inputs = new HandlerInputs();
        var upstreamHashes = new Dictionary<string, string>();
        var globalHash = "global-hash-123";
        var partitionHash = "partition-hash-abc";

        // Act
        var fingerprintWithPartition = _fingerprintCalculator.Compute(
            nodeId, inputs, upstreamHashes, globalHash, partitionHash);

        var fingerprintWithoutPartition = _fingerprintCalculator.Compute(
            nodeId, inputs, upstreamHashes, globalHash, null);

        // Assert
        fingerprintWithPartition.Should().NotBe(fingerprintWithoutPartition,
            "partition hash should affect fingerprint");
    }

    [Fact]
    public void FingerprintCalculator_SamePartitionHash_ProducesSameFingerprint()
    {
        // Arrange
        var nodeId = "test-node";
        var inputs = new HandlerInputs();
        var upstreamHashes = new Dictionary<string, string>();
        var globalHash = "global-hash-123";
        var partitionHash = "partition-hash-abc";

        // Act
        var fingerprint1 = _fingerprintCalculator.Compute(
            nodeId, inputs, upstreamHashes, globalHash, partitionHash);

        var fingerprint2 = _fingerprintCalculator.Compute(
            nodeId, inputs, upstreamHashes, globalHash, partitionHash);

        // Assert
        fingerprint1.Should().Be(fingerprint2,
            "same partition hash should produce stable fingerprint");
    }

    [Fact]
    public void FingerprintCalculator_DifferentPartitionHash_ProducesDifferentFingerprint()
    {
        // Arrange
        var nodeId = "test-node";
        var inputs = new HandlerInputs();
        var upstreamHashes = new Dictionary<string, string>();
        var globalHash = "global-hash-123";

        // Act
        var fingerprint1 = _fingerprintCalculator.Compute(
            nodeId, inputs, upstreamHashes, globalHash, "partition-hash-1");

        var fingerprint2 = _fingerprintCalculator.Compute(
            nodeId, inputs, upstreamHashes, globalHash, "partition-hash-2");

        // Assert
        fingerprint1.Should().NotBe(fingerprint2,
            "different partition hashes should produce different fingerprints");
    }

    #endregion

    #region Static Partition Change Detection

    [Fact]
    public async Task StaticPartitionChange_DetectsNewPartition()
    {
        // Arrange: Graph with static partitions ["us-east", "us-west"]
        var graphWithTwoRegions = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west")
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        // Create previous snapshot with original partitions
        var previousSnapshot = await ResolvePartitionSnapshot(graphWithTwoRegions.GetNode("processor")!.Partitions!);
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["processor"] = _fingerprintCalculator.Compute(
                    "processor",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    previousSnapshot.SnapshotHash)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>
            {
                ["processor"] = previousSnapshot
            }
        };

        // Act: Change to ["us-east", "us-west", "eu-central"]
        var graphWithThreeRegions = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west", "eu-central")
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graphWithThreeRegions,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().Contain("processor",
            "adding eu-central partition should mark node as affected");
    }

    [Fact]
    public async Task StaticPartitionUnchanged_NoAffectedNodes()
    {
        // Arrange: Graph with static partitions ["us-east", "us-west"]
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west")
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        // Create previous snapshot with same partitions
        var partitionSnapshot = await ResolvePartitionSnapshot(graph.GetNode("processor")!.Partitions!);
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["processor"] = _fingerprintCalculator.Compute(
                    "processor",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    partitionSnapshot.SnapshotHash)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>
            {
                ["processor"] = partitionSnapshot
            }
        };

        // Act: Same graph (no partition changes)
        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graph,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().NotContain("processor",
            "unchanged partitions should not mark node as affected");
    }

    #endregion

    #region Time Partition Change Detection

    [Fact]
    public async Task TimePartitionExpansion_DetectsNewDays()
    {
        // Arrange: Graph with daily partitions [Jan 1-3]
        var graphWithThreeDays = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "daily-processor",
                Name = "Daily Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = TimePartitionDefinition.Daily(
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2025, 1, 4, 0, 0, 0, TimeSpan.Zero)) // Jan 1-3
            })
            .AddEndNode()
            .AddEdge("start", "daily-processor")
            .AddEdge("daily-processor", "end")
            .Build();

        // Create previous snapshot with original partitions
        var previousSnapshot = await ResolvePartitionSnapshot(graphWithThreeDays.GetNode("daily-processor")!.Partitions!);
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["daily-processor"] = _fingerprintCalculator.Compute(
                    "daily-processor",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    previousSnapshot.SnapshotHash)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>
            {
                ["daily-processor"] = previousSnapshot
            }
        };

        // Act: Expand to [Jan 1-5]
        var graphWithFiveDays = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "daily-processor",
                Name = "Daily Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = TimePartitionDefinition.Daily(
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero)) // Jan 1-5
            })
            .AddEndNode()
            .AddEdge("start", "daily-processor")
            .AddEdge("daily-processor", "end")
            .Build();

        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graphWithFiveDays,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().Contain("daily-processor",
            "expanding time range should mark node as affected");
    }

    [Fact]
    public async Task TimePartitionUnchanged_NoAffectedNodes()
    {
        // Arrange: Graph with daily partitions [Jan 1-3]
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "daily-processor",
                Name = "Daily Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = TimePartitionDefinition.Daily(
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2025, 1, 4, 0, 0, 0, TimeSpan.Zero))
            })
            .AddEndNode()
            .AddEdge("start", "daily-processor")
            .AddEdge("daily-processor", "end")
            .Build();

        // Create previous snapshot with same partitions
        var partitionSnapshot = await ResolvePartitionSnapshot(graph.GetNode("daily-processor")!.Partitions!);
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["daily-processor"] = _fingerprintCalculator.Compute(
                    "daily-processor",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    partitionSnapshot.SnapshotHash)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>
            {
                ["daily-processor"] = partitionSnapshot
            }
        };

        // Act: Same graph (no partition changes)
        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graph,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().NotContain("daily-processor",
            "unchanged time partitions should not mark node as affected");
    }

    #endregion

    #region Downstream Propagation Tests

    [Fact]
    public async Task PartitionChange_PropagatesDownstream()
    {
        // Arrange: Graph with partitioned node A → non-partitioned node B
        var graphOriginal = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "partitioned-source",
                Name = "Partitioned Source",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west")
            })
            .AddNode(new Node
            {
                Id = "downstream",
                Name = "Downstream",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler"
            })
            .AddEndNode()
            .AddEdge("start", "partitioned-source")
            .AddEdge("partitioned-source", "downstream")
            .AddEdge("downstream", "end")
            .Build();

        // Create previous snapshot
        var previousSnapshot = await ResolvePartitionSnapshot(graphOriginal.GetNode("partitioned-source")!.Partitions!);
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["partitioned-source"] = _fingerprintCalculator.Compute(
                    "partitioned-source",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    previousSnapshot.SnapshotHash),
                ["downstream"] = _fingerprintCalculator.Compute(
                    "downstream",
                    new HandlerInputs(),
                    new Dictionary<string, string> { ["partitioned-source"] = "upstream-hash" },
                    "graph-hash",
                    null)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>
            {
                ["partitioned-source"] = previousSnapshot
            }
        };

        // Act: Add new partition to source
        var graphModified = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "partitioned-source",
                Name = "Partitioned Source",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west", "eu-central")
            })
            .AddNode(new Node
            {
                Id = "downstream",
                Name = "Downstream",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler"
            })
            .AddEndNode()
            .AddEdge("start", "partitioned-source")
            .AddEdge("partitioned-source", "downstream")
            .AddEdge("downstream", "end")
            .Build();

        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graphModified,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().Contain("partitioned-source",
            "source partition change should mark source as affected");
        affectedNodes.Should().Contain("downstream",
            "partition change should propagate to downstream nodes");
    }

    #endregion

    #region Multi-Partition Change Detection

    [Fact]
    public async Task MultiPartitionChange_DetectsChanges()
    {
        // Arrange: Multi-partition (2 days × 2 regions = 4 combinations)
        var graphOriginal = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "multi-partitioned",
                Name = "Multi Partitioned",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = MultiPartitionDefinition.Combine(
                    TimePartitionDefinition.Daily(
                        new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero)), // 2 days
                    StaticPartitionDefinition.FromKeys("us-east", "us-west") // 2 regions
                )
            })
            .AddEndNode()
            .AddEdge("start", "multi-partitioned")
            .AddEdge("multi-partitioned", "end")
            .Build();

        // Create previous snapshot
        var previousSnapshot = await ResolvePartitionSnapshot(graphOriginal.GetNode("multi-partitioned")!.Partitions!);
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["multi-partitioned"] = _fingerprintCalculator.Compute(
                    "multi-partitioned",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    previousSnapshot.SnapshotHash)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>
            {
                ["multi-partitioned"] = previousSnapshot
            }
        };

        // Act: Add one more region (2 days × 3 regions = 6 combinations)
        var graphModified = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "multi-partitioned",
                Name = "Multi Partitioned",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = MultiPartitionDefinition.Combine(
                    TimePartitionDefinition.Daily(
                        new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero)),
                    StaticPartitionDefinition.FromKeys("us-east", "us-west", "eu-central") // 3 regions
                )
            })
            .AddEndNode()
            .AddEdge("start", "multi-partitioned")
            .AddEdge("multi-partitioned", "end")
            .Build();

        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graphModified,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().Contain("multi-partitioned",
            "changing one dimension of multi-partition should mark node as affected");
    }

    #endregion

    #region Partition Removal Tests

    [Fact]
    public async Task PartitionRemoved_DetectsChange()
    {
        // Arrange: Node WITH partitions
        var graphWithPartitions = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west")
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        // Create previous snapshot with partitions
        var previousSnapshot = await ResolvePartitionSnapshot(graphWithPartitions.GetNode("processor")!.Partitions!);
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["processor"] = _fingerprintCalculator.Compute(
                    "processor",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    previousSnapshot.SnapshotHash)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>
            {
                ["processor"] = previousSnapshot
            }
        };

        // Act: Remove partitions (set to null)
        var graphWithoutPartitions = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = null // Removed partitions
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graphWithoutPartitions,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().Contain("processor",
            "removing partitions should mark node as affected");
    }

    [Fact]
    public async Task PartitionAdded_DetectsChange()
    {
        // Arrange: Node WITHOUT partitions
        var graphWithoutPartitions = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = null
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        // Create previous snapshot without partitions
        var previousGraphSnapshot = new GraphSnapshot
        {
            NodeFingerprints = new Dictionary<string, string>
            {
                ["processor"] = _fingerprintCalculator.Compute(
                    "processor",
                    new HandlerInputs(),
                    new Dictionary<string, string>(),
                    "graph-hash",
                    null)
            },
            GraphHash = "graph-hash",
            Timestamp = DateTimeOffset.UtcNow,
            PartitionSnapshots = new Dictionary<string, PartitionSnapshot>()
        };

        // Act: Add partitions
        var graphWithPartitions = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west")
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        var affectedNodes = await _affectedNodeDetector.GetAffectedNodesAsync(
            previousGraphSnapshot,
            graphWithPartitions,
            new HandlerInputs(),
            _services,
            CancellationToken.None);

        // Assert
        affectedNodes.Should().Contain("processor",
            "adding partitions should mark node as affected");
    }

    #endregion

    #region Helper Methods

    private async Task<PartitionSnapshot> ResolvePartitionSnapshot(PartitionDefinition partitionDefinition)
    {
        return await partitionDefinition.ResolveAsync(_services, CancellationToken.None);
    }

    #endregion
}
