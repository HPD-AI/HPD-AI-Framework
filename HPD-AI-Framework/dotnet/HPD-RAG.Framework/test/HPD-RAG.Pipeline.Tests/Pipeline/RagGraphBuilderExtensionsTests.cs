using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Serialization;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.RAG.Pipeline.Tests.Pipeline;

public sealed class RagGraphBuilderExtensionsTests
{
    [Fact]
    public async Task AddRagIngestion_AddsSubGraphNode()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("Ingest")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .From("START").To("read").To("END").Done()
            .BuildIngestionAsync();

        var graph = new GraphBuilder()
            .WithName("Parent")
            .AddStartNode()
            .AddRagIngestion("ingest", pipeline)
            .AddEndNode()
            .AddEdge("START", "ingest")
            .AddEdge("ingest", "END")
            .Build();

        var node = graph.GetNode("ingest");

        Assert.NotNull(node);
        Assert.Equal(NodeType.SubGraph, node!.Type);
        AssertEquivalentGraph(pipeline.Graph, node.SubGraph);
    }

    [Fact]
    public async Task AddRagRetrieval_AddsSubGraphNode()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("Retrieve")
            .AddHandler("embed", MragHandlerNames.EmbedQuery)
            .From("START").To("embed").To("END").Done()
            .BuildRetrievalAsync();

        var graph = new GraphBuilder()
            .WithName("Parent")
            .AddStartNode()
            .AddRagRetrieval("retrieve", pipeline)
            .AddEndNode()
            .AddEdge("START", "retrieve")
            .AddEdge("retrieve", "END")
            .Build();

        var node = graph.GetNode("retrieve");

        Assert.NotNull(node);
        Assert.Equal(NodeType.SubGraph, node!.Type);
        AssertEquivalentGraph(pipeline.Graph, node.SubGraph);
    }

    [Fact]
    public async Task AddRagEvaluation_GraphSerializesAsGraphConfig()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("Evaluate")
            .AddHandler("eval", MragHandlerNames.EvalRelevance)
            .From("START").To("eval").To("END").Done()
            .BuildEvaluationAsync();

        var graph = new GraphBuilder()
            .WithId("parent")
            .WithName("Parent")
            .AddStartNode()
            .AddRagEvaluation("evaluate", pipeline)
            .AddEndNode()
            .AddEdge("START", "evaluate")
            .AddEdge("evaluate", "END")
            .Build();

        var config = graph.ToConfig();

        Assert.Equal("parent", config.GraphId);
        Assert.Equal(NodeKindConfig.SubGraph, config.Nodes["evaluate"].Type);
        Assert.NotNull(config.Nodes["evaluate"].SubGraph);
        Assert.Contains("eval", config.Nodes["evaluate"].SubGraph!.Nodes.Keys);
    }

    [Fact]
    public async Task MragEdgeBuilder_UsesGraphDefaultConditionForDefaultRoutes()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("Route")
            .AddHandler("read", MragHandlerNames.ReadMarkdown)
            .AddHandler("fallback", MragHandlerNames.ChunkByHeader)
            .From("START").To("read").Done()
            .From("read").AsDefault().To("fallback").Done()
            .BuildIngestionAsync();

        var edge = pipeline.Graph.Edges.Single(edge => edge.From == "read" && edge.To == "fallback");

        Assert.NotNull(edge.Condition);
        Assert.Equal(ConditionType.Default, edge.Condition!.Type);
    }

    [Fact]
    public async Task MragEdgeBuilder_CanCreateFanInFanOutEdges()
    {
        var pipeline = await MragPipeline.Create()
            .WithName("Fan")
            .AddHandler("a", MragHandlerNames.ReadMarkdown)
            .AddHandler("b", MragHandlerNames.ChunkByHeader)
            .AddHandler("c", MragHandlerNames.ChunkByToken)
            .AddHandler("d", MragHandlerNames.WriteInMemory)
            .From("a", "b").WhenEquals("ready", true).To("c", "d").Done()
            .BuildIngestionAsync();

        var edges = pipeline.Graph.Edges
            .Where(edge => edge.From is "a" or "b")
            .Select(edge => (edge.From, edge.To))
            .ToList();

        Assert.Contains(("a", "c"), edges);
        Assert.Contains(("a", "d"), edges);
        Assert.Contains(("b", "c"), edges);
        Assert.Contains(("b", "d"), edges);
        Assert.All(
            pipeline.Graph.Edges.Where(edge => edge.From is "a" or "b"),
            edge =>
            {
                Assert.NotNull(edge.Condition);
                Assert.Equal(ConditionType.FieldEquals, edge.Condition!.Type);
                Assert.Equal("ready", edge.Condition.Field);
                Assert.Equal(true, edge.Condition.Value);
            });
    }

    [Fact]
    public void AddMragGraphSerialization_RegistersResolverContributor()
    {
        var services = new ServiceCollection();

        services.AddMragGraphSerialization();

        using var provider = services.BuildServiceProvider();
        var contributor = provider.GetRequiredService<IGraphJsonTypeInfoResolverContributor>();

        Assert.Same(MragJsonSerializerContext.Shared, contributor.Resolver);
    }

    private static void AssertEquivalentGraph(Graph expected, Graph? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Id, actual!.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.EntryNodeId, actual.EntryNodeId);
        Assert.Equal(expected.ExitNodeId, actual.ExitNodeId);
        Assert.Equal(expected.Nodes.Select(node => node.Id), actual.Nodes.Select(node => node.Id));
        Assert.Equal(
            expected.Edges.Select(edge => (edge.From, edge.To)),
            actual.Edges.Select(edge => (edge.From, edge.To)));
    }
}
