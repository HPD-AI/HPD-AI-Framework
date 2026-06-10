using HPD.Agent;
using HPD.MultiAgent.Routing;
using HPD.Graph.Core.Context;
using HPD.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.MultiAgent.Tests;

/// <summary>
/// Tests for MultiAgent predicate edge routing through graph runtime predicates.
/// </summary>
public class PredicateEdgeTests
{
    private static AgentConfig Config() => new() { Name = "T", SystemInstructions = "T" };

    [Fact]
    public async Task When_Predicate_Registers_RuntimePredicate_OnGraphEdge()
    {
        Func<EdgeConditionContext, bool> predicate = _ => true;

        var instance = await AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config())
            .From("a").To("b").When(predicate)
            .BuildAsync();

        var edge = instance.Graph.Edges.Single(edge => edge.From == "a" && edge.To == "b");

        edge.Predicate.Should().NotBeNull();
        edge.Condition.Should().BeNull();
    }

    [Fact]
    public async Task When_Predicate_EvaluatesAgainstSourceOutputs()
    {
        var instance = await AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config())
            .From("a").To("b").When(ctx => ctx.Get<string>("answer") == "yes")
            .BuildAsync();

        var edge = instance.Graph.Edges.Single(edge => edge.From == "a" && edge.To == "b");
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new GraphContext("predicate", instance.Graph, services);

        ConditionEvaluator.Evaluate(
            edge,
            new Dictionary<string, object> { ["answer"] = "yes" },
            context).Should().BeTrue();

        ConditionEvaluator.Evaluate(
            edge,
            new Dictionary<string, object> { ["answer"] = "no" },
            context).Should().BeFalse();
    }

    [Fact]
    public async Task When_Predicate_MultipleTargets_RegistersAllGraphEdges()
    {
        var instance = await AgentWorkflow.Create()
            .AddAgent("a", Config())
            .AddAgent("b", Config())
            .AddAgent("c", Config())
            .From("a").To("b", "c").When(_ => true)
            .BuildAsync();

        instance.Graph.Edges.Single(edge => edge.From == "a" && edge.To == "b")
            .Predicate.Should().NotBeNull();
        instance.Graph.Edges.Single(edge => edge.From == "a" && edge.To == "c")
            .Predicate.Should().NotBeNull();
    }

    [Fact]
    public async Task When_Predicate_ExportConfigJson_LeavesEdgeUnconditional()
    {
        var instance = await AgentWorkflow.Create()
            .WithName("PredFlow")
            .AddAgent("a", Config())
            .AddAgent("b", Config())
            .From("a").To("b").When(_ => true)
            .BuildAsync();

        var json = instance.ExportConfigJson();

        json.Should().NotContain("__predicate");
        json.Should().NotContain("Predicate");
    }
}
