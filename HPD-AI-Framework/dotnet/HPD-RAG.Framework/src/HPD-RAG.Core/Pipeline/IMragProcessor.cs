using HPD.RAG.Core.Context;

namespace HPD.RAG.Core.Pipeline;

/// <summary>
/// Tier 1 custom node — simple in/out transform. Zero HPD.Graph knowledge required.
/// MRAG auto-bridges this into IGraphNodeHandler&lt;MragPipelineContext&gt; internally.
/// Register in DI before MragPipeline.Build*Async is called; if not found, the build throws at startup.
/// </summary>
public interface IMragProcessor<TIn, TOut>
{
    Task<TOut> ProcessAsync(TIn input, MragProcessingContext context, CancellationToken ct);
}
