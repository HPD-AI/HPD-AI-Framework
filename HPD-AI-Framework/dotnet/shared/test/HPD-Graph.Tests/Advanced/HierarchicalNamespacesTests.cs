using FluentAssertions;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Artifacts;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;

using GraphType = HPDAgent.Graph.Abstractions.Graph.Graph;
using NodeType = HPDAgent.Graph.Abstractions.Graph.Node;
using NodeTypeEnum = HPDAgent.Graph.Abstractions.Graph.NodeType;
using EdgeType = HPDAgent.Graph.Abstractions.Graph.Edge;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for Phase 5: Hierarchical Namespaces (Node.ArtifactNamespace).
/// Validates namespace validation, artifact key prefixing, and hierarchical resolution.
/// </summary>
public class HierarchicalNamespacesTests
{
    // ========== Namespace Validation Tests ==========

    [Fact]
    public void ArtifactIndex_ValidNamespace_BuildsSuccessfully()
    {
        // Arrange: Graph with valid namespace
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Valid Namespace Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "producer",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "producer",
                    Name = "Producer",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ArtifactNamespace = new[] { "team-A", "service_1", "v2" }, // Valid namespace
                    ProducesArtifact = ArtifactKey.FromPath("users")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "start", To = "producer" }
            }
        };

        var index = new ArtifactIndex();

        // Act
        var act = () => index.BuildIndex(graph);

        // Assert - No exception thrown
        act.Should().NotThrow();

        // Artifact should be registered with qualified key
        var qualifiedKey = ArtifactKey.FromPath("team-A", "service_1", "v2", "users");
        var producers = index.GetProducers(qualifiedKey);
        producers.Should().Contain("producer");
    }

    [Fact]
    public void ArtifactIndex_NamespaceExceedsMaxDepth_ThrowsException()
    {
        // Arrange: Namespace with 11 levels (max is 10)
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Namespace Max Depth Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "producer",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "producer",
                    Name = "Producer",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ArtifactNamespace = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k" }, // 11 levels
                    ProducesArtifact = ArtifactKey.FromPath("users")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "start", To = "producer" }
            }
        };

        var index = new ArtifactIndex();

        // Act & Assert
        var act = () => index.BuildIndex(graph);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*exceeds maximum of 10 levels*");
    }

    [Fact]
    public void ArtifactIndex_NamespaceWithInvalidCharacters_ThrowsException()
    {
        // Arrange: Namespace with invalid characters (spaces, special chars)
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Invalid Characters Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "producer",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "producer",
                    Name = "Producer",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ArtifactNamespace = new[] { "team@special" }, // Invalid character '@'
                    ProducesArtifact = ArtifactKey.FromPath("users")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "start", To = "producer" }
            }
        };

        var index = new ArtifactIndex();

        // Act & Assert
        var act = () => index.BuildIndex(graph);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid namespace segment*");
    }

    [Fact]
    public void ArtifactIndex_NamespaceWithConsecutiveSpecialChars_ThrowsException()
    {
        // Arrange: Namespace with consecutive hyphens
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Consecutive Special Chars Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "producer",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "producer",
                    Name = "Producer",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ArtifactNamespace = new[] { "team--alpha" }, // Consecutive hyphens
                    ProducesArtifact = ArtifactKey.FromPath("users")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "start", To = "producer" }
            }
        };

        var index = new ArtifactIndex();

        // Act & Assert
        var act = () => index.BuildIndex(graph);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*consecutive special characters*");
    }

    // ========== Artifact Key Prefixing Tests ==========

    [Fact]
    public async Task ArtifactRegistration_WithNamespace_PrefixesArtifactKey()
    {
        // Arrange: Graph with namespaced subgraph
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Namespace Prefixing Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "namespaced",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "namespaced",
                    Name = "Namespaced Producer",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ArtifactNamespace = new[] { "pipeline", "stage1" },
                    ProducesArtifact = ArtifactKey.FromPath("users")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "start", To = "namespaced" }
            }
        };

        var services = new ServiceCollection();
        var artifactRegistry = new InMemoryArtifactRegistry();
        services.AddSingleton<IArtifactRegistry>(artifactRegistry);
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(new ProducerHandler());
        var serviceProvider = services.BuildServiceProvider();

        var orchestrator = new GraphOrchestrator<GraphContext>(serviceProvider, artifactRegistry: artifactRegistry);
        var context = new GraphContext("test-exec", graph, serviceProvider);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Artifact should be registered with qualified key
        var qualifiedKey = ArtifactKey.FromPath("pipeline", "stage1", "users");
        var producingNodes = await artifactRegistry.GetProducingNodeIdsAsync(qualifiedKey);
        producingNodes.Should().Contain("namespaced");

        // Unqualified key should NOT have producers
        var unqualifiedKey = ArtifactKey.FromPath("users");
        var unqualifiedProducers = await artifactRegistry.GetProducingNodeIdsAsync(unqualifiedKey);
        unqualifiedProducers.Should().BeEmpty();
    }

    [Fact]
    public async Task ArtifactRegistration_NestedNamespaces_CombinesParentAndChild()
    {
        // Arrange: SubGraph with parent namespace + child namespace
        var innerGraph = new GraphType
        {
            Id = "inner-graph",
            Name = "Inner Graph",
            Version = "1.0",
            EntryNodeId = "inner-start",
            ExitNodeId = "inner-producer",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "inner-start",
                    Name = "Inner Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "inner-producer",
                    Name = "Inner Producer",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ArtifactNamespace = new[] { "extract" }, // Child namespace
                    ProducesArtifact = ArtifactKey.FromPath("raw-data")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "inner-start", To = "inner-producer" }
            }
        };

        var outerGraph = new GraphType
        {
            Id = "outer-graph",
            Name = "Outer Graph",
            Version = "1.0",
            EntryNodeId = "outer-start",
            ExitNodeId = "subgraph",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "outer-start",
                    Name = "Outer Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "subgraph",
                    Name = "SubGraph Node",
                    Type = NodeTypeEnum.SubGraph,
                    ArtifactNamespace = new[] { "etl", "pipeline" }, // Parent namespace
                    SubGraph = innerGraph
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "outer-start", To = "subgraph" }
            }
        };

        var index = new ArtifactIndex();
        index.BuildIndex(outerGraph);

        // Assert - Namespace should be combined: ["etl", "pipeline", "extract", "raw-data"]
        var qualifiedKey = ArtifactKey.FromPath("etl", "pipeline", "extract", "raw-data");
        var producers = index.GetProducers(qualifiedKey);
        producers.Should().Contain("inner-producer");
    }

    // ========== Hierarchical Resolution Tests ==========

    [Fact]
    public async Task ArtifactResolver_LocalScope_PrefersLocalArtifact()
    {
        // Arrange: Registry with both local and global artifacts
        var registry = new InMemoryArtifactRegistry();

        // Register global artifact
        var globalKey = ArtifactKey.FromPath("users");
        await registry.RegisterAsync(globalKey, "global-v1", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "global-producer",
            ExecutionId = "exec-1"
        });

        // Register local artifact (in namespace)
        var localKey = ArtifactKey.FromPath("pipeline", "stage1", "users");
        await registry.RegisterAsync(localKey, "local-v1", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "local-producer",
            ExecutionId = "exec-1"
        });

        // Build artifact index to track namespaces
        var namespaces = new HashSet<string> { "pipeline/stage1" };
        var resolver = new ArtifactResolver(registry, namespaces);

        // Act - Resolve from within namespace
        var currentNamespace = new[] { "pipeline", "stage1" };
        var requestedKey = ArtifactKey.FromPath("users");
        var resolvedKey = await resolver.ResolveAsync(requestedKey, currentNamespace);

        // Assert - Should resolve to local artifact
        resolvedKey.Should().Be(localKey);
    }

    [Fact]
    public async Task ArtifactResolver_ParentScope_FallsBackToParent()
    {
        // Arrange: Registry with parent artifact only
        var registry = new InMemoryArtifactRegistry();

        // Register artifact in parent namespace
        var parentKey = ArtifactKey.FromPath("pipeline", "shared-config");
        await registry.RegisterAsync(parentKey, "v1", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "config-producer",
            ExecutionId = "exec-1"
        });

        var namespaces = new HashSet<string> { "pipeline" };
        var resolver = new ArtifactResolver(registry, namespaces);

        // Act - Resolve from child namespace
        var currentNamespace = new[] { "pipeline", "stage1" };
        var requestedKey = ArtifactKey.FromPath("shared-config");
        var resolvedKey = await resolver.ResolveAsync(requestedKey, currentNamespace);

        // Assert - Should resolve to parent artifact
        resolvedKey.Should().Be(parentKey);
    }

    [Fact]
    public async Task ArtifactResolver_GlobalScope_FallsBackToGlobal()
    {
        // Arrange: Registry with global artifact only
        var registry = new InMemoryArtifactRegistry();

        // Register global artifact (no namespace)
        var globalKey = ArtifactKey.FromPath("common-utils");
        await registry.RegisterAsync(globalKey, "v1", new ArtifactMetadata
        {
            CreatedAt = DateTimeOffset.UtcNow,
            InputVersions = new Dictionary<ArtifactKey, string>(),
            ProducedByNodeId = "utils-producer",
            ExecutionId = "exec-1"
        });

        var namespaces = new HashSet<string>();
        var resolver = new ArtifactResolver(registry, namespaces);

        // Act - Resolve from within namespace
        var currentNamespace = new[] { "pipeline", "stage1" };
        var requestedKey = ArtifactKey.FromPath("common-utils");
        var resolvedKey = await resolver.ResolveAsync(requestedKey, currentNamespace);

        // Assert - Should resolve to global artifact
        resolvedKey.Should().Be(globalKey);
    }

    [Fact]
    public async Task ArtifactResolver_NotFound_ThrowsWithSearchedScopes()
    {
        // Arrange: Empty registry
        var registry = new InMemoryArtifactRegistry();
        var namespaces = new HashSet<string>();
        var resolver = new ArtifactResolver(registry, namespaces);

        // Act & Assert
        var currentNamespace = new[] { "pipeline", "stage1" };
        var requestedKey = ArtifactKey.FromPath("nonexistent");

        var act = async () => await resolver.ResolveAsync(requestedKey, currentNamespace);

        await act.Should().ThrowAsync<ArtifactNotFoundException>()
            .WithMessage("*pipeline/stage1/nonexistent*")
            .WithMessage("*pipeline/nonexistent*")
            .WithMessage("*nonexistent*");
    }

    // ========== Integration Tests ==========

    [Fact]
    public async Task EndToEnd_NamespacedSubGraphs_IsolatesArtifacts()
    {
        // Arrange: Two subgraphs with same artifact name but different namespaces
        var subgraph1 = new GraphType
        {
            Id = "subgraph-1",
            Name = "Pipeline A",
            Version = "1.0",
            EntryNodeId = "start-1",
            ExitNodeId = "producer-1",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start-1",
                    Name = "Start 1",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "producer-1",
                    Name = "Producer 1",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ProducesArtifact = ArtifactKey.FromPath("results")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "start-1", To = "producer-1" }
            }
        };

        var subgraph2 = new GraphType
        {
            Id = "subgraph-2",
            Name = "Pipeline B",
            Version = "1.0",
            EntryNodeId = "start-2",
            ExitNodeId = "producer-2",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start-2",
                    Name = "Start 2",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "producer-2",
                    Name = "Producer 2",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "ProducerHandler",
                    ProducesArtifact = ArtifactKey.FromPath("results")
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "start-2", To = "producer-2" }
            }
        };

        var mainGraph = new GraphType
        {
            Id = "main-graph",
            Name = "Main Graph",
            Version = "1.0",
            EntryNodeId = "main-start",
            ExitNodeId = "sub2",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "main-start",
                    Name = "Main Start",
                    Type = NodeTypeEnum.Start
                },
                new NodeType
                {
                    Id = "sub1",
                    Name = "SubGraph 1",
                    Type = NodeTypeEnum.SubGraph,
                    ArtifactNamespace = new[] { "pipelineA" },
                    SubGraph = subgraph1
                },
                new NodeType
                {
                    Id = "sub2",
                    Name = "SubGraph 2",
                    Type = NodeTypeEnum.SubGraph,
                    ArtifactNamespace = new[] { "pipelineB" },
                    SubGraph = subgraph2
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType { From = "main-start", To = "sub1" },
                new EdgeType { From = "sub1", To = "sub2" }
            }
        };

        var index = new ArtifactIndex();
        index.BuildIndex(mainGraph);

        // Assert - Both artifacts exist with different qualified keys
        var keyA = ArtifactKey.FromPath("pipelineA", "results");
        var keyB = ArtifactKey.FromPath("pipelineB", "results");

        var producersA = index.GetProducers(keyA);
        var producersB = index.GetProducers(keyB);

        producersA.Should().Contain("producer-1");
        producersB.Should().Contain("producer-2");

        // Unqualified key should have no producers
        var unqualifiedKey = ArtifactKey.FromPath("results");
        var unqualifiedProducers = index.GetProducers(unqualifiedKey);
        unqualifiedProducers.Should().BeEmpty();
    }
}

// ========== Test Helpers ==========

/// <summary>
/// Simple handler that produces empty output.
/// Used for testing artifact registration.
/// </summary>
internal class ProducerHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "ProducerHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Success(
            PortOutputs: new Dictionary<int, Dictionary<string, object>>(),
            Duration: TimeSpan.Zero,
            Metadata: new NodeExecutionMetadata()
        ));
    }
}
