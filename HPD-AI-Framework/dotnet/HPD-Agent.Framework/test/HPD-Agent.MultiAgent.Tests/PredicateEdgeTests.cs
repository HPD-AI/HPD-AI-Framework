using System.Reflection;
using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Routing;
using HPDAgent.Graph.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for .When(predicate) edge routing — covers builder wiring (5.1–5.3),
/// AgentGraphContext.EvaluatePredicateEdges evaluation (5.4–5.8),
/// multi-target registration (5.9), serialisation behaviour (5.10),
/// and edge-not-bloated guard (5.11).
/// </summary>
public class PredicateEdgeTests
{
    private static AgentConfig Config() => new() { Name = "T", SystemInstructions = "T" };

    // ── internal access via reflection ────────────────────────────────────────

    private static Dictionary<string, Func<EdgeConditionContext, bool>> GetPredicateEdges(MultiAgent workflow)
    {
        var field = typeof(MultiAgent).GetField(
            "PredicateEdges",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        field.Should().NotBeNull("PredicateEdges field must exist on MultiAgent");
        return (Dictionary<string, Func<EdgeConditionContext, bool>>)field!.GetValue(workflow)!;
    }

    // ── 5.1  .When() stores entry in PredicateEdges ───────────────────────────

    [Fact]
    public void When_Predicate_Registers_In_PredicateEdges_Dict()
    {
        var workflow = AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config());

        Func<EdgeConditionContext, bool> predicate = _ => true;
        workflow.From("a").To("b").When(predicate);

        var edges = GetPredicateEdges(workflow);
        edges.Should().ContainKey("a->b");
        edges["a->b"].Should().BeSameAs(predicate);
    }

    // ── 5.2  .When() registers a FieldEquals edge with the synthetic key ──────

    [Fact]
    public async Task When_Predicate_Registers_FieldEquals_Edge_With_SyntheticKey()
    {
        var workflow = AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config());

        workflow.From("a").To("b").When(_ => true);

        // Build must succeed — the FieldEquals condition on the synthetic key is valid
        var act = async () => await workflow.BuildAsync();
        await act.Should().NotThrowAsync();

        GetPredicateEdges(workflow).Should().ContainKey("a->b");
    }

    // ── 5.3  .When() replaces the unconditional edge added by the constructor ─

    [Fact]
    public void When_Predicate_Replaces_Unconditional_Edge()
    {
        var workflow = AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config());

        workflow.From("a").To("b").When(_ => false);

        // Exactly one entry in predicate dict — no double-registration
        var edges = GetPredicateEdges(workflow);
        edges.Should().HaveCount(1);
        edges.Should().ContainKey("a->b");
    }

    // ── 5.4  EvaluatePredicateEdges: true predicate writes true ───────────────

    [Fact]
    public void EvaluatePredicateEdges_WhenPredicateReturnsTrue_WritesTrue()
    {
        var predicates = new Dictionary<string, Func<EdgeConditionContext, bool>>
        {
            ["a->b"] = _ => true
        };
        var ctx = BuildContext(predicates);
        var outputs = new Dictionary<string, object>();

        ctx.EvaluatePredicateEdges("a", outputs);

        outputs.Should().ContainKey("__predicate_a_b");
        outputs["__predicate_a_b"].Should().Be(true);
    }

    // ── 5.5  EvaluatePredicateEdges: false predicate writes false ─────────────

    [Fact]
    public void EvaluatePredicateEdges_WhenPredicateReturnsFalse_WritesFalse()
    {
        var predicates = new Dictionary<string, Func<EdgeConditionContext, bool>>
        {
            ["a->b"] = _ => false
        };
        var ctx = BuildContext(predicates);
        var outputs = new Dictionary<string, object>();

        ctx.EvaluatePredicateEdges("a", outputs);

        outputs["__predicate_a_b"].Should().Be(false);
    }

    // ── 5.6  EvaluatePredicateEdges: throwing predicate writes false ──────────

    [Fact]
    public void EvaluatePredicateEdges_WhenPredicateThrows_WritesFalse()
    {
        var predicates = new Dictionary<string, Func<EdgeConditionContext, bool>>
        {
            ["a->b"] = _ => throw new InvalidOperationException("boom")
        };
        var ctx = BuildContext(predicates);
        var outputs = new Dictionary<string, object>();

        var act = () => ctx.EvaluatePredicateEdges("a", outputs);
        act.Should().NotThrow("exceptions in predicates must be swallowed");
        outputs["__predicate_a_b"].Should().Be(false);
    }

    // ── 5.7  EvaluatePredicateEdges: only evaluates edges from the given node ─

    [Fact]
    public void EvaluatePredicateEdges_OnlyEvaluatesEdgesFromCorrectNode()
    {
        var predicates = new Dictionary<string, Func<EdgeConditionContext, bool>>
        {
            ["a->b"] = _ => true,
            ["c->d"] = _ => true
        };
        var ctx = BuildContext(predicates);
        var outputs = new Dictionary<string, object>();

        ctx.EvaluatePredicateEdges("a", outputs);

        outputs.Should().ContainKey("__predicate_a_b");
        outputs.Should().NotContainKey("__predicate_c_d",
            "edges from node 'c' must not be evaluated when fromNodeId is 'a'");
    }

    // ── 5.8  EvaluatePredicateEdges: predicate receives correct outputs ───────

    [Fact]
    public void EvaluatePredicateEdges_PredicateReceivesOutputsContext()
    {
        string? capturedValue = null;

        var predicates = new Dictionary<string, Func<EdgeConditionContext, bool>>
        {
            ["a->b"] = ctx =>
            {
                capturedValue = ctx.Get<string>("answer");
                return true;
            }
        };
        var context = BuildContext(predicates);
        var outputs = new Dictionary<string, object> { ["answer"] = "hello" };

        context.EvaluatePredicateEdges("a", outputs);

        capturedValue.Should().Be("hello");
    }

    // ── 5.9  Multiple targets: all edges registered ───────────────────────────

    [Fact]
    public void When_Predicate_MultipleTargets_RegistersAllEdges()
    {
        var workflow = AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config())
            .AddAgent("c", Config());

        workflow.From("a").To("b", "c").When(_ => true);

        var edges = GetPredicateEdges(workflow);
        edges.Should().ContainKey("a->b");
        edges.Should().ContainKey("a->c");
    }

    // ── 5.10  ExportConfigJson: predicate edge serialises as FieldEquals ──────

    [Fact]
    public async Task When_Predicate_ExportConfigJson_SerializesAsSyntheticFieldEquals()
    {
        var workflow = AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config())
            .From("a").To("b").When(_ => true);

        var instance = await workflow.BuildAsync();
        var json = instance.ExportConfigJson();

        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("__predicate_a_b",
            "predicate edges are serialised as FieldEquals on the synthetic key");
        var act = () => System.Text.Json.JsonDocument.Parse(json);
        act.Should().NotThrow("exported JSON must be valid");
    }

    // ── 5.11  EvaluatePredicateEdges with empty dict: no-op ──────────────────

    [Fact]
    public void EvaluatePredicateEdges_EmptyPredicateDict_WritesNothing()
    {
        var ctx = BuildContext(new Dictionary<string, Func<EdgeConditionContext, bool>>());
        var outputs = new Dictionary<string, object>();

        var act = () => ctx.EvaluatePredicateEdges("a", outputs);
        act.Should().NotThrow();
        outputs.Should().BeEmpty();
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private static AgentGraphContext BuildContext(
        Dictionary<string, Func<EdgeConditionContext, bool>> predicates)
    {
        var graphBuilder = new HPDAgent.Graph.Core.Builders.GraphBuilder();
        graphBuilder.WithName("test-graph");
        graphBuilder.AddStartNode();
        graphBuilder.AddEndNode();
        var graph = graphBuilder.Build();

        var services = new ServiceCollection().BuildServiceProvider();

        return new AgentGraphContext(
            executionId: "test-exec",
            graph: graph,
            services: services,
            agents: new Dictionary<string, Agent.Agent>(),
            agentOptions: new Dictionary<string, AgentNodeOptions>(),
            predicateEdges: predicates);
    }
}
