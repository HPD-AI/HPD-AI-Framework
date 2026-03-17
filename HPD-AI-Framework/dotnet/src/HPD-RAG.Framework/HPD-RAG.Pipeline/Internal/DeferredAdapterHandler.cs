using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Pipeline.Internal;

/// <summary>
/// Placeholder <see cref="IGraphNodeHandler{MragPipelineContext}"/> registered at build time
/// for processor, router, and raw-handler nodes.
///
/// At execution time the orchestrator calls <see cref="ExecuteAsync"/> with a
/// <see cref="MragPipelineContext"/> that carries the user's <c>IServiceProvider</c>.
/// This handler resolves the actual service (<c>IMragProcessor</c>, <c>IMragRouter</c>,
/// or <c>IGraphNodeHandler&lt;MragPipelineContext&gt;</c>) from that provider and
/// delegates execution to it.
///
/// This indirection is necessary because the pipeline is built at startup time
/// (before the user's per-execution <c>IServiceProvider</c> is known), while
/// processor/router implementations live in the user's DI container.
/// </summary>
internal sealed class DeferredAdapterHandler : IGraphNodeHandler<MragPipelineContext>
{
    private readonly string _handlerName;
    private readonly Type _serviceType;

    public DeferredAdapterHandler(string handlerName, Type serviceType)
    {
        _handlerName = handlerName;
        _serviceType = serviceType;
    }

    public string HandlerName => _handlerName;

    public Task<NodeExecutionResult> ExecuteAsync(
        MragPipelineContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Resolve from the execution-time service provider that is embedded in context.
        var resolved = context.Services.GetRequiredService(_serviceType);

        return resolved switch
        {
            IGraphNodeHandler<MragPipelineContext> rawHandler =>
                rawHandler.ExecuteAsync(context, inputs, cancellationToken),

            _ => ExecuteViaReflectionAsync(resolved, context, inputs, cancellationToken)
        };
    }

    /// <summary>
    /// Falls back to a dynamic dispatch path for IMragProcessor and IMragRouter.
    /// At runtime the generic type arguments are only known through the interface
    /// that the user's class implements, so we use a small reflection shim here.
    /// This path is only hit on first execution per adapter type; subsequent calls
    /// hit the switch above once we wrap the instance in a concrete typed adapter.
    /// </summary>
    private Task<NodeExecutionResult> ExecuteViaReflectionAsync(
        object service,
        MragPipelineContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken)
    {
        var serviceType = service.GetType();

        // Walk the interface list to find IMragProcessor<,> or IMragRouter<>
        foreach (var iface in serviceType.GetInterfaces())
        {
            if (!iface.IsGenericType) continue;
            var genDef = iface.GetGenericTypeDefinition();

            if (genDef == typeof(IMragProcessor<,>))
            {
                var typeArgs = iface.GetGenericArguments(); // [TIn, TOut]
                var adapterType = typeof(ProcessorHandlerAdapter<,>).MakeGenericType(typeArgs);
                var adapter = (IGraphNodeHandler<MragPipelineContext>)
                    Activator.CreateInstance(adapterType, _handlerName, service)!;
                return adapter.ExecuteAsync(context, inputs, cancellationToken);
            }

            if (genDef == typeof(IMragRouter<>))
            {
                var typeArgs = iface.GetGenericArguments(); // [TIn]
                // Router port count is embedded in the handler name convention: "mrag:router:{nodeId}"
                // We default to 2 here; the outer pipeline configures ports on the graph node.
                var adapterType = typeof(RouterHandlerAdapter<>).MakeGenericType(typeArgs);
                var adapter = (IGraphNodeHandler<MragPipelineContext>)
                    Activator.CreateInstance(adapterType, _handlerName, service, 2)!;
                return adapter.ExecuteAsync(context, inputs, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Service type '{serviceType.FullName}' registered for handler '{_handlerName}' " +
            $"does not implement IGraphNodeHandler<MragPipelineContext>, " +
            $"IMragProcessor<TIn, TOut>, or IMragRouter<TIn>.");
    }
}
