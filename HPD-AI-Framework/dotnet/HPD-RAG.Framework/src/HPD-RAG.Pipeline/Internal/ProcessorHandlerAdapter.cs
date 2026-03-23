using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;

namespace HPD.RAG.Pipeline.Internal;

/// <summary>
/// Bridges <see cref="IMragProcessor{TIn,TOut}"/> into <see cref="IGraphNodeHandler{MragPipelineContext}"/>.
/// Registered into the orchestrator's service provider by
/// <see cref="MragPipeline.AddProcessor{TProcessor}"/>.
///
/// Input key convention: "input" (single input socket).
/// Output key convention: "output" (single output socket, port 0).
/// </summary>
internal sealed class ProcessorHandlerAdapter<TIn, TOut> : IGraphNodeHandler<MragPipelineContext>
{
    private readonly string _handlerName;
    private readonly IMragProcessor<TIn, TOut> _processor;

    public ProcessorHandlerAdapter(string handlerName, IMragProcessor<TIn, TOut> processor)
    {
        _handlerName = handlerName;
        _processor = processor;
    }

    public string HandlerName => _handlerName;

    public async Task<NodeExecutionResult> ExecuteAsync(
        MragPipelineContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var start = DateTimeOffset.UtcNow;

        TIn input = inputs.Get<TIn>("input");
        var processingCtx = MragProcessingContextFactory.Create(context);

        TOut output = await _processor.ProcessAsync(input, processingCtx, cancellationToken)
            .ConfigureAwait(false);

        var duration = DateTimeOffset.UtcNow - start;

        return NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = output! },
            duration: duration.Duration(),
            metadata: new NodeExecutionMetadata { StartedAt = start });
    }
}
