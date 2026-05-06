using FluentAssertions;
using System.Text.Json;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.V21;

public sealed class GraphBuilderFluentTests
{
    [Fact]
    public void Build_WithOnlyHandlerNodes_AutoWiresSequentialEdges()
    {
        var graph = new GraphBuilder()
            .WithName("linear")
            .AddHandlerNode("read", "Read", "read_handler")
            .AddHandlerNode("chunk", "Chunk", "chunk_handler")
            .AddHandlerNode("embed", "Embed", "embed_handler")
            .Build();

        graph.Nodes.Should().Contain(node => node.Id == "START" && node.Type == NodeType.Start);
        graph.Nodes.Should().Contain(node => node.Id == "END" && node.Type == NodeType.End);
        graph.Edges.Select(edge => (edge.From, edge.To)).Should().Equal(
            ("START", "read"),
            ("read", "chunk"),
            ("chunk", "embed"),
            ("embed", "END"));
    }

    [Fact]
    public void Build_WithExplicitEdge_DoesNotAutoWireSequentialEdges()
    {
        var graph = new GraphBuilder()
            .WithName("explicit")
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .AddEdge("START", "b")
            .Build();

        graph.Edges.Should().ContainSingle()
            .Which.Should().Match<Edge>(edge => edge.From == "START" && edge.To == "b");
    }

    [Fact]
    public void Build_WithAutoSequentialEdgesDisabled_DoesNotAutoWire()
    {
        var graph = new GraphBuilder()
            .WithName("disabled")
            .WithAutoSequentialEdges(false)
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .Build();

        graph.Edges.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithExplicitAutoSequentialEdges_WiresNonHandlerNodes()
    {
        var graph = new GraphBuilder()
            .WithName("router")
            .WithAutoSequentialEdges()
            .AddRouterNode("route", "Route", "route_handler")
            .AddHandlerNode("handle", "Handle", "handle_handler")
            .Build();

        graph.Edges.Select(edge => (edge.From, edge.To)).Should().Equal(
            ("START", "route"),
            ("route", "handle"),
            ("handle", "END"));
    }

    [Fact]
    public void FromTo_Chaining_AddsConfiguredEdges()
    {
        var schedule = new ScheduleConstraint
        {
            CronExpression = "0 3 * * *",
            TimeZone = TimeZoneInfo.Utc,
            Tolerance = TimeSpan.FromMinutes(2)
        };
        var retryPolicy = new EdgeRetryPolicy
        {
            RetryInterval = TimeSpan.FromSeconds(5),
            MaxRetries = 3,
            MaxWaitTime = TimeSpan.FromMinutes(1),
            ExhaustedBehavior = EdgeRetryExhaustedBehavior.SkipNode
        };

        var graph = new GraphBuilder()
            .WithName("chain")
            .AddRouterNode("router", "Router", "router_handler", node => node.WithOutputPorts(2))
            .AddHandlerNode("text", "Text", "text_handler")
            .AddHandlerNode("fallback", "Fallback", "fallback_handler")
            .From("router")
                .Port(1)
                .To("text")
                    .ToPort(0)
                    .WhenGreaterThanOrEqual("score", 0.8)
                    .WithDelay(TimeSpan.FromSeconds(2))
                    .WithSchedule(schedule)
                    .WithRetryPolicy(retryPolicy)
                    .WithPriority(0)
                    .WithCloningPolicy(CloningPolicy.NeverClone)
                    .WithMetadata("route", "text")
                .And()
                .To("fallback")
                    .AsDefault()
                    .WithPriority(99)
                .Done()
            .Build();

        graph.Edges.Should().HaveCount(2);

        var textEdge = graph.Edges.Single(edge => edge.To == "text");
        textEdge.From.Should().Be("router");
        textEdge.FromPort.Should().Be(1);
        textEdge.ToPort.Should().Be(0);
        textEdge.Condition!.Type.Should().Be(ConditionType.FieldGreaterThanOrEqual);
        textEdge.Condition.Field.Should().Be("score");
        textEdge.Condition.Value.Should().Be(0.8);
        textEdge.Delay.Should().Be(TimeSpan.FromSeconds(2));
        textEdge.Schedule.Should().BeSameAs(schedule);
        textEdge.RetryPolicy.Should().BeSameAs(retryPolicy);
        textEdge.Priority.Should().Be(0);
        textEdge.CloningPolicy.Should().Be(CloningPolicy.NeverClone);
        textEdge.Metadata.Should().Contain("route", "text");

        var fallbackEdge = graph.Edges.Single(edge => edge.To == "fallback");
        fallbackEdge.Condition!.Type.Should().Be(ConditionType.Default);
        fallbackEdge.Priority.Should().Be(99);
    }

    [Fact]
    public void FromTo_RepeatedTo_CreatesLinearChain()
    {
        var graph = new GraphBuilder()
            .WithName("linear")
            .AddStartNode()
            .AddHandlerNode("retrieve", "Retrieve", "retrieve_handler")
            .AddHandlerNode("agent", "Agent", "agent_handler")
            .AddEndNode()
            .From("START").To("retrieve").To("agent").To("END").Done()
            .Build();

        graph.Edges.Select(edge => (edge.From, edge.To)).Should().Equal(
            ("START", "retrieve"),
            ("retrieve", "agent"),
            ("agent", "END"));
    }

    [Fact]
    public void FromTo_WithMultipleSourcesAndTargets_AddsFanInFanOutEdges()
    {
        var graph = new GraphBuilder()
            .WithName("fan")
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .AddHandlerNode("c", "C", "c_handler")
            .AddHandlerNode("d", "D", "d_handler")
            .From("a", "b")
                .Port(1)
                .To("c", "d")
                    .WhenEquals("ready", true)
                    .WithPriority(4)
                .Done()
            .Build();

        graph.Edges.Select(edge => (edge.From, edge.To)).Should().BeEquivalentTo(
        [
            ("a", "c"),
            ("a", "d"),
            ("b", "c"),
            ("b", "d")
        ]);
        foreach (var edge in graph.Edges)
        {
            edge.FromPort.Should().Be(1);
            edge.Priority.Should().Be(4);
            edge.Condition.Should().NotBeNull();
            edge.Condition!.Type.Should().Be(ConditionType.FieldEquals);
            edge.Condition.Field.Should().Be("ready");
            edge.Condition.Value.Should().Be(true);
        }
    }

    [Fact]
    public void RouteBy_AddsFieldRoutesAndDefaultRoute()
    {
        var graph = new GraphBuilder()
            .WithName("field-route")
            .AddRouterNode("router", "Router", "router_handler")
            .AddHandlerNode("rag", "RAG", "rag_handler")
            .AddHandlerNode("agent", "Agent", "agent_handler")
            .AddHandlerNode("fallback", "Fallback", "fallback_handler")
            .From("router")
                .RouteBy("intent")
                    .When("rag", "rag")
                    .When("agent", "agent")
                    .Default("fallback")
                    .Done()
            .Build();

        var rag = graph.Edges.Single(edge => edge.To == "rag");
        rag.Condition!.Type.Should().Be(ConditionType.FieldEquals);
        rag.Condition.Field.Should().Be("intent");
        rag.Condition.Value.Should().Be("rag");

        var agent = graph.Edges.Single(edge => edge.To == "agent");
        agent.Condition!.Type.Should().Be(ConditionType.FieldEquals);
        agent.Condition.Field.Should().Be("intent");
        agent.Condition.Value.Should().Be("agent");

        graph.Edges.Single(edge => edge.To == "fallback")
            .Condition!.Type.Should().Be(ConditionType.Default);
    }

    [Theory]
    [MemberData(nameof(ConditionMethods))]
    public void FromTo_ConditionHelpers_CreateExpectedCondition(
        Func<EdgeTargetBuilder, EdgeTargetBuilder> configure,
        ConditionType expectedType,
        object? expectedValue,
        string? expectedRegexOptions = null)
    {
        var graph = new GraphBuilder()
            .WithName("conditions")
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .From("a")
                .To("b")
                .Apply(configure)
                .Done()
            .Build();

        var condition = graph.Edges.Single().Condition!;
        condition.Type.Should().Be(expectedType);
        condition.Field.Should().Be("field");
        condition.Value.Should().BeEquivalentTo(expectedValue);
        condition.RegexOptions.Should().Be(expectedRegexOptions);
    }

    [Fact]
    public void EdgeBuilder_ExposesTemporalEdgeFeatures()
    {
        var schedule = new ScheduleConstraint { CronExpression = "0 1 * * *" };
        var retryPolicy = new EdgeRetryPolicy { RetryInterval = TimeSpan.FromSeconds(1) };

        var graph = new GraphBuilder()
            .WithName("edge-builder")
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .AddEdge("a", "b", edge => edge
                .WithDelay(TimeSpan.FromSeconds(3))
                .WithSchedule(schedule)
                .WithRetryPolicy(retryPolicy))
            .Build();

        var edge = graph.Edges.Single();
        edge.Delay.Should().Be(TimeSpan.FromSeconds(3));
        edge.Schedule.Should().BeSameAs(schedule);
        edge.RetryPolicy.Should().BeSameAs(retryPolicy);
    }

    [Fact]
    public void EdgeBuilder_WithCronAndRetryEvery_CreateTemporalEdgeFeatures()
    {
        var graph = new GraphBuilder()
            .WithName("temporal-helpers")
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .AddEdge("a", "b", edge => edge
                .WithCron("0 3 * * *", "UTC", TimeSpan.FromMinutes(2))
                .RetryEvery(
                    TimeSpan.FromSeconds(5),
                    maxWaitTime: TimeSpan.FromMinutes(1),
                    maxRetries: 3,
                    exhaustedBehavior: EdgeRetryExhaustedBehavior.SkipNode))
            .Build();

        var edge = graph.Edges.Single();
        edge.Schedule.Should().NotBeNull();
        edge.Schedule!.CronExpression.Should().Be("0 3 * * *");
        edge.Schedule.TimeZone.Should().Be(TimeZoneInfo.Utc);
        edge.Schedule.Tolerance.Should().Be(TimeSpan.FromMinutes(2));
        edge.RetryPolicy.Should().NotBeNull();
        edge.RetryPolicy!.RetryInterval.Should().Be(TimeSpan.FromSeconds(5));
        edge.RetryPolicy.MaxWaitTime.Should().Be(TimeSpan.FromMinutes(1));
        edge.RetryPolicy.MaxRetries.Should().Be(3);
        edge.RetryPolicy.ExhaustedBehavior.Should().Be(EdgeRetryExhaustedBehavior.SkipNode);
    }

    [Fact]
    public void FromTo_WithCronAndWithRetry_CreateTemporalEdgeFeatures()
    {
        var graph = new GraphBuilder()
            .WithName("chain-temporal-helpers")
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .From("a")
                .To("b")
                    .WithCron("*/15 * * * *", TimeZoneInfo.Utc)
                    .WithRetry(TimeSpan.FromSeconds(10), maxRetries: 2)
                .Done()
            .Build();

        var edge = graph.Edges.Single();
        edge.Schedule.Should().NotBeNull();
        edge.Schedule!.CronExpression.Should().Be("*/15 * * * *");
        edge.Schedule.TimeZone.Should().Be(TimeZoneInfo.Utc);
        edge.Schedule.Tolerance.Should().Be(TimeSpan.FromMinutes(1));
        edge.RetryPolicy.Should().NotBeNull();
        edge.RetryPolicy!.RetryInterval.Should().Be(TimeSpan.FromSeconds(10));
        edge.RetryPolicy.MaxRetries.Should().Be(2);
    }

    [Fact]
    public void FromTo_WhenPredicate_AddsRuntimeOnlyPredicate()
    {
        var graph = new GraphBuilder()
            .WithName("predicate")
            .AddHandlerNode("a", "A", "a_handler")
            .AddHandlerNode("b", "B", "b_handler")
            .From("a")
                .To("b")
                    .When(ctx => ctx.Get<bool>("approved"))
                .Done()
            .Build();

        var edge = graph.Edges.Single();

        edge.Predicate.Should().NotBeNull();
        ConditionEvaluator.Evaluate(
            edge,
            new Dictionary<string, object> { ["approved"] = true },
            new GraphContext("predicate-true", graph, new ServiceCollection().BuildServiceProvider())).Should().BeTrue();
        ConditionEvaluator.Evaluate(
            edge,
            new Dictionary<string, object> { ["approved"] = false },
            new GraphContext("predicate-false", graph, new ServiceCollection().BuildServiceProvider())).Should().BeFalse();
    }

    [Fact]
    public void EdgePredicate_IsIgnoredByJsonSerialization()
    {
        var edge = new Edge
        {
            From = "a",
            To = "b",
            Predicate = _ => true
        };

        var json = JsonSerializer.Serialize(edge);

        json.Should().NotContain("Predicate");
    }

    [Theory]
    [InlineData(-1)]
    public void FromTo_Port_RejectsNegativePorts(int port)
    {
        var act = () => new GraphBuilder().WithName("ports").From("a").Port(port);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EdgeBuilder_WithDelay_RejectsNegativeDelay()
    {
        var act = () => new GraphBuilder()
            .WithName("delay")
            .AddEdge("a", "b", edge => edge.WithDelay(TimeSpan.FromSeconds(-1)));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    public static TheoryData<Func<EdgeTargetBuilder, EdgeTargetBuilder>, ConditionType, object?, string?> ConditionMethods()
    {
        return new TheoryData<Func<EdgeTargetBuilder, EdgeTargetBuilder>, ConditionType, object?, string?>
        {
            { edge => edge.WhenEquals("field", "x"), ConditionType.FieldEquals, "x", null },
            { edge => edge.WhenNotEquals("field", "x"), ConditionType.FieldNotEquals, "x", null },
            { edge => edge.WhenGreaterThan("field", 1), ConditionType.FieldGreaterThan, 1, null },
            { edge => edge.WhenGreaterThanOrEqual("field", 1), ConditionType.FieldGreaterThanOrEqual, 1, null },
            { edge => edge.WhenLessThan("field", 1), ConditionType.FieldLessThan, 1, null },
            { edge => edge.WhenLessThanOrEqual("field", 1), ConditionType.FieldLessThanOrEqual, 1, null },
            { edge => edge.WhenContains("field", "x"), ConditionType.FieldContains, "x", null },
            { edge => edge.WhenContainsAny("field", "x", "y"), ConditionType.FieldContainsAny, new object[] { "x", "y" }, null },
            { edge => edge.WhenContainsAll("field", "x", "y"), ConditionType.FieldContainsAll, new object[] { "x", "y" }, null },
            { edge => edge.WhenStartsWith("field", "x", ignoreCase: true), ConditionType.FieldStartsWith, "x", "IgnoreCase" },
            { edge => edge.WhenEndsWith("field", "x", ignoreCase: true), ConditionType.FieldEndsWith, "x", "IgnoreCase" },
            { edge => edge.WhenMatchesRegex("field", "^x"), ConditionType.FieldMatchesRegex, "^x", null },
            { edge => edge.WhenExists("field"), ConditionType.FieldExists, null, null },
            { edge => edge.WhenNotExists("field"), ConditionType.FieldNotExists, null, null },
            { edge => edge.WhenEmpty("field"), ConditionType.FieldIsEmpty, null, null },
            { edge => edge.WhenNotEmpty("field"), ConditionType.FieldIsNotEmpty, null, null }
        };
    }
}

internal static class EdgeTargetBuilderTestExtensions
{
    public static EdgeTargetBuilder Apply(
        this EdgeTargetBuilder builder,
        Func<EdgeTargetBuilder, EdgeTargetBuilder> configure) => configure(builder);
}
