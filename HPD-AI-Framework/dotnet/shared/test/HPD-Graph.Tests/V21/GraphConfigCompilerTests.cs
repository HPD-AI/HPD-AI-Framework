using System.Text.Json;
using FluentAssertions;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Config;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPD.Graph.Tests.V21;

public sealed class GraphConfigCompilerTests
{
    private readonly GraphConfigCompiler _compiler = new();

    [Fact]
    public void Compile_MinimalGraph_AddsStartAndEndNodes()
    {
        var graph = _compiler.Compile(GraphConfigSerializationTests.CreateMinimalGraphConfig());

        graph.Id.Should().Be("g");
        graph.Name.Should().Be("Graph");
        graph.Nodes.Should().Contain(n => n.Id == "START" && n.Type == NodeType.Start);
        graph.Nodes.Should().Contain(n => n.Id == "END" && n.Type == NodeType.End);
        graph.Nodes.Should().Contain(n => n.Id == "handler" && n.Type == NodeType.Handler);
        graph.Edges.Should().HaveCount(2);
    }

    [Fact]
    public void Compile_PreservesGraphLevelSettings()
    {
        var config = GraphConfigSerializationTests.CreateFullGraphConfig();

        var graph = _compiler.Compile(config);

        graph.Id.Should().Be("doc-pipeline");
        graph.Version.Should().Be("1.2.3");
        graph.MaxIterations.Should().Be(42);
        graph.ExecutionTimeout.Should().Be(TimeSpan.FromMinutes(5));
        graph.CloningPolicy.Should().Be(CloningPolicy.AlwaysClone);
        graph.IterationOptions.Should().NotBeNull();
        graph.IterationOptions!.MaxIterations.Should().Be(9);
        graph.IterationOptions.UseChangeAwareIteration.Should().BeTrue();
        graph.IterationOptions.EnableAutoConvergence.Should().BeTrue();
        graph.Metadata.Should().Contain("owner", "platform");
    }

    [Theory]
    [InlineData(NodeKindConfig.Start, NodeType.Start)]
    [InlineData(NodeKindConfig.End, NodeType.End)]
    [InlineData(NodeKindConfig.Handler, NodeType.Handler)]
    [InlineData(NodeKindConfig.Router, NodeType.Router)]
    [InlineData(NodeKindConfig.SubGraph, NodeType.SubGraph)]
    [InlineData(NodeKindConfig.Map, NodeType.Map)]
    public void Compile_MapsNodeKinds(NodeKindConfig configType, NodeType runtimeType)
    {
        var config = new GraphConfig
        {
            GraphId = "node-kind",
            Name = "Node Kind",
            Nodes = new Dictionary<string, NodeConfig>
            {
                ["n"] = new()
                {
                    Id = "n",
                    Name = "N",
                    Type = configType,
                    HandlerName = configType is NodeKindConfig.Handler or NodeKindConfig.Router ? "h" : null
                }
            }
        };

        var graph = _compiler.Compile(config);

        graph.GetNode("n")!.Type.Should().Be(runtimeType);
    }

    [Fact]
    public void Compile_NodeSettings_AreMapped()
    {
        var graph = _compiler.Compile(GraphConfigSerializationTests.CreateFullGraphConfig());

        var node = graph.GetNode("read");
        node.Should().NotBeNull();
        node!.HandlerName.Should().Be("read_files");
        node.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        node.RetryPolicy!.MaxAttempts.Should().Be(3);
        node.RetryPolicy.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        node.RetryPolicy.Strategy.Should().Be(BackoffStrategy.JitteredExponential);
        node.RetryPolicy.MaxDelay.Should().Be(TimeSpan.FromSeconds(10));
        node.ErrorPolicy!.Mode.Should().Be(PropagationMode.ExecuteFallback);
        node.ErrorPolicy.FallbackNodeId.Should().Be("fallback");
        node.SuspensionOptions!.ActiveWaitTimeout.Should().Be(TimeSpan.Zero);
        node.MaxExecutions.Should().Be(5);
        node.MaxParallelExecutions.Should().Be(2);
        node.OutputPortCount.Should().Be(2);
        node.ArtifactNamespace.Should().Equal("rag", "ingest");
        node.Metadata.Should().Contain("kind", "io");
    }

    [Fact]
    public void Compile_ObjectNodeConfig_MapsPropertiesToRuntimeDictionary()
    {
        var graph = _compiler.Compile(GraphConfigSerializationTests.CreateFullGraphConfig());

        var node = graph.GetNode("read")!;

        node.Config.Should().ContainKey("path");
        ((JsonElement)node.Config["path"]).GetString().Should().Be("/tmp/docs");
        ((JsonElement)node.Config["limit"]).GetInt32().Should().Be(10);
    }

    [Theory]
    [InlineData("""["x","y"]""", JsonValueKind.Array)]
    [InlineData("\"hello\"", JsonValueKind.String)]
    [InlineData("123", JsonValueKind.Number)]
    public void Compile_NonObjectNodeConfig_MapsToValueSlot(string rawJson, JsonValueKind kind)
    {
        var config = GraphConfigSerializationTests.CreateMinimalGraphConfig() with
        {
            Nodes = new Dictionary<string, NodeConfig>
            {
                ["handler"] = new()
                {
                    Id = "handler",
                    Name = "Handler",
                    Type = NodeKindConfig.Handler,
                    HandlerName = "handler",
                    Config = JsonDocument.Parse(rawJson).RootElement.Clone()
                }
            }
        };

        var node = _compiler.Compile(config).GetNode("handler")!;

        node.Config.Should().ContainKey("$value");
        ((JsonElement)node.Config["$value"]).ValueKind.Should().Be(kind);
    }

    [Fact]
    public void Compile_EdgeSettings_AreMapped()
    {
        var graph = _compiler.Compile(GraphConfigSerializationTests.CreateFullGraphConfig());

        var edge = graph.Edges.Single(e => e.From == "START" && e.To == "read");
        edge.FromPort.Should().Be(0);
        edge.ToPort.Should().Be(1);
        edge.Priority.Should().Be(7);
        edge.Delay.Should().Be(TimeSpan.FromSeconds(2));
        edge.CloningPolicy.Should().Be(CloningPolicy.NeverClone);
        edge.Schedule!.CronExpression.Should().Be("0 3 * * *");
        edge.Schedule.TimeZone!.Id.Should().Be("UTC");
        edge.Schedule.Tolerance.Should().Be(TimeSpan.FromMinutes(2));
        edge.RetryPolicy!.RetryInterval.Should().Be(TimeSpan.FromSeconds(5));
        edge.RetryPolicy.MaxRetries.Should().Be(4);
        edge.RetryPolicy.MaxWaitTime.Should().Be(TimeSpan.FromMinutes(1));
        edge.RetryPolicy.ExhaustedBehavior.Should().Be(EdgeRetryExhaustedBehavior.SkipNode);
        edge.Metadata.Should().Contain("edge", "start-read");
    }

    [Theory]
    [MemberData(nameof(SupportedConditionMappings))]
    public void Compile_MapsSupportedConditions(ConditionKindConfig configKind, ConditionType runtimeType)
    {
        var condition = new ConditionConfig
        {
            Type = configKind,
            Field = "field",
            Value = JsonDocument.Parse("1").RootElement.Clone(),
            Pattern = configKind == ConditionKindConfig.FieldMatchesRegex ? "^a" : null
        };

        var graph = CompileSingleCondition(condition);

        graph.Edges.Single().Condition!.Type.Should().Be(runtimeType);
    }

    [Fact]
    public void Compile_CompoundAllCondition_MapsToAnd()
    {
        var graph = CompileSingleCondition(new ConditionConfig
        {
            Type = ConditionKindConfig.All,
            All =
            [
                new ConditionConfig { Type = ConditionKindConfig.FieldExists, Field = "a" },
                new ConditionConfig { Type = ConditionKindConfig.FieldNotEmpty, Field = "b" }
            ]
        });

        var condition = graph.Edges.Single().Condition!;
        condition.Type.Should().Be(ConditionType.And);
        condition.Conditions.Should().HaveCount(2);
    }

    [Fact]
    public void Compile_CompoundAnyCondition_MapsToOr()
    {
        var graph = CompileSingleCondition(new ConditionConfig
        {
            Type = ConditionKindConfig.Any,
            Any =
            [
                new ConditionConfig { Type = ConditionKindConfig.FieldExists, Field = "a" },
                new ConditionConfig { Type = ConditionKindConfig.FieldExists, Field = "b" }
            ]
        });

        graph.Edges.Single().Condition!.Type.Should().Be(ConditionType.Or);
    }

    [Fact]
    public void Compile_CompoundNotCondition_MapsToNot()
    {
        var graph = CompileSingleCondition(new ConditionConfig
        {
            Type = ConditionKindConfig.Not,
            Not = new ConditionConfig { Type = ConditionKindConfig.FieldExists, Field = "a" }
        });

        graph.Edges.Single().Condition!.Type.Should().Be(ConditionType.Not);
    }

    [Theory]
    [InlineData("", "GraphConfig.GraphId is required.")]
    [InlineData(" ", "GraphConfig.GraphId is required.")]
    public void Compile_MissingGraphId_Throws(string graphId, string message)
    {
        var config = GraphConfigSerializationTests.CreateMinimalGraphConfig() with { GraphId = graphId };

        var act = () => _compiler.Compile(config);

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void Compile_MissingName_Throws()
    {
        var config = GraphConfigSerializationTests.CreateMinimalGraphConfig() with { Name = "" };

        var act = () => _compiler.Compile(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("GraphConfig.Name is required.");
    }

    [Fact]
    public void Compile_EdgeWithMissingSource_Throws()
    {
        var config = GraphConfigSerializationTests.CreateMinimalGraphConfig() with
        {
            Edges = [new EdgeConfig { From = "missing", To = "handler" }]
        };

        var act = () => _compiler.Compile(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*missing source node 'missing'*");
    }

    [Fact]
    public void Compile_EdgeWithMissingTarget_Throws()
    {
        var config = GraphConfigSerializationTests.CreateMinimalGraphConfig() with
        {
            Edges = [new EdgeConfig { From = "START", To = "missing" }]
        };

        var act = () => _compiler.Compile(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*missing target node 'missing'*");
    }

    [Fact]
    public void ToGraph_Extension_CompilesConfig()
    {
        var graph = GraphConfigSerializationTests.CreateMinimalGraphConfig().ToGraph();

        graph.Id.Should().Be("g");
    }

    public static TheoryData<ConditionKindConfig, ConditionType> SupportedConditionMappings()
    {
        return new TheoryData<ConditionKindConfig, ConditionType>
        {
            { ConditionKindConfig.Always, ConditionType.Always },
            { ConditionKindConfig.Default, ConditionType.Default },
            { ConditionKindConfig.FieldEquals, ConditionType.FieldEquals },
            { ConditionKindConfig.FieldNotEquals, ConditionType.FieldNotEquals },
            { ConditionKindConfig.FieldGreaterThan, ConditionType.FieldGreaterThan },
            { ConditionKindConfig.FieldGreaterThanOrEqual, ConditionType.FieldGreaterThanOrEqual },
            { ConditionKindConfig.FieldLessThan, ConditionType.FieldLessThan },
            { ConditionKindConfig.FieldLessThanOrEqual, ConditionType.FieldLessThanOrEqual },
            { ConditionKindConfig.FieldContains, ConditionType.FieldContains },
            { ConditionKindConfig.FieldContainsAny, ConditionType.FieldContainsAny },
            { ConditionKindConfig.FieldContainsAll, ConditionType.FieldContainsAll },
            { ConditionKindConfig.FieldStartsWith, ConditionType.FieldStartsWith },
            { ConditionKindConfig.FieldEndsWith, ConditionType.FieldEndsWith },
            { ConditionKindConfig.FieldMatchesRegex, ConditionType.FieldMatchesRegex },
            { ConditionKindConfig.FieldExists, ConditionType.FieldExists },
            { ConditionKindConfig.FieldNotExists, ConditionType.FieldNotExists },
            { ConditionKindConfig.FieldEmpty, ConditionType.FieldIsEmpty },
            { ConditionKindConfig.FieldNotEmpty, ConditionType.FieldIsNotEmpty },
            { ConditionKindConfig.UpstreamOneSuccess, ConditionType.UpstreamOneSuccess },
            { ConditionKindConfig.UpstreamAllDone, ConditionType.UpstreamAllDone },
            { ConditionKindConfig.UpstreamAllDoneOneSuccess, ConditionType.UpstreamAllDoneOneSuccess }
        };
    }

    private RuntimeGraph CompileSingleCondition(ConditionConfig condition)
    {
        var config = GraphConfigSerializationTests.CreateMinimalGraphConfig() with
        {
            Edges =
            [
                new EdgeConfig
                {
                    From = "START",
                    To = "handler",
                    Condition = condition
                }
            ]
        };

        return _compiler.Compile(config);
    }
}
