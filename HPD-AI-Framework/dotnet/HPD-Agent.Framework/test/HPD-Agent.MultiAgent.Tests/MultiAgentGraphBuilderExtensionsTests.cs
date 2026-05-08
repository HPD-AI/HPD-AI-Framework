using HPD.Agent;
using HPD.MultiAgent.Config;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Serialization;
using HPD.RAG.Pipeline;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Core.Builders;
using HPDAgent.Graph.Core.Config;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.MultiAgent.Tests;

public sealed class MultiAgentGraphBuilderExtensionsTests
{
    [Fact]
    public void AddMultiAgent_WithConfig_AddsSubGraphNode()
    {
        var graph = new GraphBuilder()
            .WithName("Parent")
            .AddStartNode()
            .AddMultiAgent("agent_workflow", CreateWorkflowConfig())
            .AddEndNode()
            .AddEdge("START", "agent_workflow")
            .AddEdge("agent_workflow", "END")
            .Build();

        var node = graph.GetNode("agent_workflow");

        node.Should().NotBeNull();
        node!.Type.Should().Be(NodeType.SubGraph);
        node.SubGraph.Should().NotBeNull();
        node.SubGraph!.Name.Should().Be("AgentWorkflow");
        node.SubGraph.Nodes.Should().Contain(n => n.Id == "classifier");
    }

    [Fact]
    public async Task AddMultiAgent_WithWorkflowInstance_AddsSubGraphNode()
    {
        var workflow = await AgentWorkflow.FromConfig(CreateWorkflowConfig()).BuildAsync();

        var graph = new GraphBuilder()
            .WithName("Parent")
            .AddStartNode()
            .AddMultiAgent("agent_workflow", workflow)
            .AddEndNode()
            .AddEdge("START", "agent_workflow")
            .AddEdge("agent_workflow", "END")
            .Build();

        var node = graph.GetNode("agent_workflow");

        node.Should().NotBeNull();
        node!.Type.Should().Be(NodeType.SubGraph);
        AssertEquivalentSubGraph(node.SubGraph, workflow.Graph);
    }

    [Fact]
    public void AddMultiAgent_GraphSerializesAsGraphConfig()
    {
        var graph = new GraphBuilder()
            .WithId("parent")
            .WithName("Parent")
            .AddStartNode()
            .AddMultiAgent("agent_workflow", CreateWorkflowConfig())
            .AddEndNode()
            .AddEdge("START", "agent_workflow")
            .AddEdge("agent_workflow", "END")
            .Build();

        var config = graph.ToConfig();

        config.GraphId.Should().Be("parent");
        config.Nodes["agent_workflow"].Type.Should().Be(HPDAgent.Graph.Abstractions.Config.NodeKindConfig.SubGraph);
        config.Nodes["agent_workflow"].SubGraph.Should().NotBeNull();
        config.Nodes["agent_workflow"].SubGraph!.Nodes.Should().ContainKey("classifier");
    }

    [Fact]
    public void AddMultiAgentGraphSerialization_RegistersResolverContributor()
    {
        var services = new ServiceCollection();

        services.AddMultiAgentGraphSerialization();

        using var provider = services.BuildServiceProvider();
        var contributor = provider.GetRequiredService<IGraphJsonTypeInfoResolverContributor>();

        contributor.Resolver.Should().BeSameAs(MultiAgentGraphConfigJsonContext.Default);
    }

    [Fact]
    public async Task GraphBuilder_CanComposeRagAndMultiAgentSubGraphs()
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

        ragNode.Should().NotBeNull();
        ragNode!.Type.Should().Be(NodeType.SubGraph);
        AssertEquivalentSubGraph(ragNode.SubGraph, ragPipeline.Graph);

        agentNode.Should().NotBeNull();
        agentNode!.Type.Should().Be(NodeType.SubGraph);
        agentNode.SubGraph.Should().NotBeNull();
        graph.Edges.Should().Contain(edge => edge.From == "retrieve_context" && edge.To == "agent_workflow");

        var config = graph.ToConfig();
        config.Nodes["retrieve_context"].SubGraph.Should().NotBeNull();
        config.Nodes["agent_workflow"].SubGraph.Should().NotBeNull();
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
            new EdgeConfig { From = "START", To = "classifier" },
            new EdgeConfig { From = "classifier", To = "END" }
        ]
    };

    private static void AssertEquivalentSubGraph(Graph? actual, Graph expected)
    {
        actual.Should().NotBeNull();
        actual.Should().NotBeSameAs(expected);
        actual!.Id.Should().Be(expected.Id);
        actual.Name.Should().Be(expected.Name);
        actual.EntryNodeId.Should().Be(expected.EntryNodeId);
        actual.ExitNodeId.Should().Be(expected.ExitNodeId);
        actual.MaxIterations.Should().Be(expected.MaxIterations);
        actual.Nodes.Select(node => node.Id).Should().BeEquivalentTo(expected.Nodes.Select(node => node.Id));
        actual.Edges.Select(edge => $"{edge.From}->{edge.To}").Should()
            .BeEquivalentTo(expected.Edges.Select(edge => $"{edge.From}->{edge.To}"));
    }
}
