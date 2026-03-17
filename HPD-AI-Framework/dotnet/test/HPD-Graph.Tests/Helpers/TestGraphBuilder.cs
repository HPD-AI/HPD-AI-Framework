using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Fluent builder for creating test graphs.
/// </summary>
public class TestGraphBuilder
{
    private readonly List<Node> _nodes = new();
    private readonly List<Edge> _edges = new();
    private string _id = Guid.NewGuid().ToString();
    private string _name = "TestGraph";
    private string _version = "1.0.0";
    private string _entryNodeId = "start";
    private string _exitNodeId = "end";

    public TestGraphBuilder WithId(string id)
    {
        _id = id;
        return this;
    }

    public TestGraphBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public TestGraphBuilder WithVersion(string version)
    {
        _version = version;
        return this;
    }

    public TestGraphBuilder WithEntryNode(string id)
    {
        _entryNodeId = id;
        return this;
    }

    public TestGraphBuilder WithExitNode(string id)
    {
        _exitNodeId = id;
        return this;
    }

    public TestGraphBuilder AddStartNode(string id = "start", string name = "Start")
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name,
            Type = NodeType.Start
        });
        _entryNodeId = id;
        return this;
    }

    public TestGraphBuilder AddEndNode(string id = "end", string name = "End")
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name,
            Type = NodeType.End
        });
        _exitNodeId = id;
        return this;
    }

    public TestGraphBuilder AddHandlerNode(
        string id,
        string handlerName,
        string? name = null,
        Dictionary<string, object>? config = null,
        HPDAgent.Graph.Abstractions.Execution.SuspensionOptions? suspensionOptions = null)
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name ?? id,
            Type = NodeType.Handler,
            HandlerName = handlerName,
            Config = config ?? new Dictionary<string, object>(),
            SuspensionOptions = suspensionOptions
        });
        return this;
    }

    public TestGraphBuilder AddRouterNode(
        string id,
        string handlerName,
        string? name = null)
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name ?? id,
            Type = NodeType.Router,
            HandlerName = handlerName
        });
        return this;
    }

    public TestGraphBuilder AddSubGraphNode(
        string id,
        HPDAgent.Graph.Abstractions.Graph.Graph subGraph,
        string? name = null)
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name ?? id,
            Type = NodeType.SubGraph,
            SubGraph = subGraph
        });
        return this;
    }

    public TestGraphBuilder AddMapNode(
        string id,
        HPDAgent.Graph.Abstractions.Graph.Graph processorGraph,
        string? name = null,
        int? maxParallelMapTasks = null,
        MapErrorMode? errorMode = null,
        string? mapInputChannel = null,
        string? mapOutputChannel = null,
        string? mapItemType = null,
        string? mapResultType = null)
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name ?? id,
            Type = NodeType.Map,
            MapProcessorGraph = processorGraph,
            MaxParallelMapTasks = maxParallelMapTasks,
            MapErrorMode = errorMode,
            MapInputChannel = mapInputChannel,
            MapOutputChannel = mapOutputChannel,
            MapItemType = mapItemType,
            MapResultType = mapResultType
        });
        return this;
    }

    // Overload for heterogeneous maps
    public TestGraphBuilder AddMapNode(
        string id,
        IReadOnlyDictionary<string, HPDAgent.Graph.Abstractions.Graph.Graph>? mapProcessorGraphs = null,
        string? mapRouterName = null,
        HPDAgent.Graph.Abstractions.Graph.Graph? mapDefaultGraph = null,
        string? name = null,
        int? maxParallelMapTasks = null,
        MapErrorMode? errorMode = null,
        string? mapInputChannel = null,
        string? mapOutputChannel = null)
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name ?? id,
            Type = NodeType.Map,
            MapProcessorGraphs = mapProcessorGraphs,
            MapRouterName = mapRouterName,
            MapDefaultGraph = mapDefaultGraph,
            MaxParallelMapTasks = maxParallelMapTasks,
            MapErrorMode = errorMode,
            MapInputChannel = mapInputChannel,
            MapOutputChannel = mapOutputChannel
        });
        return this;
    }

    /// <summary>
    /// Adds a fully configured node object (useful for artifact testing).
    /// </summary>
    public TestGraphBuilder AddNode(Node node)
    {
        _nodes.Add(node);
        return this;
    }

    /// <summary>
    /// Adds a custom node with full configuration support (for testing port features).
    /// </summary>
    public TestGraphBuilder AddNode(
        string id,
        string name,
        NodeType type,
        string? handlerName,
        int outputPortCount = 1)
    {
        _nodes.Add(new Node
        {
            Id = id,
            Name = name,
            Type = type,
            HandlerName = handlerName ?? string.Empty,
            OutputPortCount = outputPortCount
        });
        return this;
    }

    /// <summary>
    /// Adds an edge with optional port, priority, and condition support.
    /// </summary>
    public TestGraphBuilder AddEdge(
        string from,
        string to,
        EdgeCondition? condition = null,
        int? fromPort = null,
        int? toPort = null,
        int? priority = null)
    {
        _edges.Add(new Edge
        {
            From = from,
            To = to,
            FromPort = fromPort,
            ToPort = toPort,
            Priority = priority,
            Condition = condition
        });
        return this;
    }

    public HPDAgent.Graph.Abstractions.Graph.Graph Build()
    {
        return new HPDAgent.Graph.Abstractions.Graph.Graph
        {
            Id = _id,
            Name = _name,
            Version = _version,
            Nodes = _nodes,
            Edges = _edges,
            EntryNodeId = _entryNodeId,
            ExitNodeId = _exitNodeId
        };
    }
}
