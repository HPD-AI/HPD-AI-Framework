using FluentAssertions;
using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Validation;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Extensions;
using HPDAgent.Graph.Core.Validation;
using Xunit;

namespace HPDAgent.Graph.Tests.Integration;

/// <summary>
/// Integration tests that combine all 5 universal workflow primitives:
/// 1. Input Validation
/// 2. Retry Jitter
/// 3. Declarative Caching
/// 4. Result TTL
/// 5. Metadata Query Helpers
/// </summary>
public class UniversalPrimitivesIntegrationTests
{
    [Fact]
    public void NodeBuilder_CanConfigureAllPrimitives()
    {
        // Create a node with all 5 primitives configured
        var graph = new GraphBuilder()
            .WithId("test-graph")
            .WithName("Universal Primitives Test")
            .AddNode("data-fetcher", "DataFetcher", NodeType.Handler, "DataFetcherHandler", n => n
                // Phase 1: Input Validation
                .RequireInput<string>("apiUrl",
                    InputValidators.Url(),
                    "API endpoint URL")
                .RequireInput<int>("maxResults",
                    InputValidators.Range(1, 100),
                    "Maximum number of results")
                .OptionalInput<TimeSpan>("timeout",
                    TimeSpan.FromSeconds(30),
                    description: "Request timeout")

                // Phase 2: Retry Jitter
                .WithRetryPolicy(new RetryPolicy
                {
                    MaxAttempts = 5,
                    InitialDelay = TimeSpan.FromSeconds(1),
                    MaxDelay = TimeSpan.FromMinutes(5),
                    Strategy = BackoffStrategy.JitteredExponential
                })

                // Phase 3 & 4: Declarative Caching with TTL
                .WithCache(new CacheOptions
                {
                    Strategy = CacheKeyStrategy.InputsAndCode,
                    Ttl = TimeSpan.FromHours(24),
                    Invalidation = CacheInvalidation.OnCodeChange
                })

                // Phase 5: Metadata for querying
                .WithMetadata("team", "data-platform")
                .WithMetadata("cost-center", "analytics")
                .WithMetadata("priority", "high"))
            .Build();

        var node = graph.Nodes.First(n => n.Id == "data-fetcher");

        // Verify Phase 1: Input Validation
        node.InputSchemas.Should().NotBeNull();
        node.InputSchemas.Should().HaveCount(3);
        node.InputSchemas.Should().ContainKey("apiUrl");
        node.InputSchemas.Should().ContainKey("maxResults");
        node.InputSchemas.Should().ContainKey("timeout");

        node.InputSchemas!["apiUrl"].Type.Should().Be(typeof(string));
        node.InputSchemas["apiUrl"].Required.Should().BeTrue();
        node.InputSchemas["apiUrl"].Validator.Should().NotBeNull();

        node.InputSchemas["timeout"].Required.Should().BeFalse();
        node.InputSchemas["timeout"].DefaultValue.Should().Be(TimeSpan.FromSeconds(30));

        // Verify Phase 2: Retry Jitter
        node.RetryPolicy.Should().NotBeNull();
        node.RetryPolicy!.Strategy.Should().Be(BackoffStrategy.JitteredExponential);
        node.RetryPolicy.MaxAttempts.Should().Be(5);

        // Verify Phase 3 & 4: Caching with TTL
        node.Cache.Should().NotBeNull();
        node.Cache!.Strategy.Should().Be(CacheKeyStrategy.InputsAndCode);
        node.Cache.Ttl.Should().Be(TimeSpan.FromHours(24));
        node.Cache.Invalidation.Should().Be(CacheInvalidation.OnCodeChange);

        // Verify Phase 5: Metadata
        node.Metadata.Should().NotBeNull();
        node.GetMetadata("team").Should().Be("data-platform");
        node.GetMetadata("cost-center").Should().Be("analytics");
        node.GetMetadata("priority").Should().Be("high");
    }

    [Fact]
    public void MultipleNodes_CanBeFilteredByMetadata()
    {
        var graph = new GraphBuilder()
            .WithId("multi-node-graph")
            .WithName("Multi-Node Graph")
            .AddNode("ml-train", "ML Trainer", NodeType.Handler, "MLTrainer", n => n
                .WithMetadata("team", "ml")
                .WithMetadata("priority", "high"))
            .AddNode("ml-serve", "ML Server", NodeType.Handler, "MLServer", n => n
                .WithMetadata("team", "ml")
                .WithMetadata("priority", "medium"))
            .AddNode("batch-process", "Batch Processor", NodeType.Handler, "BatchProcessor", n => n
                .WithMetadata("team", "data")
                .WithMetadata("priority", "low"))
            .AddNode("api-handler", "API Handler", NodeType.Handler, "ApiHandler", n => n
                .WithMetadata("team", "backend")
                .WithMetadata("priority", "high"))
            .Build();

        // Test Phase 5: Metadata queries
        var mlNodes = graph.Nodes.WithMetadata("team", "ml");
        mlNodes.Should().HaveCount(2);

        var highPriorityNodes = graph.Nodes.WithMetadataMatching("priority",
            p => p == "high" || p == "critical");
        highPriorityNodes.Should().HaveCount(2);

        var allTeams = graph.Nodes.GetMetadataValues("team");
        allTeams.Should().Contain("ml", "data", "backend");
    }

    [Fact]
    public void ValidationSchema_WithCustomValidator_WorksCorrectly()
    {
        var schema = new InputSchema
        {
            Type = typeof(string),
            Required = true,
            Validator = InputValidators.Email(),
            Description = "User email address"
        };

        // Valid email
        var validResult = schema.Validator!.Validate("email", "user@example.com");
        validResult.IsValid.Should().BeTrue();

        // Invalid email
        var invalidResult = schema.Validator.Validate("email", "not-an-email");
        invalidResult.IsValid.Should().BeFalse();
        invalidResult.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void RetryPolicy_JitteredBackoff_ProducesVariedDelays()
    {
        var policy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.JitteredExponential
        };

        var delays = new List<double>();
        for (int i = 0; i < 50; i++)
        {
            delays.Add(policy.GetDelay(1).TotalMilliseconds);
        }

        // Should have variation (jitter)
        delays.Distinct().Count().Should().BeGreaterThan(10);

        // All should be within jitter range (50-150%)
        delays.Should().OnlyContain(d => d >= 500 && d <= 1500);
    }

    [Fact]
    public void CacheOptions_AllStrategies_CanBeConfigured()
    {
        // Test all CacheKeyStrategy values
        var inputsOnlyCache = new CacheOptions
        {
            Strategy = CacheKeyStrategy.Inputs,
            Ttl = TimeSpan.FromHours(1)
        };

        var inputsAndCodeCache = new CacheOptions
        {
            Strategy = CacheKeyStrategy.InputsAndCode,
            Ttl = TimeSpan.FromHours(24)
        };

        var fullCache = new CacheOptions
        {
            Strategy = CacheKeyStrategy.InputsCodeAndConfig,
            Ttl = TimeSpan.FromDays(7)
        };

        inputsOnlyCache.Strategy.Should().Be(CacheKeyStrategy.Inputs);
        inputsAndCodeCache.Strategy.Should().Be(CacheKeyStrategy.InputsAndCode);
        fullCache.Strategy.Should().Be(CacheKeyStrategy.InputsCodeAndConfig);
    }

    [Fact]
    public void Node_WithoutPrimitives_HasNullProperties()
    {
        var node = new Node
        {
            Id = "simple-node",
            Name = "Simple Handler",
            Type = NodeType.Handler
        };

        // All primitives should be null (opt-in, zero overhead)
        node.InputSchemas.Should().BeNull();
        node.Cache.Should().BeNull();
        // RetryPolicy and Metadata have default values, not null
    }

    [Fact]
    public void CompleteWorkflow_AllPrimitivesCombined()
    {
        // Build a realistic workflow with all primitives
        var graph = new GraphBuilder()
            .WithId("production-workflow")
            .WithName("Production Data Pipeline")

            // Node 1: Data ingestion with validation and caching
            .AddNode("ingest", "Data Ingestion", NodeType.Handler, "IngestHandler", n => n
                .RequireInput<string>("sourceUrl", InputValidators.Url())
                .RequireInput<string>("format", InputValidators.Enum<DataFormat>())
                .WithCache(new CacheOptions
                {
                    Strategy = CacheKeyStrategy.Inputs,
                    Ttl = TimeSpan.FromHours(6)
                })
                .WithRetryPolicy(new RetryPolicy
                {
                    MaxAttempts = 3,
                    InitialDelay = TimeSpan.FromSeconds(5),
                    Strategy = BackoffStrategy.JitteredExponential
                })
                .WithMetadata("stage", "ingestion")
                .WithMetadata("team", "data-platform"))

            // Node 2: Data transformation
            .AddNode("transform", "Data Transform", NodeType.Handler, "TransformHandler", n => n
                .RequireInput<int>("batchSize", InputValidators.Range(100, 10000))
                .WithMetadata("stage", "processing")
                .WithMetadata("team", "data-platform"))

            // Node 3: Model inference with caching
            .AddNode("infer", "ML Inference", NodeType.Handler, "InferenceHandler", n => n
                .RequireInput<string>("modelId")
                .OptionalInput<float>("threshold", 0.5f)
                .WithCache(new CacheOptions
                {
                    Strategy = CacheKeyStrategy.InputsAndCode,
                    Ttl = TimeSpan.FromHours(1)
                })
                .WithMetadata("stage", "inference")
                .WithMetadata("team", "ml"))

            .Build();

        // Verify graph structure (3 defined nodes + 2 auto-generated START/END nodes)
        graph.Nodes.Should().HaveCount(5);

        // Query by metadata (Phase 5)
        var dataTeamNodes = graph.Nodes.WithMetadata("team", "data-platform");
        dataTeamNodes.Should().HaveCount(2);

        var inferenceNodes = graph.Nodes.WithMetadata("stage", "inference");
        inferenceNodes.Should().HaveCount(1);

        // Verify all nodes have appropriate primitives configured
        var ingestNode = graph.Nodes.First(n => n.Id == "ingest");
        ingestNode.InputSchemas.Should().NotBeNull();
        ingestNode.Cache.Should().NotBeNull();
        ingestNode.RetryPolicy.Should().NotBeNull();

        var inferNode = graph.Nodes.First(n => n.Id == "infer");
        inferNode.InputSchemas.Should().ContainKey("threshold");
        inferNode.InputSchemas!["threshold"].DefaultValue.Should().Be(0.5f);
    }

    private enum DataFormat
    {
        Json,
        Csv,
        Parquet
    }
}
