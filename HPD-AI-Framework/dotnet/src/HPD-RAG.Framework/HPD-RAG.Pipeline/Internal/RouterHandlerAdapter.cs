using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;

namespace HPD.RAG.Pipeline.Internal;

/// <summary>
/// Bridges <see cref="IMragRouter{TIn}"/> into <see cref="IGraphNodeHandler{MragPipelineContext}"/>.
/// Registered into the orchestrator's service provider by
/// <see cref="MragPipeline.AddRouter{TRouter}"/>.
///
/// The router's <see cref="MragRouteResult.Port"/> maps to HPD.Graph output port index.
/// Each port's output dictionary contains a single key "output" with the routed data.
/// </summary>
internal sealed class RouterHandlerAdapter<TIn> : IGraphNodeHandler<MragPipelineContext>
{
    private readonly string _handlerName;
    private readonly IMragRouter<TIn> _router;
    private readonly int _portCount;

    public RouterHandlerAdapter(string handlerName, IMragRouter<TIn> router, int portCount)
    {
        _handlerName = handlerName;
        _router = router;
        _portCount = portCount;
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

        MragRouteResult routeResult = await _router.RouteAsync(input, processingCtx, cancellationToken)
            .ConfigureAwait(false);

        if (routeResult.Port < 0 || routeResult.Port >= _portCount)
        {
            throw new InvalidOperationException(
                $"Router '{_handlerName}' returned port {routeResult.Port} but only {_portCount} port(s) are declared. " +
                $"Increase the ports argument when calling AddRouter<T>(nodeId, ports: {routeResult.Port + 1}).");
        }

        var duration = DateTimeOffset.UtcNow - start;

        // Build port-keyed outputs — only the selected port carries data.
        var portOutputs = new Dictionary<int, Dictionary<string, object>>();
        for (int i = 0; i < _portCount; i++)
        {
            portOutputs[i] = i == routeResult.Port
                ? new Dictionary<string, object> { ["output"] = routeResult.Data }
                : new Dictionary<string, object>();
        }

        return new NodeExecutionResult.Success(
            PortOutputs: portOutputs,
            Duration: duration.Duration(),
            Metadata: new NodeExecutionMetadata { StartedAt = start });
    }
}
