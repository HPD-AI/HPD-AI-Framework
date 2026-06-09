using System.Text.Json;
using FluentAssertions;
using HPD.Serialization;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Core.Config;

namespace HPD.Graph.Tests.V21;

public sealed class GraphConfigSerializationTests
{
    [Fact]
    public void GraphConfig_RoundTrips_WithSourceGeneratedContext()
    {
        var config = CreateFullGraphConfig();

        var json = JsonSerializer.Serialize(config, GraphConfigJsonSerializerContext.Default.GraphConfig);
        var roundTrip = JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.GraphConfig);

        roundTrip.Should().NotBeNull();
        roundTrip!.SchemaVersion.Should().Be("2.1");
        roundTrip.GraphId.Should().Be("doc-pipeline");
        roundTrip.GraphVersion.Should().Be("1.2.3");
        roundTrip.Name.Should().Be("Document Pipeline");
        roundTrip.Description.Should().Be("A v2.1 config graph");
        roundTrip.EntryNodeId.Should().Be("START");
        roundTrip.ExitNodeId.Should().Be("END");
        roundTrip.MaxIterations.Should().Be(42);
        roundTrip.ExecutionTimeout.Should().Be(TimeSpan.FromMinutes(5));
        roundTrip.Metadata.Should().Contain("owner", "platform");
        roundTrip.Nodes.Should().ContainKey("read");
        roundTrip.Edges.Should().HaveCount(1);
    }

    [Fact]
    public void Configs_Serialize_EnumsAsStrings_AndCamelCaseProperties()
    {
        var config = CreateFullGraphConfig();
        var schedule = new ScheduledGraph
        {
            GraphId = "daily",
            Schedule = new GraphScheduleConfig
            {
                CronExpression = "0 3 * * *",
                MisfirePolicy = ScheduleMisfirePolicyConfig.RunOnce
            }
        };

        var json = JsonSerializer.Serialize(config, GraphConfigJsonSerializerContext.Default.GraphConfig);
        var scheduleJson = JsonSerializer.Serialize(schedule, GraphConfigJsonSerializerContext.Default.ScheduledGraph);

        json.Should().Contain("\"graphId\"");
        json.Should().Contain("\"type\": \"Handler\"");
        json.Should().Contain("\"strategy\": \"JitteredExponential\"");
        scheduleJson.Should().Contain("\"misfirePolicy\": \"RunOnce\"");
    }

    [Fact]
    public void GraphConfig_Deserialization_RejectsUnknownProperties()
    {
        const string json = """
        {
          "schemaVersion": "2.1",
          "graphId": "unknown-fields",
          "graphVersion": "1.0.0",
          "name": "Unknown Fields",
          "notARealProperty": true,
          "nodes": {},
          "edges": []
        }
        """;

        var act = () => JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.GraphConfig);

        act.Should().Throw<JsonException>()
            .WithMessage("*notARealProperty*");
    }

    [Fact]
    public void GraphConfig_Deserialization_RejectsStringNumbers()
    {
        const string json = """
        {
          "schemaVersion": "2.1",
          "graphId": "strict-numbers",
          "graphVersion": "1.0.0",
          "name": "Strict Numbers",
          "maxIterations": "42",
          "nodes": {},
          "edges": []
        }
        """;

        var act = () => JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.GraphConfig);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("""{"enabled":true,"count":3,"name":"alpha"}""", JsonValueKind.Object)]
    [InlineData("""["a","b","c"]""", JsonValueKind.Array)]
    [InlineData("\"literal\"", JsonValueKind.String)]
    [InlineData("123", JsonValueKind.Number)]
    [InlineData("true", JsonValueKind.True)]
    public void NodeConfig_Config_Preserves_ArbitraryJson(string rawJson, JsonValueKind expectedKind)
    {
        var node = new NodeConfig
        {
            Id = "n",
            Name = "Node",
            Type = NodeKindConfig.Handler,
            HandlerName = "handler",
            Config = JsonDocument.Parse(rawJson).RootElement.Clone()
        };

        var json = JsonSerializer.Serialize(node, GraphConfigJsonSerializerContext.Default.NodeConfig);
        var roundTrip = JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.NodeConfig);

        roundTrip.Should().NotBeNull();
        roundTrip!.Config.Should().NotBeNull();
        roundTrip.Config!.Value.ValueKind.Should().Be(expectedKind);
    }

    [Fact]
    public void StoredGraph_RoundTrips()
    {
        var stored = new StoredGraph
        {
            GraphId = "g",
            Name = "Graph",
            GraphVersion = "1.0.0",
            Config = CreateMinimalGraphConfig("g"),
            CreatedAt = new DateTimeOffset(2026, 5, 1, 1, 2, 3, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 5, 2, 1, 2, 3, TimeSpan.Zero),
            Description = "stored",
            Metadata = new Dictionary<string, string> { ["team"] = "core" }
        };

        var json = JsonSerializer.Serialize(stored, GraphConfigJsonSerializerContext.Default.StoredGraph);
        var roundTrip = JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.StoredGraph);

        roundTrip.Should().BeEquivalentTo(stored, options => options.Excluding(s => s.Config));
        roundTrip!.Config.GraphId.Should().Be("g");
    }

    [Fact]
    public void StoredGraph_RoundTrips_AsYaml_WithSharedSerializer()
    {
        var stored = new StoredGraph
        {
            GraphId = "yaml-graph",
            Name = "YAML Graph",
            GraphVersion = "1.0.0",
            Config = CreateMinimalGraphConfig("yaml-graph") with
            {
                Metadata = new Dictionary<string, string> { ["format"] = "yaml" }
            },
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1)
        };

        var yaml = GraphConfigSerializer.SerializeStoredGraph(stored, HpdConfigFormat.Yaml);
        var roundTrip = GraphConfigSerializer.DeserializeStoredGraph(yaml, HpdConfigFormat.Yaml);

        yaml.Should().Contain("graphId: yaml-graph");
        roundTrip.Should().NotBeNull();
        roundTrip!.GraphId.Should().Be("yaml-graph");
        roundTrip.Config.Metadata.Should().Contain("format", "yaml");
    }

    [Fact]
    public void GraphFactory_BuildFromFile_YamlExtension_LoadsGraphConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-graph-{Guid.NewGuid():N}.yaml");
        GraphConfigSerializer.WriteConfigFile(path, CreateMinimalGraphConfig("yaml-factory"));

        try
        {
            var graph = new GraphFactory().BuildFromFile(path);

            graph.Id.Should().Be("yaml-factory");
            graph.Name.Should().Be("Graph");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StoredGraphSummary_RoundTrips()
    {
        var summary = new StoredGraphSummary
        {
            GraphId = "g",
            Name = "Graph",
            GraphVersion = "1.0.0",
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch.AddDays(1),
            Description = "summary"
        };

        var json = JsonSerializer.Serialize(summary, GraphConfigJsonSerializerContext.Default.StoredGraphSummary);
        var roundTrip = JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.StoredGraphSummary);

        roundTrip.Should().BeEquivalentTo(summary);
    }

    [Fact]
    public void ScheduledGraph_RoundTrips_WithDefaultInput()
    {
        var scheduled = new ScheduledGraph
        {
            GraphId = "daily",
            Enabled = true,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch.AddHours(1),
            LastRunAt = DateTimeOffset.UnixEpoch.AddHours(2),
            NextRunAt = DateTimeOffset.UnixEpoch.AddHours(3),
            Schedule = new GraphScheduleConfig
            {
                CronExpression = "0 3 * * *",
                TimeZoneId = "America/New_York",
                Description = "Daily",
                MaxRetries = 3,
                RetryAfter = TimeSpan.FromMinutes(5),
                Timeout = TimeSpan.FromHours(2),
                MisfirePolicy = ScheduleMisfirePolicyConfig.RunOnce,
                ConcurrencyPolicy = ScheduleConcurrencyPolicyConfig.SkipIfRunning,
                DefaultInput = JsonDocument.Parse("""{"source":"daily"}""").RootElement.Clone(),
                Metadata = new Dictionary<string, string> { ["kind"] = "batch" }
            }
        };

        var json = JsonSerializer.Serialize(scheduled, GraphConfigJsonSerializerContext.Default.ScheduledGraph);
        var roundTrip = JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.ScheduledGraph);

        roundTrip.Should().NotBeNull();
        roundTrip!.GraphId.Should().Be("daily");
        roundTrip.Schedule.DefaultInput!.Value.GetProperty("source").GetString().Should().Be("daily");
    }

    [Fact]
    public void WorkflowExecution_RoundTrips()
    {
        var execution = new WorkflowExecution
        {
            GraphId = "g",
            ExecutionId = "e",
            Status = WorkflowExecutionStatus.Suspended,
            CreatedAt = DateTimeOffset.UnixEpoch,
            StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
            CompletedAt = null,
            CurrentNodeId = "approval",
            SuspendedNodeId = "approval",
            SuspendToken = "token",
            ClaimedBy = "worker-a",
            ClaimedAt = DateTimeOffset.UnixEpoch.AddSeconds(2),
            LeaseUntil = DateTimeOffset.UnixEpoch.AddSeconds(32),
            LastHeartbeatAt = DateTimeOffset.UnixEpoch.AddSeconds(12),
            AttemptCount = 2,
            LastAttemptAt = DateTimeOffset.UnixEpoch.AddSeconds(2),
            NextAttemptAt = DateTimeOffset.UnixEpoch.AddSeconds(60),
            ErrorMessage = null
        };

        var json = JsonSerializer.Serialize(execution, GraphConfigJsonSerializerContext.Default.WorkflowExecution);
        var roundTrip = JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.WorkflowExecution);

        roundTrip.Should().BeEquivalentTo(execution);
    }

    [Fact]
    public void HandlerDescriptor_RoundTrips()
    {
        var descriptor = new HandlerDescriptor
        {
            HandlerName = "chunk_text",
            DisplayName = "Chunk Text",
            Domain = "rag",
            HandlerType = "Handlers.ChunkTextHandler",
            ContextType = "MragPipelineContext",
            Description = "Chunks text",
            Category = "Text",
            Inputs =
            [
                new SocketDescriptor
                {
                    Name = "text",
                    TypeName = "System.String",
                    Direction = SocketDirection.Input,
                    Required = true,
                    Description = "Input text"
                }
            ],
            Outputs =
            [
                new SocketDescriptor
                {
                    Name = "chunks",
                    TypeName = "TextChunk[]",
                    Direction = SocketDirection.Output,
                    Required = true
                }
            ],
            Config = new ConfigDescriptor
            {
                TypeName = "ChunkConfig",
                SchemaId = "chunk-config",
                JsonSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()
            },
            Metadata = new Dictionary<string, string> { ["icon"] = "scissors" }
        };

        var json = JsonSerializer.Serialize(descriptor, GraphConfigJsonSerializerContext.Default.HandlerDescriptor);
        var roundTrip = JsonSerializer.Deserialize(json, GraphConfigJsonSerializerContext.Default.HandlerDescriptor);

        roundTrip.Should().NotBeNull();
        roundTrip!.HandlerName.Should().Be("chunk_text");
        roundTrip.Inputs.Should().ContainSingle(i => i.Name == "text");
        roundTrip.Outputs.Should().ContainSingle(o => o.Name == "chunks");
        roundTrip.Config!.JsonSchema!.Value.GetProperty("type").GetString().Should().Be("object");
    }

    internal static GraphConfig CreateMinimalGraphConfig(string graphId = "g")
    {
        return new GraphConfig
        {
            GraphId = graphId,
            Name = "Graph",
            Nodes = new Dictionary<string, NodeConfig>
            {
                ["handler"] = new()
                {
                    Id = "handler",
                    Name = "Handler",
                    Type = NodeKindConfig.Handler,
                    HandlerName = "handler"
                }
            },
            Edges =
            [
                new EdgeConfig { From = "START", To = "handler" },
                new EdgeConfig { From = "handler", To = "END" }
            ]
        };
    }

    internal static GraphConfig CreateFullGraphConfig()
    {
        return new GraphConfig
        {
            SchemaVersion = "2.1",
            GraphId = "doc-pipeline",
            GraphVersion = "1.2.3",
            Name = "Document Pipeline",
            Description = "A v2.1 config graph",
            EntryNodeId = "START",
            ExitNodeId = "END",
            MaxIterations = 42,
            ExecutionTimeout = TimeSpan.FromMinutes(5),
            CloningPolicy = CloningPolicyConfig.AlwaysClone,
            IterationOptions = new IterationOptionsConfig
            {
                MaxIterations = 9,
                StopOnConvergence = true
            },
            Nodes = new Dictionary<string, NodeConfig>
            {
                ["read"] = new()
                {
                    Id = "read",
                    Name = "Read",
                    Type = NodeKindConfig.Handler,
                    HandlerName = "read_files",
                    Config = JsonDocument.Parse("""{"path":"/tmp/docs","limit":10}""").RootElement.Clone(),
                    Timeout = TimeSpan.FromSeconds(30),
                    RetryPolicy = new RetryPolicyConfig
                    {
                        MaxAttempts = 3,
                        InitialDelay = TimeSpan.FromSeconds(1),
                        Strategy = BackoffStrategyConfig.JitteredExponential,
                        MaxDelay = TimeSpan.FromSeconds(10),
                        RetryableExceptionTypeNames = ["System.TimeoutException"]
                    },
                    ErrorPolicy = new ErrorPropagationPolicyConfig
                    {
                        Mode = PropagationModeConfig.ExecuteFallback,
                        FallbackNodeId = "fallback"
                    },
                    SuspensionOptions = new SuspensionOptionsConfig
                    {
                        ActiveWaitTimeout = TimeSpan.Zero,
                        EmitEvents = true,
                        SaveCheckpointFirst = true
                    },
                    EnableCheckpointing = false,
                    MaxExecutions = 5,
                    MaxParallelExecutions = 2,
                    OutputPortCount = 2,
                    Artifacts = new ArtifactDependencyConfig
                    {
                        ProducesArtifact = "documents/raw",
                        RequiresArtifacts = ["inputs/manifest"]
                    },
                    Partitions = new PartitionDefinitionConfig
                    {
                        Type = PartitionKindConfig.Static,
                        Definition = JsonDocument.Parse("""{"keys":["us","eu"]}""").RootElement.Clone()
                    },
                    PartitionDependencies = new PartitionDependencyConfig
                    {
                        Type = PartitionDependencyMappingKindConfig.MonthlyFromDaily
                    },
                    Cache = new CacheOptionsConfig
                    {
                        Strategy = "InputsCodeAndConfig",
                        Ttl = TimeSpan.FromMinutes(10),
                        Invalidation = "OnConfigChange"
                    },
                    ArtifactNamespace = ["rag", "ingest"],
                    InputSchemas = new Dictionary<string, InputSchemaConfig>
                    {
                        ["url"] = new()
                        {
                            TypeName = "string",
                            Required = true,
                            Constraints = JsonDocument.Parse("""{"type":"url"}""").RootElement.Clone()
                        }
                    },
                    Metadata = new Dictionary<string, string> { ["kind"] = "io" }
                }
            },
            Edges =
            [
                new EdgeConfig
                {
                    From = "START",
                    To = "read",
                    FromPort = 0,
                    ToPort = 1,
                    Priority = 7,
                    Delay = TimeSpan.FromSeconds(2),
                    CloningPolicy = CloningPolicyConfig.NeverClone,
                    Schedule = new ScheduleConstraintConfig
                    {
                        CronExpression = "0 3 * * *",
                        TimeZoneId = "UTC",
                        Tolerance = TimeSpan.FromMinutes(2)
                    },
                    RetryPolicy = new EdgeRetryPolicyConfig
                    {
                        RetryInterval = TimeSpan.FromSeconds(5),
                        MaxRetries = 4,
                        MaxWaitTime = TimeSpan.FromMinutes(1),
                        ExhaustedBehavior = EdgeRetryExhaustedBehaviorConfig.SkipNode
                    },
                    Condition = new ConditionConfig
                    {
                        Type = ConditionKindConfig.FieldEquals,
                        Field = "status",
                        Value = JsonDocument.Parse("\"ready\"").RootElement.Clone()
                    },
                    Metadata = new Dictionary<string, string> { ["edge"] = "start-read" }
                }
            ],
            Metadata = new Dictionary<string, string> { ["owner"] = "platform" }
        };
    }
}
