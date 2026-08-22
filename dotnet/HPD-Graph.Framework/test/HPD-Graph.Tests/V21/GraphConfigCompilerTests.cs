using System.Text.Json;
using FluentAssertions;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Caching;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Graph;
using HPD.Graph.Abstractions.Validation;
using HPD.Graph.Core.Config;
using HPD.Graph.Core.Validation;
using RuntimeGraph = HPD.Graph.Abstractions.Graph.Graph;

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
        node.EnableCheckpointing.Should().BeFalse();
        node.MaxExecutions.Should().Be(5);
        node.MaxParallelExecutions.Should().Be(2);
        node.OutputPortCount.Should().Be(2);
        node.ProducesArtifact!.ToString().Should().Be("documents/raw");
        node.RequiresArtifacts.Should().ContainSingle(a => a.ToString() == "inputs/manifest");
        node.Partitions.Should().BeOfType<StaticPartitionDefinition>();
        ((StaticPartitionDefinition)node.Partitions!).Keys.Should().Equal("us", "eu");
        node.PartitionDependencies.Should().NotBeNull();
        node.Cache!.Strategy.Should().Be(CacheKeyStrategy.InputsCodeAndConfig);
        node.Cache.Ttl.Should().Be(TimeSpan.FromMinutes(10));
        node.Cache.Invalidation.Should().Be(CacheInvalidation.OnConfigChange);
        node.ArtifactNamespace.Should().Equal("rag", "ingest");
        node.InputSchemas.Should().ContainKey("url");
        node.InputSchemas!["url"].Type.Should().Be(typeof(string));
        node.InputSchemas["url"].Validator.Should().NotBeNull();
        node.Metadata.Should().Contain("kind", "io");
    }

    [Fact]
    public void Compile_CustomPrimitiveDescriptors_ResolveThroughRegistry()
    {
        var options = new GraphConfigCompilerOptions()
            .RegisterPartitionDependencyMapping("last-days", arguments =>
            {
                var days = arguments!.Value.GetProperty("days").GetInt32();
                return PartitionDependencyMapping.Custom(
                    "last-days",
                    arguments,
                    key => Enumerable.Range(0, days)
                        .Select(offset => new PartitionKey { Dimensions = [$"{key.Dimensions[0]}-{offset}"] }));
            })
            .RegisterInputValidator("starts-with", arguments =>
            {
                var prefix = arguments!.Value.GetProperty("prefix").GetString()!;
                return InputValidators.Custom("starts-with", arguments, new PrefixValidator(prefix));
            });

        var compiler = new GraphConfigCompiler(options);
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
                    PartitionDependencies = new PartitionDependencyConfig
                    {
                        Custom = new CustomPrimitiveDescriptorConfig
                        {
                            Name = "last-days",
                            Arguments = JsonDocument.Parse("""{"days":2}""").RootElement.Clone()
                        }
                    },
                    InputSchemas = new Dictionary<string, InputSchemaConfig>
                    {
                        ["code"] = new()
                        {
                            TypeName = "string",
                            Constraints = JsonDocument.Parse("""{"type":"custom","name":"starts-with","arguments":{"prefix":"HPD-"}}""").RootElement.Clone()
                        }
                    }
                }
            }
        };

        var node = compiler.Compile(config).GetNode("handler")!;

        node.PartitionDependencies!.CustomDescriptor!.Name.Should().Be("last-days");
        node.PartitionDependencies.MapInputPartitions(new PartitionKey { Dimensions = ["2026-05-06"] })
            .Select(k => k.Dimensions[0])
            .Should().Equal("2026-05-06-0", "2026-05-06-1");
        node.InputSchemas!["code"].Validator.Should().BeAssignableTo<IDescribedInputValidator>();
        node.InputSchemas["code"].Validator!.Validate("code", "HPD-Graph").IsValid.Should().BeTrue();
        node.InputSchemas["code"].Validator!.Validate("code", "Graph").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Compile_InputSchemaType_ResolvesThroughRegisteredType()
    {
        var compiler = new GraphConfigCompiler(new GraphConfigCompilerOptions()
            .RegisterType<RegisteredInput>());

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
                    InputSchemas = new Dictionary<string, InputSchemaConfig>
                    {
                        ["input"] = new()
                        {
                            TypeName = typeof(RegisteredInput).FullName!,
                            Required = true
                        }
                    }
                }
            }
        };

        var node = compiler.Compile(config).GetNode("handler")!;

        node.InputSchemas!["input"].Type.Should().Be(typeof(RegisteredInput));
    }

    [Fact]
    public void Compile_InputSchemaType_RejectsUnregisteredCustomType()
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
                    InputSchemas = new Dictionary<string, InputSchemaConfig>
                    {
                        ["input"] = new()
                        {
                            TypeName = typeof(RegisteredInput).FullName!,
                            Required = true
                        }
                    }
                }
            }
        };

        var act = () => _compiler.Compile(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not be resolved*");
    }

    [Fact]
    public void Compile_EnumValidator_ResolvesEnumTypeThroughRegistry()
    {
        var compiler = new GraphConfigCompiler(new GraphConfigCompilerOptions()
            .RegisterType<RegisteredMode>());

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
                    InputSchemas = new Dictionary<string, InputSchemaConfig>
                    {
                        ["mode"] = new()
                        {
                            TypeName = "string",
                            Constraints = JsonDocument.Parse(
                                $$"""{"type":"enum","enumType":{{JsonSerializer.Serialize(typeof(RegisteredMode).FullName)}}}""")
                                .RootElement.Clone()
                        }
                    }
                }
            }
        };

        var validator = compiler.Compile(config).GetNode("handler")!.InputSchemas!["mode"].Validator!;

        validator.Validate("mode", "Fast").IsValid.Should().BeTrue();
        validator.Validate("mode", "nope").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Compile_ObjectNodeConfig_MapsPropertiesToRuntimeDictionary()
    {
        var graph = _compiler.Compile(GraphConfigSerializationTests.CreateFullGraphConfig());

        var node = graph.GetNode("read")!;

        node.Config.Should().NotBeNull();
        node.Config!.Value.GetProperty("path").GetString().Should().Be("/tmp/docs");
        node.Config.Value.GetProperty("limit").GetInt32().Should().Be(10);
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

        node.Config.Should().NotBeNull();
        node.Config!.Value.ValueKind.Should().Be(kind);
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

    private sealed class RegisteredInput
    {
    }

    private enum RegisteredMode
    {
        Fast,
        Careful
    }

    private sealed class PrefixValidator(string prefix) : IInputValidator
    {
        public ValidationResult Validate(string inputName, object? value)
        {
            return value is string text && text.StartsWith(prefix, StringComparison.Ordinal)
                ? ValidationResult.Success()
                : ValidationResult.Failure($"{inputName} must start with {prefix}");
        }
    }
}
