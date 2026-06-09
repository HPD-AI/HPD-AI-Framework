using System.Text.Json;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Core.Builders;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Config;

public interface IGraphFactory
{
    RuntimeGraph Build(GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null);

    RuntimeGraph BuildFromFile(string path, GraphConfigCompilerOptions? compilerOptions = null);

    GraphBuilder CreateBuilder(GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null);

    GraphBuilder CreateBuilderFromFile(string path, GraphConfigCompilerOptions? compilerOptions = null);
}

public sealed class GraphFactory : IGraphFactory
{
    public RuntimeGraph Build(GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new GraphConfigCompiler(compilerOptions).Compile(config);
    }

    public RuntimeGraph BuildFromFile(string path, GraphConfigCompilerOptions? compilerOptions = null)
        => Build(LoadConfigFile(path), compilerOptions);

    public GraphBuilder CreateBuilder(GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null)
    {
        var graph = Build(config, compilerOptions);
        return new GraphBuilder(graph)
            .WithAutoSequentialEdges(false);
    }

    public GraphBuilder CreateBuilderFromFile(string path, GraphConfigCompilerOptions? compilerOptions = null)
        => CreateBuilder(LoadConfigFile(path), compilerOptions);

    private static GraphConfig LoadConfigFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Graph configuration file path cannot be null or empty.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException($"Graph configuration file not found: {path}");

        return GraphConfigSerializer.ReadConfigFile(path)
            ?? throw new JsonException($"Failed to deserialize GraphConfig from '{path}' - result was null.");
    }
}
