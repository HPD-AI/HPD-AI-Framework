using HPD.Serialization;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Storage;

namespace HPD.Graph.Abstractions.Serialization;

/// <summary>AOT-safe JSON/YAML serialization helpers for graph configuration documents.</summary>
public static class GraphConfigSerializer
{
    public static GraphConfig? ReadConfigFile(string path)
        => HpdConfigSerializer.ReadFile(path, GraphConfigJsonSerializerContext.Default.GraphConfig);

    public static ValueTask<GraphConfig?> ReadConfigFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.ReadFileAsync(
            path,
            GraphConfigJsonSerializerContext.Default.GraphConfig,
            cancellationToken);

    public static void WriteConfigFile(string path, GraphConfig config)
        => HpdConfigSerializer.WriteFile(path, config, GraphConfigJsonSerializerContext.Default.GraphConfig);

    public static ValueTask WriteConfigFileAsync(
        string path,
        GraphConfig config,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.WriteFileAsync(
            path,
            config,
            GraphConfigJsonSerializerContext.Default.GraphConfig,
            cancellationToken);

    public static string SerializeConfig(GraphConfig config, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Serialize(config, GraphConfigJsonSerializerContext.Default.GraphConfig, format);

    public static GraphConfig? DeserializeConfig(string text, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Deserialize(text, GraphConfigJsonSerializerContext.Default.GraphConfig, format);

    public static StoredGraph? ReadStoredGraphFile(string path)
        => HpdConfigSerializer.ReadFile(path, GraphConfigJsonSerializerContext.Default.StoredGraph);

    public static ValueTask<StoredGraph?> ReadStoredGraphFileAsync(
        string path,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.ReadFileAsync(
            path,
            GraphConfigJsonSerializerContext.Default.StoredGraph,
            cancellationToken);

    public static void WriteStoredGraphFile(string path, StoredGraph graph)
        => HpdConfigSerializer.WriteFile(path, graph, GraphConfigJsonSerializerContext.Default.StoredGraph);

    public static ValueTask WriteStoredGraphFileAsync(
        string path,
        StoredGraph graph,
        CancellationToken cancellationToken = default)
        => HpdConfigSerializer.WriteFileAsync(
            path,
            graph,
            GraphConfigJsonSerializerContext.Default.StoredGraph,
            cancellationToken);

    public static string SerializeStoredGraph(StoredGraph graph, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Serialize(graph, GraphConfigJsonSerializerContext.Default.StoredGraph, format);

    public static StoredGraph? DeserializeStoredGraph(string text, HpdConfigFormat format = HpdConfigFormat.Json)
        => HpdConfigSerializer.Deserialize(text, GraphConfigJsonSerializerContext.Default.StoredGraph, format);
}
