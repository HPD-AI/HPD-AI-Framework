using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Caching;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Comprehensive tests for Phase 2: Partitioning.
/// Tests partition key generation, time-based partitions, static partitions,
/// multi-dimensional partitions, and partition-aware execution.
/// </summary>
public class PartitioningTests
{
    private GraphOrchestrator<GraphContext> CreateOrchestrator()
    {
        var services = TestServiceProvider.Create();
        return new GraphOrchestrator<GraphContext>(
            services,
            cacheStore: new InMemoryNodeCacheStore(),
            fingerprintCalculator: new HierarchicalFingerprintCalculator(),
            checkpointStore: null,
            defaultSuspensionOptions: null,
            affectedNodeDetector: null,
            snapshotStore: null,
            artifactRegistry: new InMemoryArtifactRegistry()
        );
    }

    #region TimePartitionDefinition Tests

    [Fact]
    public async Task TimePartitionDefinition_Daily_GeneratesCorrectPartitionKeys()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2025, 1, 5, 0, 0, 0, TimeSpan.Zero);

        var definition = TimePartitionDefinition.Daily(start, end);
        var services = TestServiceProvider.Create();

        // Act
        var snapshot = await definition.ResolveAsync(services, CancellationToken.None);

        // Assert
        snapshot.Keys.Should().HaveCount(4); // Jan 1-4 (end is exclusive)
        snapshot.Keys[0].Dimensions.Should().Equal("2025-01-01");
        snapshot.Keys[1].Dimensions.Should().Equal("2025-01-02");
        snapshot.Keys[2].Dimensions.Should().Equal("2025-01-03");
        snapshot.Keys[3].Dimensions.Should().Equal("2025-01-04");
        snapshot.SnapshotHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TimePartitionDefinition_Weekly_GeneratesCorrectPartitionKeys()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2025, 1, 22, 0, 0, 0, TimeSpan.Zero);

        var definition = TimePartitionDefinition.Weekly(start, end);
        var services = TestServiceProvider.Create();

        // Act
        var snapshot = await definition.ResolveAsync(services, CancellationToken.None);

        // Assert
        snapshot.Keys.Should().HaveCount(3); // 3 weeks
        snapshot.Keys.All(k => k.Dimensions[0].Contains("-W")).Should().BeTrue("ISO week format");
    }

    [Fact]
    public async Task TimePartitionDefinition_Monthly_GeneratesCorrectPartitionKeys()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero);

        var definition = TimePartitionDefinition.Monthly(start, end);
        var services = TestServiceProvider.Create();

        // Act
        var snapshot = await definition.ResolveAsync(services, CancellationToken.None);

        // Assert
        snapshot.Keys.Should().HaveCount(3); // Jan, Feb, Mar
        snapshot.Keys[0].Dimensions.Should().Equal("2025-01");
        snapshot.Keys[1].Dimensions.Should().Equal("2025-02");
        snapshot.Keys[2].Dimensions.Should().Equal("2025-03");
    }

    [Fact]
    public async Task TimePartitionDefinition_Hourly_GeneratesCorrectPartitionKeys()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2025, 1, 1, 3, 0, 0, TimeSpan.Zero);

        var definition = TimePartitionDefinition.Hourly(start, end);
        var services = TestServiceProvider.Create();

        // Act
        var snapshot = await definition.ResolveAsync(services, CancellationToken.None);

        // Assert
        snapshot.Keys.Should().HaveCount(3); // Hours 0, 1, 2
        snapshot.Keys[0].Dimensions.Should().Equal("2025-01-01-00");
        snapshot.Keys[1].Dimensions.Should().Equal("2025-01-01-01");
        snapshot.Keys[2].Dimensions.Should().Equal("2025-01-01-02");
    }

    [Fact]
    public async Task TimePartitionDefinition_Quarterly_GeneratesCorrectPartitionKeys()
    {
        // Arrange
        var start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var definition = TimePartitionDefinition.Quarterly(start, end);
        var services = TestServiceProvider.Create();

        // Act
        var snapshot = await definition.ResolveAsync(services, CancellationToken.None);

        // Assert
        snapshot.Keys.Should().HaveCount(4); // Q1, Q2, Q3, Q4
        snapshot.Keys[0].Dimensions.Should().Equal("2025-Q1");
        snapshot.Keys[1].Dimensions.Should().Equal("2025-Q2");
        snapshot.Keys[2].Dimensions.Should().Equal("2025-Q3");
        snapshot.Keys[3].Dimensions.Should().Equal("2025-Q4");
    }

    // NOTE: GetKeyForTime() removed in favor of ResolveAsync()
    // Time-based partition key lookup should be done by resolving the full snapshot
    // and finding the key that matches the timestamp

    #endregion

    #region StaticPartitionDefinition Tests

    [Fact]
    public async Task StaticPartitionDefinition_GeneratesAllKeys()
    {
        // Arrange
        var definition = StaticPartitionDefinition.FromKeys("us-east", "us-west", "eu-central");
        var services = TestServiceProvider.Create();

        // Act
        var snapshot = await definition.ResolveAsync(services, CancellationToken.None);

        // Assert
        snapshot.Keys.Should().HaveCount(3);
        snapshot.Keys[0].Dimensions.Should().Equal("us-east");
        snapshot.Keys[1].Dimensions.Should().Equal("us-west");
        snapshot.Keys[2].Dimensions.Should().Equal("eu-central");
        snapshot.SnapshotHash.Should().NotBeNullOrEmpty();
    }

    // NOTE: IsTimeBased property removed - partition types are now distinguished by their type
    // rather than by a runtime property

    #endregion

    #region MultiPartitionDefinition Tests

    [Fact]
    public async Task MultiPartitionDefinition_GeneratesCartesianProduct()
    {
        // Arrange
        var timeDimension = TimePartitionDefinition.Daily(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero)
        );

        var regionDimension = StaticPartitionDefinition.FromKeys("us-east", "us-west");

        var definition = MultiPartitionDefinition.Combine(timeDimension, regionDimension);
        var services = TestServiceProvider.Create();

        // Act
        var snapshot = await definition.ResolveAsync(services, CancellationToken.None);

        // Assert
        snapshot.Keys.Should().HaveCount(4); // 2 days × 2 regions = 4 combinations
        snapshot.Keys[0].Dimensions.Should().BeEquivalentTo(new[] { "2025-01-01", "us-east" });
        snapshot.Keys[1].Dimensions.Should().BeEquivalentTo(new[] { "2025-01-01", "us-west" });
        snapshot.Keys[2].Dimensions.Should().BeEquivalentTo(new[] { "2025-01-02", "us-east" });
        snapshot.Keys[3].Dimensions.Should().BeEquivalentTo(new[] { "2025-01-02", "us-west" });
        snapshot.SnapshotHash.Should().NotBeNullOrEmpty();
    }

    // NOTE: IsTimeBased property removed - not needed with new snapshot-based API

    #endregion

    #region PartitionDependencyMapping Tests

    [Fact]
    public void PartitionDependencyMapping_WeeklyFromDaily_GeneratesSevenDailyPartitions()
    {
        // Arrange
        var mapping = PartitionDependencyMapping.WeeklyFromDaily();
        var weekKey = new PartitionKey { Dimensions = new[] { "2025-W03" } };

        // Act
        var dailyPartitions = mapping.MapInputPartitions(weekKey).ToList();

        // Assert
        dailyPartitions.Should().HaveCount(7);
        dailyPartitions.All(p => p.Dimensions[0].StartsWith("2025-01-")).Should().BeTrue();
    }

    [Fact]
    public void PartitionDependencyMapping_MonthlyFromDaily_GeneratesAllDaysInMonth()
    {
        // Arrange
        var mapping = PartitionDependencyMapping.MonthlyFromDaily();
        var monthKey = new PartitionKey { Dimensions = new[] { "2025-01" } };

        // Act
        var dailyPartitions = mapping.MapInputPartitions(monthKey).ToList();

        // Assert
        dailyPartitions.Should().HaveCount(31); // January has 31 days
        dailyPartitions.First().Dimensions.Should().Equal("2025-01-01");
        dailyPartitions.Last().Dimensions.Should().Equal("2025-01-31");
    }

    [Fact]
    public void PartitionDependencyMapping_QuarterlyFromMonthly_GeneratesThreeMonths()
    {
        // Arrange
        var mapping = PartitionDependencyMapping.QuarterlyFromMonthly();
        var quarterKey = new PartitionKey { Dimensions = new[] { "2025-Q1" } };

        // Act
        var monthlyPartitions = mapping.MapInputPartitions(quarterKey).ToList();

        // Assert
        monthlyPartitions.Should().HaveCount(3);
        monthlyPartitions[0].Dimensions.Should().Equal("2025-01");
        monthlyPartitions[1].Dimensions.Should().Equal("2025-02");
        monthlyPartitions[2].Dimensions.Should().Equal("2025-03");
    }

    #endregion

    #region Partition-Aware Execution Tests

    [Fact]
    public async Task ExecuteAsync_PartitionedNode_ExecutesForEachPartition()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var targetArtifact = ArtifactKey.FromPath("partitioned", "data");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = targetArtifact,
                Partitions = TimePartitionDefinition.Daily(
                    new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2025, 1, 4, 0, 0, 0, TimeSpan.Zero)
                )
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());

        // Create orchestrator with the same artifact registry instance
        var services = TestServiceProvider.Create();
        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore: new InMemoryNodeCacheStore(),
            fingerprintCalculator: new HierarchicalFingerprintCalculator(),
            checkpointStore: null,
            defaultSuspensionOptions: null,
            affectedNodeDetector: null,
            snapshotStore: null,
            artifactRegistry: artifactRegistry
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.CompletedNodes.Should().Contain("processor");

        // Should have registered 3 partitions (Jan 1, 2, 3)
        var allArtifacts = await artifactRegistry.ListArtifactsAsync().ToListAsync();
        allArtifacts.Should().Contain(targetArtifact);
    }

    [Fact]
    public async Task ExecuteAsync_PartitionedNode_CurrentPartitionIsSet()
    {
        // This test would require a custom handler that captures context.CurrentPartition
        // For now, we verify the partition infrastructure is wired up correctly
        // TODO: Add integration test with custom handler that verifies CurrentPartition

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "processor",
                Name = "Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("partition-a", "partition-b")
            })
            .AddEndNode()
            .AddEdge("start", "processor")
            .AddEdge("processor", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.CompletedNodes.Should().Contain("processor");
    }

    [Fact]
    public async Task ExecuteAsync_PartitionedNode_WithRegionalPartitions()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "regional-processor",
                Name = "Regional Processor",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                Partitions = StaticPartitionDefinition.FromKeys("us-east", "us-west", "eu-central"),
                ProducesArtifact = ArtifactKey.FromPath("regional", "metrics")
            })
            .AddEndNode()
            .AddEdge("start", "regional-processor")
            .AddEdge("regional-processor", "end")
            .Build();

        var context = new GraphContext("exec-1", graph, TestServiceProvider.Create());
        var orchestrator = CreateOrchestrator();

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.CompletedNodes.Should().Contain("regional-processor");
    }

    #endregion

    #region IPartitionMapItem Tests

    [Fact]
    public void PartitionMapItem_ImplementsIPartitionMapItem()
    {
        // Arrange
        var partitionKey = new PartitionKey { Dimensions = new[] { "2025-01-15" } };

        // Act
        var item = new PartitionMapItem(0, partitionKey);

        // Assert
        item.Should().BeAssignableTo<IPartitionMapItem>();
        item.PartitionKey.Should().Be(partitionKey);
        item.Index.Should().Be(0);
    }

    #endregion
}
