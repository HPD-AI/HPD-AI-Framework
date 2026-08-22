using System.Text.Json;
using HPD.Graph.Abstractions.Config;

namespace HPD.Graph.Tests.V21;

internal static class GraphConfigSerializationTests
{
    internal static GraphConfig CreateMinimalGraphConfig(string graphId = "g") => new()
    {
        GraphId = graphId,
        Name = "Graph",
        Nodes = new Dictionary<string, NodeConfig>
        {
            ["handler"] = new()
            {
                Id = "handler", Name = "Handler", Type = NodeKindConfig.Handler, HandlerName = "handler",
            },
        },
        Edges =
        [
            new EdgeConfig { From = "START", To = "handler" },
            new EdgeConfig { From = "handler", To = "END" },
        ],
    };

    internal static GraphConfig CreateFullGraphConfig() => new()
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
        IterationOptions = new IterationOptionsConfig { MaxIterations = 9, StopOnConvergence = true },
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
                    RetryableExceptionTypeNames = ["System.TimeoutException"],
                },
                ErrorPolicy = new ErrorPropagationPolicyConfig
                {
                    Mode = PropagationModeConfig.ExecuteFallback,
                    FallbackNodeId = "fallback",
                },
                SuspensionOptions = new SuspensionOptionsConfig
                {
                    ActiveWaitTimeout = TimeSpan.Zero,
                    EmitEvents = true,
                    SaveCheckpointFirst = true,
                },
                EnableCheckpointing = false,
                MaxExecutions = 5,
                MaxParallelExecutions = 2,
                OutputPortCount = 2,
                Artifacts = new ArtifactDependencyConfig
                {
                    ProducesArtifact = "documents/raw",
                    RequiresArtifacts = ["inputs/manifest"],
                },
                Partitions = new PartitionDefinitionConfig
                {
                    Type = PartitionKindConfig.Static,
                    Definition = JsonDocument.Parse("""{"keys":["us","eu"]}""").RootElement.Clone(),
                },
                PartitionDependencies = new PartitionDependencyConfig
                {
                    Type = PartitionDependencyMappingKindConfig.MonthlyFromDaily,
                },
                Cache = new CacheOptionsConfig
                {
                    Strategy = "InputsCodeAndConfig",
                    Ttl = TimeSpan.FromMinutes(10),
                    Invalidation = "OnConfigChange",
                },
                ArtifactNamespace = ["rag", "ingest"],
                InputSchemas = new Dictionary<string, InputSchemaConfig>
                {
                    ["url"] = new()
                    {
                        TypeName = "string",
                        Required = true,
                        Constraints = JsonDocument.Parse("""{"type":"url"}""").RootElement.Clone(),
                    },
                },
                Metadata = new Dictionary<string, string> { ["kind"] = "io" },
            },
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
                    CronExpression = "0 3 * * *", TimeZoneId = "UTC", Tolerance = TimeSpan.FromMinutes(2),
                },
                RetryPolicy = new EdgeRetryPolicyConfig
                {
                    RetryInterval = TimeSpan.FromSeconds(5),
                    MaxRetries = 4,
                    MaxWaitTime = TimeSpan.FromMinutes(1),
                    ExhaustedBehavior = EdgeRetryExhaustedBehaviorConfig.SkipNode,
                },
                Condition = new ConditionConfig
                {
                    Type = ConditionKindConfig.FieldEquals,
                    Field = "status",
                    Value = JsonDocument.Parse("\"ready\"").RootElement.Clone(),
                },
                Metadata = new Dictionary<string, string> { ["edge"] = "start-read" },
            },
        ],
        Metadata = new Dictionary<string, string> { ["owner"] = "platform" },
    };
}
