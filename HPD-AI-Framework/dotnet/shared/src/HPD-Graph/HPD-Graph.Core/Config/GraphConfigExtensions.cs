using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Core.Builders;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Config;

public static class GraphConfigExtensions
{
    public static RuntimeGraph ToGraph(this GraphConfig config)
    {
        return new GraphConfigCompiler().Compile(config);
    }

    public static GraphBuilder ToBuilder(this GraphConfig config, GraphConfigCompilerOptions? compilerOptions = null)
    {
        return new GraphBuilder(config, compilerOptions);
    }

    public static GraphConfig ToConfig(this RuntimeGraph graph)
    {
        return new GraphConfigExporter().Export(graph);
    }
}
