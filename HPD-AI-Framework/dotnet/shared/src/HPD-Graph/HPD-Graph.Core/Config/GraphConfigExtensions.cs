using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Core.Builders;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Config;

public static class GraphConfigExtensions
{
    public static RuntimeGraph ToGraph(this GraphConfig config)
    {
        return new GraphFactory().Build(config);
    }

    public static GraphBuilder ToBuilder(this GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null)
    {
        return new GraphFactory().CreateBuilder(config, compilerOptions);
    }

    public static GraphConfig ToConfig(this RuntimeGraph graph)
    {
        return new GraphConfigExporter().Export(graph);
    }

    public static RuntimeGraph ToGraphFromFile(string path, GraphConfigCompilerOptions? compilerOptions = null)
    {
        return new GraphFactory().BuildFromFile(path, compilerOptions);
    }

    public static GraphBuilder ToBuilderFromFile(string path, GraphConfigCompilerOptions? compilerOptions = null)
    {
        return new GraphFactory().CreateBuilderFromFile(path, compilerOptions);
    }

    public static GraphConfig ReadFile(string path)
    {
        return GraphConfigSerializer.ReadConfigFile(path)
            ?? throw new InvalidOperationException($"Failed to deserialize GraphConfig from '{path}'.");
    }
}
