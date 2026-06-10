using HPD.Graph.Abstractions.Invocation;

namespace HPD.Graph.Core.Discovery;

/// <summary>
/// Default DI-backed handler registry for generated graph handler invokers.
/// </summary>
public sealed class GraphHandlerRegistry : IGraphHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IGraphNodeHandlerInvoker> _invokers;

    public GraphHandlerRegistry(IEnumerable<IGraphNodeHandlerInvoker> invokers)
    {
        _invokers = invokers.ToDictionary(invoker => invoker.HandlerName, StringComparer.Ordinal);
    }

    public IGraphNodeHandlerInvoker? GetInvoker(string handlerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);
        return _invokers.TryGetValue(handlerName, out var invoker) ? invoker : null;
    }

    public IReadOnlyList<IGraphNodeHandlerInvoker> GetAllInvokers()
    {
        return _invokers.Values.ToArray();
    }
}
