using HPDAgent.Graph.Core.Builders;

namespace HPD.RAG.Pipeline;

/// <summary>
/// Graph builder extensions that embed compiled RAG pipelines as HPD.Graph subgraphs.
/// </summary>
public static class RagGraphBuilderExtensions
{
    public static GraphBuilder AddRagIngestion(
        this GraphBuilder builder,
        string nodeId,
        MragIngestionPipeline pipeline,
        Action<NodeBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.AddSubGraphNode(nodeId, pipeline.PipelineName, pipeline.Graph, configure);
    }

    public static GraphBuilder AddRagRetrieval(
        this GraphBuilder builder,
        string nodeId,
        MragRetrievalPipeline pipeline,
        Action<NodeBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.AddSubGraphNode(nodeId, pipeline.PipelineName, pipeline.Graph, configure);
    }

    public static GraphBuilder AddRagEvaluation(
        this GraphBuilder builder,
        string nodeId,
        MragEvaluationPipeline pipeline,
        Action<NodeBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(pipeline);

        return builder.AddSubGraphNode(nodeId, pipeline.PipelineName, pipeline.Graph, configure);
    }
}
