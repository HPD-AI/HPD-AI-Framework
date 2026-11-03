using HPD.Agent;
using HPD.MultiAgent;
using HPD.MultiAgent.Config;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Serialization;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Graph;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Core.Builders;
using HPD.Graph.Core.Config;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GraphDefinition = HPD.Graph.Abstractions.Graph.Graph;

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
    public async Task RagAndMultiAgentSubGraphs_ComposeInOneGraph()
    {
        var ragPipeline = await MragPipeline.Create()
            .WithName("Retrieve Context")
            .AddHandler("retrieve", MragHandlerNames.EmbedQuery)
            .From("START").To("retrieve").To("END").Done()
            .BuildRetrievalAsync();

        var graph = new GraphBuilder()
            .WithId("rag-agent-composition")
            .WithName("RAG Agent Composition")
            .AddStartNode()
            .AddRagRetrieval("retrieve_context", ragPipeline)
            .AddMultiAgent("agent_workflow", CreateWorkflowConfig())
            .AddEndNode()
            .From("START").To("retrieve_context").To("agent_workflow").To("END").Done()
            .Build();

        var ragNode = graph.GetNode("retrieve_context");
        var agentNode = graph.GetNode("agent_workflow");

        Assert.NotNull(ragNode);
        Assert.Equal(NodeType.SubGraph, ragNode!.Type);
        AssertEquivalentGraph(ragPipeline.Graph, ragNode.SubGraph);

        Assert.NotNull(agentNode);
        Assert.Equal(NodeType.SubGraph, agentNode!.Type);
        Assert.NotNull(agentNode.SubGraph);
        Assert.Contains(graph.Edges, edge =>
            edge.From == "retrieve_context" && edge.To == "agent_workflow");

        var config = graph.ToConfig();
        Assert.NotNull(config.Nodes["retrieve_context"].SubGraph);
        Assert.NotNull(config.Nodes["agent_workflow"].SubGraph);
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

    private static MultiAgentWorkflowConfig CreateWorkflowConfig() => new()
    {
        Name = "AgentWorkflow",
        Agents = new Dictionary<string, AgentNodeConfig>
        {
            ["classifier"] = new()
            {
                Agent = new AgentConfig
                {
                    Name = "Classifier",
                    SystemInstructions = "Classify the request."
                }
            }
        },
        Edges =
        [
            new HPD.MultiAgent.Config.EdgeConfig { From = "START", To = "classifier" },
            new HPD.MultiAgent.Config.EdgeConfig { From = "classifier", To = "END" }
        ]
    };

    private static void AssertEquivalentGraph(GraphDefinition expected, GraphDefinition? actual)
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
