using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Caching;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Xunit;

namespace HPD.Graph.Tests.Artifacts;

/// <summary>
/// Integration tests for artifact orchestration with GraphOrchestrator.
/// Tests the full flow: node execution → artifact registration → lineage tracking.
/// </summary>
public class ArtifactOrchestrationIntegrationTests
{
    [Fact]
    public async Task Execute_NodeWithProducesArtifact_RegistersArtifact()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var artifactKey = ArtifactKey.FromPath("database", "users");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "extract",
                Name = "Extract Users",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifactKey
            })
            .AddEndNode()
            .AddEdge("start", "extract")
            .AddEdge("extract", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("exec-1", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore: null,
            fingerprintCalculator: new HierarchicalFingerprintCalculator(),
            checkpointStore: null,
            defaultSuspensionOptions: null,
            affectedNodeDetector: null,
            snapshotStore: null,
            artifactRegistry: artifactRegistry
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Wait for background artifact registration to complete (fire-and-forget in orchestrator)
        await Task.Delay(100);

        // Assert
        context.ShouldHaveCompletedNode("extract");

        // Artifact should be registered
        var latestVersion = await artifactRegistry.GetLatestVersionAsync(artifactKey);
        latestVersion.Should().NotBeNull();

        // Metadata should include node ID
        var metadata = await artifactRegistry.GetMetadataAsync(artifactKey, latestVersion!);
        metadata.Should().NotBeNull();
        metadata!.ProducedByNodeId.Should().Be("extract");
        metadata.ExecutionId.Should().Be("exec-1");
    }

    [Fact]
    public async Task Execute_ChainedArtifacts_TracksLineage()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var rawArtifact = ArtifactKey.FromPath("raw", "users");
        var cleanArtifact = ArtifactKey.FromPath("clean", "users");

        var graph = new TestGraphBuilder()
            .WithId("etl-pipeline")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "extract",
                Name = "Extract",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = rawArtifact
            })
            .AddNode(new Node
            {
                Id = "transform",
                Name = "Transform",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = cleanArtifact,
                RequiresArtifacts = new[] { rawArtifact }
            })
            .AddEndNode()
            .AddEdge("start", "extract")
            .AddEdge("extract", "transform")
            .AddEdge("transform", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("exec-1", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            cacheStore: null,
            fingerprintCalculator: new HierarchicalFingerprintCalculator(),
            checkpointStore: null,
            defaultSuspensionOptions: null,
            affectedNodeDetector: null,
            snapshotStore: null,
            artifactRegistry: artifactRegistry
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Wait for background artifact registration to complete (fire-and-forget in orchestrator)
        await Task.Delay(100);

        // Assert
        context.ShouldHaveCompletedNode("extract");
        context.ShouldHaveCompletedNode("transform");

        // Both artifacts should be registered
        var rawVersion = await artifactRegistry.GetLatestVersionAsync(rawArtifact);
        var cleanVersion = await artifactRegistry.GetLatestVersionAsync(cleanArtifact);

        rawVersion.Should().NotBeNull();
        cleanVersion.Should().NotBeNull();

        // Clean artifact should have raw artifact in its lineage
        var lineage = await artifactRegistry.GetLineageAsync(cleanArtifact, cleanVersion!);
        lineage.Should().ContainKey(rawArtifact);
        lineage[rawArtifact].Should().Be(rawVersion);
    }

    [Fact]
    public async Task Execute_WithoutArtifactRegistry_WorksNormally()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("exec-1", graph, services);

        // Orchestrator WITHOUT artifact registry
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should execute normally
        context.ShouldHaveCompletedNode("step1");
    }

    [Fact]
    public async Task Execute_NodeWithoutProducesArtifact_NoRegistration()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddHandlerNode("step1", "SuccessHandler")  // No ProducesArtifact
            .AddEndNode()
            .AddEdge("start", "step1")
            .AddEdge("step1", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("exec-1", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            artifactRegistry: artifactRegistry
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Wait for background artifact registration to complete (fire-and-forget in orchestrator)
        await Task.Delay(100);

        // Assert
        context.ShouldHaveCompletedNode("step1");

        // No artifacts should be registered
        var artifacts = new List<ArtifactKey>();
        await foreach (var artifact in artifactRegistry.ListArtifactsAsync())
        {
            artifacts.Add(artifact);
        }
        artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_MultipleExecutions_UpdatesVersions()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var artifactKey = ArtifactKey.FromPath("database", "users");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer",
                Name = "Producer",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifactKey
            })
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var fingerprintCalc = new HierarchicalFingerprintCalculator();

        // Act - First execution with orchestrator instance 1
        var orchestrator1 = new GraphOrchestrator<GraphContext>(
            services,
            fingerprintCalculator: fingerprintCalc,
            artifactRegistry: artifactRegistry
        );

        var context1 = new GraphContext("exec-1", graph, services);
        await orchestrator1.ExecuteAsync(context1);

        // Wait for background artifact registration to complete (fire-and-forget in orchestrator)
        await Task.Delay(100);

        var version1 = await artifactRegistry.GetLatestVersionAsync(artifactKey);

        // Act - Second execution with orchestrator instance 2
        // Same graph, but different handler produces different output each time
        var orchestrator2 = new GraphOrchestrator<GraphContext>(
            services,
            fingerprintCalculator: fingerprintCalc,
            artifactRegistry: artifactRegistry
        );

        var context2 = new GraphContext("exec-2", graph, services);
        await orchestrator2.ExecuteAsync(context2);

        // Wait for background artifact registration to complete (fire-and-forget in orchestrator)
        await Task.Delay(100);

        var version2 = await artifactRegistry.GetLatestVersionAsync(artifactKey);

        // Assert
        version1.Should().NotBeNull();
        version2.Should().NotBeNull();

        // In content-addressable versioning, same inputs produce the same version
        version1.Should().Be(version2, "same graph and inputs should produce same fingerprint");

        // The latest version pointer should be updated to the most recent execution
        var latestMetadata = await artifactRegistry.GetMetadataAsync(artifactKey, version2!);
        latestMetadata.Should().NotBeNull();
        latestMetadata!.ExecutionId.Should().Be("exec-2", "latest version should point to most recent execution");

        // Both executions registered the same version (content-addressable property)
        // This verifies that artifact registry correctly handles idempotent registrations
    }

    [Fact]
    public async Task BuildIndex_DeclaresArtifacts_IndexBuiltCorrectly()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var artifactKey = ArtifactKey.FromPath("database", "users");

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer",
                Name = "Producer",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifactKey
            })
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("exec-1", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            artifactRegistry: artifactRegistry
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Wait for background artifact registration to complete (fire-and-forget in orchestrator)
        await Task.Delay(100);

        // Assert - After execution, artifact should be registered
        var producers = await artifactRegistry.GetProducingNodeIdsAsync(artifactKey);
        producers.Should().ContainSingle()
            .Which.Should().Be("producer");
    }

    [Fact]
    public async Task Execute_WithPartitionedArtifact_RegistersWithPartition()
    {
        // Arrange
        var artifactRegistry = new InMemoryArtifactRegistry();
        var partition = new PartitionKey { Dimensions = new[] { "2025-01-15" } };
        var artifactKey = new ArtifactKey
        {
            Path = new[] { "database", "users" },
            Partition = partition
        };

        var graph = new TestGraphBuilder()
            .WithId("test-graph")
            .AddStartNode()
            .AddNode(new Node
            {
                Id = "producer",
                Name = "Producer",
                Type = NodeType.Handler,
                HandlerName = "SuccessHandler",
                ProducesArtifact = artifactKey
            })
            .AddEndNode()
            .AddEdge("start", "producer")
            .AddEdge("producer", "end")
            .Build();

        var services = TestServiceProvider.Create();
        var context = new GraphContext("exec-1", graph, services);

        var orchestrator = new GraphOrchestrator<GraphContext>(
            services,
            fingerprintCalculator: new HierarchicalFingerprintCalculator(),
            artifactRegistry: artifactRegistry
        );

        // Act
        await orchestrator.ExecuteAsync(context);

        // Wait for background artifact registration to complete (fire-and-forget in orchestrator)
        await Task.Delay(100);

        // Assert
        var version = await artifactRegistry.GetLatestVersionAsync(artifactKey);
        version.Should().NotBeNull();

        var metadata = await artifactRegistry.GetMetadataAsync(artifactKey, version!);
        metadata.Should().NotBeNull();
        metadata!.ProducedByNodeId.Should().Be("producer");
    }
}
