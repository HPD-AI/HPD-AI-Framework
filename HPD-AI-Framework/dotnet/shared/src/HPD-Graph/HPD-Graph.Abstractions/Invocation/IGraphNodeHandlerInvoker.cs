using HPDAgent.Graph.Abstractions.Context;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;

namespace HPDAgent.Graph.Abstractions.Invocation;

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

