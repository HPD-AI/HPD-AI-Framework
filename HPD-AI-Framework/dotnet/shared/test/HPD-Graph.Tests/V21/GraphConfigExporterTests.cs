using System.Text.Json;
using FluentAssertions;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Config;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPD.Graph.Tests.V21;

public sealed class GraphConfigExporterTests
{
    private readonly GraphConfigExporter _exporter = new();

    [Fact]
    public void Export_RuntimeGraph_PreservesGraphLevelSettings()
    {
        var graph = CreateRuntimeGraph();

        var config = _exporter.Export(graph);

        config.GraphId.Should().Be("runtime");
        config.GraphVersion.Should().Be("2.0.0");
        config.Name.Should().Be("Runtime Graph");
        config.EntryNodeId.Should().Be("START");
        config.ExitNodeId.Should().Be("END");
        config.MaxIterations.Should().Be(12);
        config.ExecutionTimeout.Should().Be(TimeSpan.FromMinutes(3));
        config.CloningPolicy.Should().Be(CloningPolicyConfig.NeverClone);
        config.IterationOptions!.MaxIterations.Should().Be(7);
        config.IterationOptions.EnableChangeDetection.Should().BeTrue();
        config.IterationOptions.StopOnConvergence.Should().BeFalse();
        config.Metadata.Should().Contain("owner", "runtime");
    }

    [Fact]
    public void Export_OmitsEntryAndExitNodes_FromNodeDictionary()
    {
        var config = _exporter.Export(CreateRuntimeGraph());

        config.Nodes.Should().ContainKey("work");
        config.Nodes.Should().NotContainKey("START");
        config.Nodes.Should().NotContainKey("END");
    }

    [Fact]
    public void Export_NodeSettings_AreMapped()
    {
        var config = _exporter.Export(CreateRuntimeGraph());

        var node = config.Nodes["work"];
        node.Id.Should().Be("work");
        node.Name.Should().Be("Work");
        node.Type.Should().Be(NodeKindConfig.Handler);
        node.HandlerName.Should().Be("work_handler");
        node.Timeout.Should().Be(TimeSpan.FromSeconds(20));
        node.RetryPolicy!.MaxAttempts.Should().Be(4);
        node.RetryPolicy.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        node.RetryPolicy.Strategy.Should().Be(BackoffStrategyConfig.Linear);
        node.RetryPolicy.MaxDelay.Should().Be(TimeSpan.FromSeconds(9));
        node.RetryPolicy.RetryableExceptionTypeNames.Should().Contain(t => t.Contains("TimeoutException"));
        node.ErrorPolicy!.Mode.Should().Be(PropagationModeConfig.ExecuteFallback);
        node.ErrorPolicy.FallbackNodeId.Should().Be("fallback");
        node.SuspensionOptions!.ActiveWaitTimeout.Should().Be(TimeSpan.Zero);
        node.MaxExecutions.Should().Be(3);
        node.MaxParallelExecutions.Should().Be(2);
        node.OutputPortCount.Should().Be(2);
        node.ArtifactNamespace.Should().Equal("ns", "child");
        node.Metadata.Should().Contain("kind", "test");
    }

    [Fact]
    public void Export_NodeConfigDictionary_BecomesJsonObject()
    {
        var config = _exporter.Export(CreateRuntimeGraph());

        var nodeConfig = config.Nodes["work"].Config;

        nodeConfig.Should().NotBeNull();
        nodeConfig!.Value.GetProperty("path").GetString().Should().Be("/tmp");
        nodeConfig.Value.GetProperty("count").GetInt32().Should().Be(5);
    }

    [Fact]
    public void Export_NodeValueSlot_BecomesRawJson()
    {
        var graph = CreateRuntimeGraph() with
        {
            Nodes =
            [
                StartNode(),
                new Node
                {
                    Id = "work",
                    Name = "Work",
                    Type = NodeType.Handler,
                    HandlerName = "work_handler",
                    Config = new Dictionary<string, object>
                    {
                        ["$value"] = JsonDocument.Parse("""["a","b"]""").RootElement.Clone()
                    }
                },
                EndNode()
            ]
        };

        var config = _exporter.Export(graph);

        config.Nodes["work"].Config!.Value.ValueKind.Should().Be(JsonValueKind.Array);
        config.Nodes["work"].Config!.Value.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void Export_EdgeSettings_AreMapped()
    {
        var config = _exporter.Export(CreateRuntimeGraph());

        var edge = config.Edges.Single(e => e.From == "START" && e.To == "work");
        edge.FromPort.Should().Be(1);
        edge.ToPort.Should().Be(2);
        edge.Priority.Should().Be(8);
        edge.Delay.Should().Be(TimeSpan.FromSeconds(3));
        edge.CloningPolicy.Should().Be(CloningPolicyConfig.AlwaysClone);
        edge.Schedule!.CronExpression.Should().Be("*/5 * * * *");
        edge.Schedule.TimeZoneId.Should().Be("UTC");
        edge.Schedule.Tolerance.Should().Be(TimeSpan.FromSeconds(45));
        edge.RetryPolicy!.RetryInterval.Should().Be(TimeSpan.FromSeconds(6));
        edge.RetryPolicy.MaxRetries.Should().Be(10);
        edge.RetryPolicy.MaxWaitTime.Should().Be(TimeSpan.FromMinutes(2));
        edge.RetryPolicy.ExhaustedBehavior.Should().Be(EdgeRetryExhaustedBehaviorConfig.SkipNode);
        edge.Metadata.Should().Contain("edge", "metadata");
    }

    [Theory]
    [MemberData(nameof(SupportedConditionMappings))]
    public void Export_MapsSupportedConditions(ConditionType runtimeType, ConditionKindConfig configKind)
    {
        var graph = CreateSingleConditionGraph(new EdgeCondition
        {
            Type = runtimeType,
            Field = "field",
            Value = 1
        });

        var config = _exporter.Export(graph);

        config.Edges.Single().Condition!.Type.Should().Be(configKind);
    }

    [Fact]
    public void Export_CompoundAndCondition_MapsToAll()
    {
        var graph = CreateSingleConditionGraph(new EdgeCondition
        {
            Type = ConditionType.And,
            Conditions =
            [
                new EdgeCondition { Type = ConditionType.FieldExists, Field = "a" },
                new EdgeCondition { Type = ConditionType.FieldIsNotEmpty, Field = "b" }
            ]
        });

        var condition = _exporter.Export(graph).Edges.Single().Condition!;

        condition.Type.Should().Be(ConditionKindConfig.All);
        condition.All.Should().HaveCount(2);
    }

    [Fact]
    public void Export_CompoundOrCondition_MapsToAny()
    {
        var graph = CreateSingleConditionGraph(new EdgeCondition
        {
            Type = ConditionType.Or,
            Conditions =
            [
                new EdgeCondition { Type = ConditionType.FieldExists, Field = "a" },
                new EdgeCondition { Type = ConditionType.FieldExists, Field = "b" }
            ]
        });

        _exporter.Export(graph).Edges.Single().Condition!.Type.Should().Be(ConditionKindConfig.Any);
    }

    [Fact]
    public void Export_CompoundNotCondition_MapsToNot()
    {
        var graph = CreateSingleConditionGraph(new EdgeCondition
        {
            Type = ConditionType.Not,
            Conditions = [new EdgeCondition { Type = ConditionType.FieldExists, Field = "a" }]
        });

        _exporter.Export(graph).Edges.Single().Condition!.Type.Should().Be(ConditionKindConfig.Not);
    }

    [Fact]
    public void Export_EdgeRetryPolicy_WithRuntimePredicate_Throws()
    {
        var graph = CreateRuntimeGraph() with
        {
            Edges =
            [
                new Edge
                {
                    From = "START",
                    To = "work",
                    RetryPolicy = new EdgeRetryPolicy
                    {
                        RetryInterval = TimeSpan.FromSeconds(1),
                        RetryCondition = _ => Task.FromResult(true)
                    }
                }
            ]
        };

        var act = () => _exporter.Export(graph);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*edge retry predicates cannot be exported*");
    }

    [Fact]
    public void Export_Schedule_WithRuntimePredicate_Throws()
    {
        var graph = CreateRuntimeGraph() with
        {
            Edges =
            [
                new Edge
                {
                    From = "START",
                    To = "work",
                    Schedule = new ScheduleConstraint
                    {
                        CronExpression = "* * * * *",
                        AdditionalCondition = _ => Task.FromResult(true)
                    }
                }
            ]
        };

        var act = () => _exporter.Export(graph);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*schedule predicates cannot be exported*");
    }

    [Fact]
    public void Export_ErrorPolicy_WithRuntimePredicate_Throws()
    {
        var graph = CreateRuntimeGraph() with
        {
            Nodes =
            [
                StartNode(),
                new Node
                {
                    Id = "work",
                    Name = "Work",
                    Type = NodeType.Handler,
                    HandlerName = "work_handler",
                    ErrorPolicy = new ErrorPropagationPolicy
                    {
                        Mode = PropagationMode.Isolate,
                        ShouldPropagate = _ => true
                    }
                },
                EndNode()
            ]
        };

        var act = () => _exporter.Export(graph);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*error propagation predicates cannot be exported*");
    }

    [Fact]
    public void ToConfig_Extension_ExportsGraph()
    {
        var config = CreateRuntimeGraph().ToConfig();

        config.GraphId.Should().Be("runtime");
    }

    [Fact]
    public void ExportThenCompile_RoundTripsSupportedRuntimeShape()
    {
        var graph = CreateRuntimeGraph();

        var config = graph.ToConfig();
        var roundTrip = config.ToGraph();

        roundTrip.Id.Should().Be(graph.Id);
        roundTrip.Name.Should().Be(graph.Name);
        roundTrip.Nodes.Select(n => n.Id).Should().BeEquivalentTo(graph.Nodes.Select(n => n.Id));
        roundTrip.Edges.Should().HaveCount(graph.Edges.Count);
    }

    public static TheoryData<ConditionType, ConditionKindConfig> SupportedConditionMappings()
    {
        return new TheoryData<ConditionType, ConditionKindConfig>
        {
            { ConditionType.Always, ConditionKindConfig.Always },
            { ConditionType.Default, ConditionKindConfig.Default },
            { ConditionType.FieldEquals, ConditionKindConfig.FieldEquals },
            { ConditionType.FieldNotEquals, ConditionKindConfig.FieldNotEquals },
            { ConditionType.FieldGreaterThan, ConditionKindConfig.FieldGreaterThan },
            { ConditionType.FieldGreaterThanOrEqual, ConditionKindConfig.FieldGreaterThanOrEqual },
            { ConditionType.FieldLessThan, ConditionKindConfig.FieldLessThan },
            { ConditionType.FieldLessThanOrEqual, ConditionKindConfig.FieldLessThanOrEqual },
            { ConditionType.FieldContains, ConditionKindConfig.FieldContains },
            { ConditionType.FieldContainsAny, ConditionKindConfig.FieldContainsAny },
            { ConditionType.FieldContainsAll, ConditionKindConfig.FieldContainsAll },
            { ConditionType.FieldStartsWith, ConditionKindConfig.FieldStartsWith },
            { ConditionType.FieldEndsWith, ConditionKindConfig.FieldEndsWith },
            { ConditionType.FieldMatchesRegex, ConditionKindConfig.FieldMatchesRegex },
            { ConditionType.FieldExists, ConditionKindConfig.FieldExists },
            { ConditionType.FieldNotExists, ConditionKindConfig.FieldNotExists },
            { ConditionType.FieldIsEmpty, ConditionKindConfig.FieldEmpty },
            { ConditionType.FieldIsNotEmpty, ConditionKindConfig.FieldNotEmpty },
            { ConditionType.UpstreamOneSuccess, ConditionKindConfig.UpstreamOneSuccess },
            { ConditionType.UpstreamAllDone, ConditionKindConfig.UpstreamAllDone },
            { ConditionType.UpstreamAllDoneOneSuccess, ConditionKindConfig.UpstreamAllDoneOneSuccess }
        };
    }

    private RuntimeGraph CreateRuntimeGraph()
    {
        return new RuntimeGraph
        {
            Id = "runtime",
            Name = "Runtime Graph",
            Version = "2.0.0",
            EntryNodeId = "START",
            ExitNodeId = "END",
            MaxIterations = 12,
            ExecutionTimeout = TimeSpan.FromMinutes(3),
            CloningPolicy = CloningPolicy.NeverClone,
            IterationOptions = new IterationOptions
            {
                MaxIterations = 7,
                UseChangeAwareIteration = true,
                EnableAutoConvergence = false
            },
            Metadata = new Dictionary<string, string> { ["owner"] = "runtime" },
            Nodes = [StartNode(), WorkNode(), EndNode()],
            Edges =
            [
                new Edge
                {
                    From = "START",
                    To = "work",
                    FromPort = 1,
                    ToPort = 2,
                    Priority = 8,
                    Delay = TimeSpan.FromSeconds(3),
                    CloningPolicy = CloningPolicy.AlwaysClone,
                    Schedule = new ScheduleConstraint
                    {
                        CronExpression = "*/5 * * * *",
                        TimeZone = TimeZoneInfo.Utc,
                        Tolerance = TimeSpan.FromSeconds(45)
                    },
                    RetryPolicy = new EdgeRetryPolicy
                    {
                        RetryInterval = TimeSpan.FromSeconds(6),
                        MaxRetries = 10,
                        MaxWaitTime = TimeSpan.FromMinutes(2),
                        ExhaustedBehavior = EdgeRetryExhaustedBehavior.SkipNode
                    },
                    Condition = new EdgeCondition
                    {
                        Type = ConditionType.FieldEquals,
                        Field = "status",
                        Value = "ready"
                    },
                    Metadata = new Dictionary<string, string> { ["edge"] = "metadata" }
                },
                new Edge { From = "work", To = "END" }
            ]
        };
    }

    private static RuntimeGraph CreateSingleConditionGraph(EdgeCondition condition)
    {
        return new RuntimeGraph
        {
            Id = "condition",
            Name = "Condition",
            EntryNodeId = "START",
            ExitNodeId = "END",
            Nodes = [StartNode(), WorkNode(), EndNode()],
            Edges = [new Edge { From = "START", To = "work", Condition = condition }]
        };
    }

    private static Node StartNode() => new()
    {
        Id = "START",
        Name = "Start",
        Type = NodeType.Start
    };

    private static Node EndNode() => new()
    {
        Id = "END",
        Name = "End",
        Type = NodeType.End
    };

    private static Node WorkNode() => new()
    {
        Id = "work",
        Name = "Work",
        Type = NodeType.Handler,
        HandlerName = "work_handler",
        Config = new Dictionary<string, object>
        {
            ["path"] = "/tmp",
            ["count"] = 5
        },
        Timeout = TimeSpan.FromSeconds(20),
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 4,
            InitialDelay = TimeSpan.FromSeconds(1),
            Strategy = BackoffStrategy.Linear,
            MaxDelay = TimeSpan.FromSeconds(9),
            RetryableExceptions = [typeof(TimeoutException)]
        },
        ErrorPolicy = new ErrorPropagationPolicy
        {
            Mode = PropagationMode.ExecuteFallback,
            FallbackNodeId = "fallback"
        },
        SuspensionOptions = new SuspensionOptions
        {
            ActiveWaitTimeout = TimeSpan.Zero,
            EmitEvents = true,
            SaveCheckpointFirst = true
        },
        MaxExecutions = 3,
        MaxParallelExecutions = 2,
        OutputPortCount = 2,
        ArtifactNamespace = ["ns", "child"],
        Metadata = new Dictionary<string, string> { ["kind"] = "test" }
    };
}
