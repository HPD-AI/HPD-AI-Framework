using HPD.Graph.Abstractions.Context;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Handlers;

namespace HPD.Graph.Abstractions.Invocation;

public interface IGraphNodeHandlerInvoker
{
    string HandlerName { get; }
    Type HandlerType { get; }
    Type ContextType { get; }

    ValueTask<NodeExecutionResult> ExecuteAsync(
        IGraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default);
}

public interface IGraphHandlerRegistry
{
    IGraphNodeHandlerInvoker? GetInvoker(string handlerName);
    IReadOnlyList<IGraphNodeHandlerInvoker> GetAllInvokers();
}

